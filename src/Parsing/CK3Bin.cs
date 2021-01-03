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

        private const string GAMESTATE_ENTRY = "gamestate";

        private ParseState _state;

        private CK3Bin(Stream stream, Utf8JsonWriter writer, ParseState state)
        {
            _stream = stream;
            _writer = writer;
            _readPipe = PipeReader.Create(stream);
            _state = state;
        }
        public CK3Bin(Stream stream, Utf8JsonWriter writer) : this(stream, writer, state: ParseState.Checksum)
        {
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

            var objectStack = new Stack<bool>();
            bool reachedCompressedGamestate = false;

            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    var result = await pipeReader.ReadAsync(cancelToken);
                    if (result.IsCanceled)
                    {
                        return;
                    }

                    ParseSequence(result.Buffer, ref _state, objectStack, out var consumed, out var examined);
                    pipeReader.AdvanceTo(consumed, examined);

                    if (!reachedCompressedGamestate && _state == ParseState.DecompressGamestate)
                    {
                        reachedCompressedGamestate = true;
                    }

                    if (reachedCompressedGamestate)
                    {
                        using var pipeStream = pipeReader.AsStream(true);
                        using var zip = new System.IO.Compression.ZipArchive(pipeStream, System.IO.Compression.ZipArchiveMode.Read, true, Encoding.UTF8);

                        using var gamestateStream = zip.GetEntry(GAMESTATE_ENTRY).Open();
                        _writer.WritePropertyName(GAMESTATE_ENTRY);

                        var gamestateBin = new CK3Bin(gamestateStream, _writer, ParseState.Token);
                        await gamestateBin.ParseAsync(cancelToken);
                    }
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
            ContainerToRoot,
            DecompressGamestate
        }

        private enum ContainerType
        {
            Object,
            Array
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

                        if (state == ParseState.ContainerToRoot)
                        {
                            state = ParseState.DecompressGamestate;
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
                        if (!reader.TryReadLittleEndian(out uint uintValue))
                            return false;

                        Debug.WriteLine($"uint={uintValue}");
                        _writer.WriteNumberValue(uintValue);

                        return true;
                    case SpecialTokens.ULong:
                        if (!reader.TryReadLittleEndian(out ulong ulongValue))
                            return false;

                        Debug.WriteLine($"ulong={ulongValue}");
                        _writer.WriteNumberValue(ulongValue);

                        return true;
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

            bool TryPeekContainerType(ref SequenceReader<byte> reader, out ContainerType? containerType)
            {
                var copy = reader;
                if (!copy.TryReadToken(out var firstToken))
                {
                    containerType = default;
                    return false;
                }

                if (firstToken.IsControl)
                {
                    switch (firstToken.AsSpecial())
                    {
                        //nested openers means outer array, inner container
                        case SpecialTokens.Open:
                            containerType = ContainerType.Array;
                            return true;
                        //treat empty as object
                        case SpecialTokens.Close:
                            containerType = ContainerType.Object;
                            return true;
                        default:
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
                        {
                            containerType = default;
                            return false;
                        }

                        copy.Advance(strLen);
                    }

                    if (!copy.TryReadToken(out var secondToken))
                    {
                        containerType = default;
                        return false;
                    }

                    if (secondToken.IsControl && secondToken.AsSpecial() == SpecialTokens.Equals)
                    {
                        containerType = ContainerType.Object;
                        return true;
                        //}
                        //else
                        //{
                        //    throw new InvalidOperationException("Unexpected token following idstr while peeking container type");
                    }

                    containerType = ContainerType.Array;
                    return true;
                }
            }
            #endregion

            var reader = new SequenceReader<byte>(buffer);
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
                        case SpecialTokens.Close when objectStack.Count == 1:
                            if (reader.IsNext(PKZIP_MAGIC, false))
                            {
                                state = ParseState.ContainerToRoot;
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
                //needed to finish writing container end
                case ParseState.ContainerToRoot:
                //needed for idstr object property names
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
                    if (!TryPeekContainerType(ref reader, out var containerType) || containerType == null)
                    {
                        consumed = buffer.Start;
                        examined = buffer.End;
                        return;
                    }

                    switch (containerType.Value)
                    {
                        case ContainerType.Object:
                            _writer.WriteStartObject();
                            objectStack.Push(true);
                            Debug.WriteLine("{");
                            break;
                        case ContainerType.Array:
                            _writer.WriteStartArray();
                            objectStack.Push(false);
                            Debug.WriteLine("[");
                            break;
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
