using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LibCK3.Parsing
{
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
            var objectStack = new Stack<bool>();

            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    var result = await pipeReader.ReadAsync(cancelToken);
                    if (result.IsCanceled)
                    {
                        return;
                    }

                    ParseSequence(result.Buffer, ref state, objectStack, out var consumed, out var examined);
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
            Value,
            Container,
        }

        private struct CK3Element
        {
            public JsonEncodedText Identifier;
            public bool? IsContainer;
            public bool? IsObject;
            public bool? IsArray;
        }

        private void ParseSequence(ReadOnlySequence<byte> buffer, ref ParseState state, Stack<bool> objectStack, out SequencePosition consumed, out SequencePosition examined)
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

            bool TryReadValue(ref SequenceReader<byte> reader, ref ParseState state)
            {
                if (!reader.TryReadToken(out var token))
                    return false;

                if (!token.IsSpecial)
                {
                    var identifier = token.AsIdentifier();
                    Debug.WriteLine($"identifier={identifier}");
                    _writer.WriteStringValue(identifier);
                    return true;
                }

                switch (token.AsSpecial())
                {
                    case SpecialTokens.Open:
                        state = ParseState.Container;
                        return true;
                    case SpecialTokens.Close:
                        Debug.Unindent();

                        if (objectStack.Pop())
                        {
                            Debug.WriteLine("}");
                            _writer.WriteEndObject();
                        }
                        else
                        {
                            Debug.WriteLine("]");
                            _writer.WriteEndArray();
                        }

                        return true;
                    case SpecialTokens.Int:
                        {
                            if (!reader.TryReadLittleEndian(out int intValue))
                                return false;

                            Debug.WriteLine($"int={intValue}");
                            _writer.WriteNumberValue(intValue);

                            return true;
                        }
                    case SpecialTokens.UInt:
                        {
                            if (!reader.TryReadLittleEndian(out int intValue))
                                return false;

                            var uintValue = (uint)intValue;
                            Debug.WriteLine($"uint={uintValue}");
                            _writer.WriteNumberValue(uintValue);

                            return true;
                        }
                    case SpecialTokens.Float:
                        if (!reader.TryRead(out float floatValue))
                            return false;

                        Debug.WriteLine($"float={floatValue}");
                        _writer.WriteNumberValue(floatValue);

                        return true;
                    case SpecialTokens.Bool:
                        if (!reader.TryRead(out bool boolValue))
                            return false;

                        Debug.WriteLine($"bool={boolValue}");
                        _writer.WriteBooleanValue(boolValue);

                        return true;
                    case SpecialTokens.LPQStr:
                        return TryReadLPQStr(ref reader, state == ParseState.IdentifierKey);

                    default:
                        throw new InvalidOperationException("Unknown token while parsing value");
                }
            }

            bool TryPeekContainerType(ref SequenceReader<byte> reader, ref CK3Element element)
            {
                var copy = reader;
                if (!copy.TryReadToken(out var firstToken))
                    return false;

                if (firstToken.IsControl)
                {
                    if (firstToken.AsSpecial() == SpecialTokens.Close)
                    {
                        element.IsObject = true;
                        return true;
                    }
                    else
                    {
                        throw new InvalidOperationException("Unexpected control token while peeking container type");
                    }
                }
                else /* is type or identifier, better check if this is a pair */
                {
                    //might be array or object w/ lpqstr identifier
                    if (firstToken.AsSpecial() == SpecialTokens.LPQStr)
                    {
                        //need to check .Remaining to avoid a throw on .Advance with insufficient data
                        if (!copy.TryReadLittleEndian(out short strLen) || copy.Remaining < strLen)
                            return false;

                        copy.Advance(strLen);
                    }

                    if (!copy.TryReadToken(out var secondToken))
                        return false;

                    if (secondToken.IsControl && secondToken.AsSpecial() == SpecialTokens.Equals)
                    {
                        element.IsObject = true;
                        return true;
                        //}
                        //else
                        //{
                        //    throw new InvalidOperationException("Unexpected token following idstr while peeking container type");
                    }

                    element.IsArray = true;
                    return true;
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
                    if (!reader.TryReadToken(out var token))
                    {
                        consumed = buffer.Start;
                        examined = buffer.End;
                        return;
                    }
                    else if (!token.IsSpecial)
                    {
                        state = ParseState.Identifier;
                        consumed = buffer.Start;
                        examined = consumed;
                        return;
                    }

                    switch (token.AsSpecial())
                    {
                        case SpecialTokens.Equals:
                            state = ParseState.Value;
                            break;
                        case SpecialTokens.LPQStr:
                            if (objectStack.Peek())
                            {
                                state = ParseState.IdentifierKey;
                            }
                            goto default;
                        case SpecialTokens.Open:
                        case SpecialTokens.Close:
                        default:
                            state = state != ParseState.Token ? state : ParseState.Value;
                            consumed = buffer.Start;
                            examined = consumed;
                            return;
                    }
                    break;
                case ParseState.Identifier:
                    if (!reader.TryReadToken(out var idToken))
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
                    var initState = state;
                    if (!TryReadValue(ref reader, ref state))
                    {
                        consumed = buffer.Start;
                        examined = buffer.End;
                        return;
                    }

                    state = initState == state ? ParseState.Token : state;
                    break;
                case ParseState.Container:
                    var element = new CK3Element();
                    if (!TryPeekContainerType(ref reader, ref element))
                    {
                        consumed = buffer.Start;
                        examined = buffer.End;
                        return;
                    }

                    Debug.Assert(element.IsArray.GetValueOrDefault() ^ element.IsObject.GetValueOrDefault());

                    if (element.IsObject.GetValueOrDefault())
                    {
                        _writer.WriteStartObject();
                        objectStack.Push(true);
                        Debug.WriteLine("{");
                    }
                    else if (element.IsArray.GetValueOrDefault())
                    {
                        _writer.WriteStartArray();
                        objectStack.Push(false);
                        Debug.WriteLine("[");
                    }

                    Debug.Indent();
                    state = ParseState.Token;

                    break;
            }

            //Debug.WriteLine($"state: {state} consumed: {reader.Consumed} remaining: {reader.Remaining} end: {reader.End}");
            consumed = reader.Position;
            examined = consumed;
        }
    }
}
