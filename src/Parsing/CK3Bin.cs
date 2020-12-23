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

        public bool IsControl => AsControl() switch
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

            var state = ParseState.Checksum;
            Stack<Action> close = new();

            static void DefaultClose() => throw new InvalidOperationException("Close was called without being set from open");
            close.Push(DefaultClose);

            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    var result = await pipeReader.ReadAsync(cancelToken);
                    if (result.IsCanceled)
                    {
                        return;
                    }

                    ParseSequence(result.Buffer, ref state, close, out var consumed, out var examined);
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

        private enum ParseState
        {
            Checksum,
            Token,
            Identifier,
            IdentifierKey,
            Value
        }

        private void ParseSequence(ReadOnlySequence<byte> buffer, ref ParseState state, Stack<Action> close, out SequencePosition consumed, out SequencePosition examined)
        {
            #region Reader methods

            bool TryReadChecksum(ref SequenceReader<byte> reader, out ReadOnlySpan<byte> line)
                => reader.TryReadTo(out line, (byte)'\n');

            bool TryReadLPQStr(ref SequenceReader<byte> reader, bool asPropertyName = false)
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

                if (!asPropertyName)
                {
                    _writer.WriteStringValue(str);
                    Debug.WriteLine($"str={Encoding.UTF8.GetString(str)}");
                }
                else
                {
                    _writer.WritePropertyName(str);
                    Debug.WriteLine($"idstr={Encoding.UTF8.GetString(str)}");
                }

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

                if (!TryReadValue(ref reader, default))//, token))//CK3Type type))
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

            bool TryReadValue(ref SequenceReader<byte> reader, ParseState state)
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
                    _writer.WriteStringValue(identifier);
                    return true;
                }

                switch (token.AsControl())
                {
                    case ControlTokens.Open:
                        if (!TryReadToken(ref reader, out var isObjToken))
                        {
                            return false;
                        }

                        void WriteObject()
                        {
                            _writer.WriteStartObject();
                            close.Push(_writer.WriteEndObject);
                            Debug.WriteLine("{");
                        }

                        void WriteArray()
                        {
                            _writer.WriteStartArray();
                            close.Push(_writer.WriteEndArray);
                            Debug.WriteLine("[");
                        }

                        if (!isObjToken.IsControl)
                        {
                            WriteObject();
                        } //gene
                        else if (isObjToken.AsControl() == ControlTokens.LPQStr)
                        {
                            var copy = reader;
                            if (!copy.TryReadLittleEndian(out ushort strLen))
                            {
                                return false;
                            }

                            copy.Advance(strLen);
                            if (!TryReadToken(ref copy, out var detectToken))
                            {
                                return false;
                            }

                            if (detectToken.IsControl && detectToken.AsControl() == ControlTokens.Equals)
                            {
                                WriteObject();
                            }
                            else
                            {
                                WriteArray();
                            }
                        }
                        else
                        {
                            WriteArray();
                        }

                        Debug.Indent();
                        reader.Rewind(sizeof(ushort));

                        return true;
                    case ControlTokens.Close:
#if DEBUG
                        Debug.Unindent();
                        switch (close.Peek().Method.Name)
                        {
                            case nameof(_writer.WriteEndArray):
                                Debug.WriteLine("]");
                                break;
                            case nameof(_writer.WriteEndObject):
                                Debug.WriteLine("}");
                                break;
                        }
#endif

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
                            _writer.WriteNumberValue(intValue);

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
                            _writer.WriteNumberValue(uintValue);

                            return true;
                        }
                    case ControlTokens.Float:
                        Debug.Assert(reader.UnreadSpan.Length >= sizeof(float));

                        var floatBytes = reader.UnreadSpan.Slice(0, sizeof(float));
                        var floatValue = MemoryMarshal.AsRef<float>(floatBytes);

                        Debug.WriteLine($"float={floatValue}");
                        _writer.WriteNumberValue(floatValue);

                        reader.Advance(sizeof(float));
                        return true;
                    case ControlTokens.LPQStr:
                        return TryReadLPQStr(ref reader, state == ParseState.IdentifierKey);

                    default:
                        throw new InvalidOperationException();
                }
            }
            #endregion

            var reader = new SequenceReader<byte>(buffer);
            //ReadOnlySpan<byte> justRead;
            //while (!reader.TryReadTo(out justRead, PKZIP_MAGIC, false))
            //{
            //    Debug.WriteLine("NO");
            //    reader.Advance(reader.UnreadSpan.Length);

            //    if (reader.End) break;
            //}
            //Debug.WriteLine("YES");

            switch (state)
            {
                case ParseState.Checksum:
                    if (!TryReadChecksum(ref reader, out var checksum))
                    {
                        consumed = buffer.Start;
                        examined = buffer.End;
                        return;
                    }

                    _writer.WriteString("checksum", checksum);

                    state = ParseState.Token;
                    break;
                case ParseState.Token:
                    if (!TryReadToken(ref reader, out var token))
                    {
                        consumed = buffer.Start;
                        examined = buffer.End;
                        return;
                    }
                    else if (!token.IsControl)
                    {
                        state = ParseState.Identifier;
                        consumed = buffer.Start;
                        examined = consumed;
                        return;
                    }

                    switch (token.AsControl())
                    {
                        case ControlTokens.Equals:
                            state = ParseState.Value;
                            break;
                        case ControlTokens.LPQStr:
                            state = ParseState.IdentifierKey;
                            goto default;
                        case ControlTokens.Open:
                        case ControlTokens.Close:
                        default:
                            state = state != ParseState.Token ? state : ParseState.Value;
                            consumed = buffer.Start;
                            examined = consumed;
                            return;
                    }
                    break;
                case ParseState.Identifier:
                    if (!TryReadToken(ref reader, out var idToken))
                    {
                        consumed = buffer.Start;
                        examined = buffer.End;
                        return;
                    }

                    Debug.WriteLine($"tag={idToken.AsIdentifier()}");
                    _writer.WritePropertyName(idToken.AsIdentifier());

                    state = ParseState.Token;
                    break;
                case ParseState.IdentifierKey:
                case ParseState.Value:
                    if (!TryReadValue(ref reader, state))
                    {
                        consumed = buffer.Start;
                        examined = buffer.End;
                        return;
                    }

                    //don't overwrite if TryReadValue changed state
                    state = ParseState.Token;
                    break;
            }

            //Debug.WriteLine($"state: {state} consumed: {reader.Consumed} remaining: {reader.Remaining} end: {reader.End}");
            consumed = reader.Position;
            examined = consumed;
        }
    }
}
