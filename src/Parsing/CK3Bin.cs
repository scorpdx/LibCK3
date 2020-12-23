using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Threading;
using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace LibCK3.Parsing
{
    public readonly ref struct CK3Element
    {
        public readonly CK3Type Type;
        public readonly ReadOnlySpan<byte> Content;

        public CK3Element(CK3Type type, ReadOnlySpan<byte> content)
        {
            Type = type;
            Content = content;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    [DebuggerDisplay("(ID:{ID}, Control: {IsControl}, Identifier: {AsIdentifier()})")]
    internal readonly ref struct CK3Token
    {
        private readonly ushort ID;

        public CK3Token(ushort ID)
        {
            this.ID = ID;
        }

        public bool IsControl => ((ControlTokens)ID) switch
        {
            //
            ControlTokens.Equals => true,
            ControlTokens.Open => true,
            ControlTokens.Close => true,
            //type
            ControlTokens.Int => true,
            ControlTokens.Float => true,
            ControlTokens.LPQStr => true,
            ControlTokens.UInt => true,
            _ => false
        };

        public ControlTokens AsControl() => (ControlTokens)ID;

        public string AsIdentifier() => CK3Tokens.Tokens[ID];
    }

    public class CK3Bin
    {
        private static readonly byte[] PKZIP_MAGIC = new[] { (byte)0x50, (byte)0x4b, (byte)0x03, (byte)0x04 };

        private readonly PipeReader _readPipe;
        private readonly Stream _stream;

        private readonly Utf8JsonWriter _writer;

        public CK3Bin(Stream stream, Utf8JsonWriter writer)
        {
            _stream = stream;
            _writer = writer;
            _readPipe = PipeReader.Create(stream);
        }
        public CK3Bin(string path, Utf8JsonWriter writer)
            : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), writer)
        {
        }

        public Task ParseAsync(CancellationToken token = default)
            => ReadPipeAsync(_readPipe, token);

        private async Task ReadPipeAsync(PipeReader pipeReader, CancellationToken cancelToken = default)
        {
            _writer.WriteStartObject();

            bool hasReadChecksum = false;
            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    var result = await pipeReader.ReadAsync(cancelToken);
                    if (result.IsCanceled)
                    {
                        return;
                    }

                    ParseSequence(result.Buffer, ref hasReadChecksum, out var consumed, out var examined);
                    pipeReader.AdvanceTo(consumed, examined);
                }
                cancelToken.ThrowIfCancellationRequested();
            }
            finally
            {
                await _readPipe.CompleteAsync();
            }

            _writer.WriteEndObject();
        }

        private void ParseSequence(ReadOnlySequence<byte> buffer, ref bool hasReadChecksum, out SequencePosition consumed, out SequencePosition examined)
        {
            void DefaultClose() => throw new InvalidOperationException("Close was called without being set from open");

            Stack<Action> close = new();
            close.Push(DefaultClose);

            bool valueMode = false;

            bool TryReadChecksum(ref SequenceReader<byte> reader, out ReadOnlySpan<byte> line)
                => reader.TryReadTo(out line, (byte)'\n');

            bool TryReadLPQStr(ref SequenceReader<byte> reader, string propertyName = default)
            {
                if (!reader.TryReadLittleEndian(out ushort strLen))
                {
                    return false;
                }

                Span<byte> str = stackalloc byte[strLen];
                if (!reader.TryCopyTo(str))
                {
                    reader.Rewind(sizeof(ushort));
                    return false;
                }

                if (valueMode)
                {
                    _writer.WriteStringValue(str);
                }
                else
                {
                    _writer.WriteString(propertyName, str);
                }

                Debug.WriteLine($"[str]{propertyName}={Encoding.UTF8.GetString(str)}");
                reader.Advance(strLen);
                return true;
            }

            bool TryReadPair(ref SequenceReader<byte> reader)
            {
                if (!TryReadToken(ref reader, out var token))
                    return false;

                if (!token.IsControl)
                {
                    //is identifier, so read the '='
                    if (!TryReadToken(ref reader, out var controlToken))
                    {
                        reader.Rewind(sizeof(short));
                        return false;
                    }

                    switch (controlToken.AsControl())
                    {
                        case ControlTokens.Equals:
                            break;
                        default:
                            _writer.Flush();
                            throw new InvalidOperationException();
                    }
                    Debug.WriteLine($"tag={token.AsIdentifier()}");
                    _writer.WritePropertyName(token.AsIdentifier());
                }
                else
                {
                    reader.Rewind(sizeof(short));
                }

                if (!token.IsControl && token.AsIdentifier() == "genes")
                {
                    Debug.WriteLine(":");
                }
                if (!TryReadValue(ref reader, token))//CK3Type type))
                    return false;

                //switch(type)
                //{
                //}

                return true;
            }

            bool TryReadToken(ref SequenceReader<byte> reader, out CK3Token token)
            {
                if (!reader.TryReadLittleEndian(out ushort id))
                {
                    token = default;
                    return false;
                }

                token = new CK3Token(id);
                return true;
            }

            bool TryReadIdentifier(ref SequenceReader<byte> reader, out string identifier)
            {
                if (!TryReadToken(ref reader, out var firstToken))
                {
                    identifier = default;
                    return false;
                }

                if (!firstToken.IsControl)
                {
                    identifier = firstToken.AsIdentifier();
                    return true;
                }
                //else if (firstToken.AsControl() == ControlTokens.LPQStr && reader.TryReadLPQStr(out identifier))
                //{
                //    return true;
                //}
                else
                {
                    reader.Rewind(sizeof(ushort));

                    identifier = default;
                    return false;
                }
            }

            (bool isArray, bool isObject)? PeekType(ref SequenceReader<byte> reader, out CK3Token token)
            {
                if (!TryReadToken(ref reader, out token))
                {
                    return null;
                }

                try
                {
                    if (!TryReadToken(ref reader, out var newtoken))
                    {
                        return null;
                    }

                    if (newtoken.IsControl && newtoken.AsControl() == ControlTokens.Close)
                    {
                        return (isArray: false, isObject: true);
                    }
                    reader.Rewind(sizeof(ushort));

                    if (TryReadIdentifier(ref reader, out _)
                        && TryReadToken(ref reader, out var newControlToken)
                        && newControlToken.IsControl && newControlToken.AsControl() == ControlTokens.Equals)
                    {
                        return (isArray: false, isObject: true);
                    }
                    else
                    {
                        return (isArray: true, isObject: false);
                    }

                    try
                    {
                        if (!TryReadToken(ref reader, out var newcontroltoken))
                        {
                            return null;
                        }

                        if (newcontroltoken.IsControl && newcontroltoken.AsControl() == ControlTokens.Equals)
                        {
                            return (isArray: false, isObject: true);
                        }
                    }
                    finally
                    {
                        reader.Rewind(sizeof(ushort));
                    }
                }
                finally
                {
                    reader.Rewind(sizeof(ushort));
                }

            }

            bool TryReadValue(ref SequenceReader<byte> reader, CK3Token prevToken)
            {
                if (!reader.TryReadLittleEndian(out ushort id))
                {
                    return false;
                }

                var token = new CK3Token(id);
                if (!token.IsControl)
                {
                    var identifier = token.AsIdentifier();
                    Debug.WriteLine($"identifier={identifier}");
                    if (prevToken.IsControl)
                    {
                        _writer.WriteStringValue(identifier);
                    }
                    else
                    {
                        _writer.WriteString(prevToken.AsIdentifier(), identifier);
                    }
                    return true;
                }

                switch (token.AsControl())
                {
                    case ControlTokens.Open:
                        Debug.WriteLine("{");
                        Debug.Indent();

                        SequenceReader<byte> copy = reader;
                        try
                        {
                            ReadOnlySpan<byte> delim = new[] { (byte)ControlTokens.Open, (byte)ControlTokens.Close, (byte)ControlTokens.Equals };
                            int depth = 0;
                        SpitTake:
                            if (!reader.TryAdvanceToAny(delim, false))
                            {
                                return false;
                            }
                            else if (reader.TryPeek(out byte foundDelim))
                            {
                                var delimAsControl = (ControlTokens)foundDelim;
                                switch (delimAsControl)
                                {
                                    case ControlTokens.Open when reader.TryPeek(out byte openPeek) && ((ControlTokens)openPeek) == ControlTokens.Close:
                                    case ControlTokens.Equals when depth == 0:
                                        Debug.WriteLine($"root container is an object");
                                        _writer.WriteStartObject();
                                        close.Push(_writer.WriteEndObject);
                                        return true;
                                    case ControlTokens.Open:
                                        depth++;
                                        goto SpitTake;
                                    case ControlTokens.Close when depth > 1:
                                        depth--;
                                        break;
                                    case ControlTokens.Close:
                                        Debug.WriteLine($"root container is an array");
                                        _writer.WriteStartArray();
                                        close.Push(_writer.WriteEndArray);
                                        return true;
                                }
                            }
                            return false;
                        }
                        finally
                        {
                            reader = copy;
                        }
                    case ControlTokens.Close:
                        Debug.WriteLine("}");
                        Debug.Unindent();

                        var closer = close.Pop();
                        closer();

                        return true;
                    case ControlTokens.Int:
                        {
                            if (!reader.TryReadLittleEndian(out int intValue))
                            {
                                //element = default;
                                return false;
                            }

                            Debug.WriteLine($"int={intValue}");
                            Debug.Assert(!prevToken.IsControl);
                            _writer.WriteNumber(prevToken.AsIdentifier(), intValue);

                            return true;
                        }
                    case ControlTokens.UInt:
                        {
                            if (!reader.TryReadLittleEndian(out int intValue))
                            {
                                //element = default;
                                return false;
                            }

                            var uintValue = (uint)intValue;
                            Debug.WriteLine($"uint={uintValue}");
                            Debug.Assert(!prevToken.IsControl);
                            _writer.WriteNumber(prevToken.AsIdentifier(), uintValue);

                            return true;
                        }
                    case ControlTokens.Float:
                        Debug.Assert(reader.UnreadSpan.Length >= sizeof(float));

                        var floatBytes = reader.UnreadSpan.Slice(0, sizeof(float));
                        var floatValue = MemoryMarshal.AsRef<float>(floatBytes);

                        Debug.WriteLine($"float={floatValue}");
                        if (prevToken.IsControl)
                        {
                            _writer.WriteNumberValue(floatValue);
                        }
                        else
                        {
                            _writer.WriteNumber(prevToken.AsIdentifier(), floatValue);
                        }

                        reader.Advance(sizeof(float));
                        return true;
                    case ControlTokens.LPQStr:
                        return TryReadLPQStr(ref reader, prevToken.IsControl ? default : prevToken.AsIdentifier());

                    default:
                        throw new InvalidOperationException();
                }
            }

            var reader = new SequenceReader<byte>(buffer);
            //ReadOnlySpan<byte> justRead;
            //while (!reader.TryReadTo(out justRead, PKZIP_MAGIC, false))
            //{
            //    Debug.WriteLine("NO");
            //    reader.Advance(reader.UnreadSpan.Length);

            //    if (reader.End) break;
            //}
            //Debug.WriteLine("YES");

            if (!hasReadChecksum)
            {
                if (!TryReadChecksum(ref reader, out var checksum))
                {
                    consumed = buffer.Start;
                    examined = buffer.End;
                    return;
                }

                _writer.WriteString("checksum", checksum);
                hasReadChecksum = true;
            }

            while (TryReadPair(ref reader))//, out var token, out var value))
            {
                Debug.WriteLine($"consumed: {reader.Consumed} remaining: {reader.Remaining} end: {reader.End}");
            }

            consumed = reader.Position;
            examined = consumed;

            //while(reader.TryAdvanceTo(PKZIP_MAGIC[0], false) && reader.TryReadLittleEndian(out int magic))
            //{
            //    Debug.WriteLine(reader.Position);
            //    if (magic == PKZIP_MAGIC_INT_LE)
            //    {
            //        Console.WriteLine("ok");
            //    }
            //}
        }
    }
}
