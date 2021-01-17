using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
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
        private const string GAMESTATE_ENTRY = "gamestate";
        private static readonly byte[] PKZIP_MAGIC = new[] { (byte)0x50, (byte)0x4b, (byte)0x03, (byte)0x04 };

        private readonly PipeReader _readPipe;
        private readonly Stream _stream;

        private readonly Utf8JsonWriter _writer;

        private readonly bool _parseGamestate;

        private ParseState _state;

        private CK3Bin(Stream stream, Utf8JsonWriter writer, ParseState state)
        {
            _stream = stream;
            _writer = writer;
            _readPipe = PipeReader.Create(stream);
            _state = state;
        }
        public CK3Bin(Stream stream, Utf8JsonWriter writer, bool parseGamestate = true) : this(stream, writer, state: ParseState.Checksum)
        {
            _parseGamestate = parseGamestate;
        }
        public CK3Bin(string path, Utf8JsonWriter writer, bool parseGamestate = true)
            : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), writer, parseGamestate)
        {
        }

        public Task ParseAsync(CancellationToken token = default)
            => ReadPipeAsync(_readPipe, token);

        private async Task ReadPipeAsync(PipeReader pipeReader, CancellationToken cancelToken = default)
        {
            _writer.WriteStartObject();

            var objectStack = new Stack<bool>();

            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    var result = await pipeReader.ReadAsync(cancelToken);
                    if (result.IsCanceled || result.IsCompleted)
                    {
                        break;
                    }

                    ParseSequence(result.Buffer, objectStack, out var consumed, out var examined);
                    pipeReader.AdvanceTo(consumed, examined);

                    if (_state == ParseState.DecompressGamestate)
                    {
                        if (!_parseGamestate)
                        {
                            break;
                        }

                        await using var pipeStream = pipeReader.AsStream(true);
                        using var zip = new System.IO.Compression.ZipArchive(pipeStream, System.IO.Compression.ZipArchiveMode.Read, true, Encoding.UTF8);

                        await using var gamestateStream = zip.GetEntry(GAMESTATE_ENTRY).Open();
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

        private void ParseSequence(ReadOnlySequence<byte> buffer, Stack<bool> objectStack, out SequencePosition consumed, out SequencePosition examined)
        {
            #region Reader methods

            bool TryReadChecksum(ref SequenceReader<byte> reader, out ReadOnlySpan<byte> line)
                => reader.TryReadTo(out line, (byte)'\n');

            bool TryReadLPQStr(ref SequenceReader<byte> reader, bool asPropertyName = false)
            {
                if (!reader.TryReadLittleEndian(out ushort strLen) || reader.Remaining < strLen)
                    return false;

                Span<byte> str = stackalloc byte[strLen];
                if (!reader.TryCopyTo(str))
                    return false;

                if (!asPropertyName)
                {
                    _writer.WriteStringValue(str);
                }
                else
                {
                    _writer.WritePropertyName(str);
                }

                reader.Advance(strLen);
                return true;
            }

            bool TryReadValue(ref SequenceReader<byte> reader, CK3Token token)
            {
                if (!token.IsSpecial)
                {
                    _writer.WriteStringValue(token.AsIdentifier());
                    return true;
                }

                switch (token.AsSpecial())
                {
                    case SpecialTokens.Open:
                        _state = ParseState.Container;
                        return true;
                    case SpecialTokens.Close:
                        if (objectStack.Pop())
                        {
                            _writer.WriteEndObject();
                        }
                        else
                        {
                            _writer.WriteEndArray();
                        }

                        if (_state == ParseState.ContainerToRoot)
                        {
                            _state = ParseState.DecompressGamestate;
                        }

                        return true;
                    case SpecialTokens.Int:
                        if (!reader.TryReadLittleEndian(out int intValue))
                            return false;

                        if (CK3Date.TryParse(intValue, out var date))
                        {
                            Span<byte> utf8Date = stackalloc byte[11]; //99999.99.99
                            if (!date.ToUtf8String(ref utf8Date, out int bytesWritten))
                                //This should never happen
                                throw new InvalidOperationException("utf8Date buffer was too small to format");

                            //Trim unwritten ends
                            utf8Date = utf8Date[..bytesWritten];
                            if (_state == ParseState.IdentifierKey)
                            {
                                _writer.WritePropertyName(utf8Date);
                            }
                            else
                            {
                                _writer.WriteStringValue(utf8Date);
                            }
                        }
                        else
                        {
                            if (_state == ParseState.IdentifierKey)
                            {
                                Span<byte> utf8Int = stackalloc byte[11]; //-2147483648
                                if (!Utf8Formatter.TryFormat(intValue, utf8Int, out int bytesWritten))
                                    //This should never happen
                                    throw new InvalidOperationException("utf8Int buffer was too small to format");

                                _writer.WritePropertyName(utf8Int[..bytesWritten]);
                            }
                            else
                            {
                                _writer.WriteNumberValue(intValue);
                            }
                        }

                        return true;
                    case SpecialTokens.UInt:
                        if (!reader.TryReadLittleEndian(out uint uintValue))
                            return false;

                        if (_state == ParseState.IdentifierKey)
                        {
                            Span<byte> utf8Uint = stackalloc byte[10]; //4294967295
                            if (!Utf8Formatter.TryFormat(uintValue, utf8Uint, out int bytesWritten))
                                //This should never happen
                                throw new InvalidOperationException("utf8Uint buffer was too small to format");

                            _writer.WritePropertyName(utf8Uint[..bytesWritten]);
                            return true;
                        }

                        _writer.WriteNumberValue(uintValue);
                        return true;
                    case SpecialTokens.ULong:
                        if (!reader.TryReadLittleEndian(out ulong ulongValue))
                            return false;

                        _writer.WriteNumberValue(ulongValue);
                        return true;
                    case SpecialTokens.Float:
                        if (!reader.TryRead(out float floatValue))
                            return false;

                        _writer.WriteNumberValue(floatValue);
                        return true;
                    case SpecialTokens.Bool:
                        if (!reader.TryRead(out bool boolValue))
                            return false;

                        _writer.WriteBooleanValue(boolValue);
                        return true;
                    case SpecialTokens.Double:
                        if (!reader.TryReadLittleEndian(out long ck3DoubleValue))
                            return false;

                        var doubleValue = ck3DoubleValue / 1000.0D;
                        _writer.WriteNumberValue(doubleValue);
                        return true;
                    case SpecialTokens.LPQStr:
                    case SpecialTokens.LPStr:
                        return TryReadLPQStr(ref reader, _state == ParseState.IdentifierKey);
                    case SpecialTokens.RGB:
                        if (!reader.TryReadToken(out var openToken) || openToken.AsSpecial() != SpecialTokens.Open)
                            return false;

                        //every color segment uint prefixed by unused ushort
                        //0-2   ushort:{
                        //2-4   ushort:_
                        //4-8   uint:R
                        //8-A   ushort:_
                        //A-E   uint:G
                        //E-10  ushort:_
                        //10-14 uint:B
                        //14-16 ushort:}

                        if (!reader.TryReadLittleEndian(out ushort _) || !reader.TryReadLittleEndian(out uint R))
                            return false;
                        if (!reader.TryReadLittleEndian(out ushort _) || !reader.TryReadLittleEndian(out uint G))
                            return false;
                        if (!reader.TryReadLittleEndian(out ushort _) || !reader.TryReadLittleEndian(out uint B))
                            return false;

                        if (!reader.TryReadToken(out var closeToken) || closeToken.AsSpecial() != SpecialTokens.Close)
                            return false;

                        //_writer.WriteCommentValue("RGB");
                        _writer.WriteStartArray();
                        _writer.WriteNumberValue(R);
                        _writer.WriteNumberValue(G);
                        _writer.WriteNumberValue(B);
                        _writer.WriteEndArray();
                        return true;

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
                    //might be array or object w/ typed identifier
                    if (firstToken.IsType)
                    {
                        switch (firstToken.AsSpecial())
                        {
                            case SpecialTokens.LPQStr:
                            case SpecialTokens.LPStr:
                                //need to check .Remaining to avoid a throw on .Advance with insufficient data
                                if (!copy.TryReadLittleEndian(out short strLen) || copy.Remaining < strLen)
                                {
                                    containerType = default;
                                    return false;
                                }

                                copy.Advance(strLen);
                                break;
                            case SpecialTokens.UInt:
                            case SpecialTokens.Int:
                            case SpecialTokens.Float:
                                if (!copy.TryReadLittleEndian(out int _))
                                {
                                    containerType = default;
                                    return false;
                                }
                                break;
                            case SpecialTokens.ULong:
                            case SpecialTokens.Double:
                                if (!copy.TryReadLittleEndian(out long _))
                                {
                                    containerType = default;
                                    return false;
                                }
                                break;
                            default:
                                //This is not correct, but tries to be forgiving in the case of an unexpected identifier type
                                //which wasn't skipped, so treat the container as an array and dump everything inside
                                containerType = ContainerType.Array;
                                return true;
                        }
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
            consumed = buffer.Start;
            examined = consumed;
            while (!reader.End)
            {
                CK3Token token = default;
                switch (_state)
                {
                    case ParseState.Checksum:
                        if (!TryReadChecksum(ref reader, out var checksum))
                        {
                            examined = buffer.End;
                            return;
                        }

                        _writer.WriteString("checksum", checksum);
                        _state = ParseState.Token;
                        break;
                    case ParseState.Token:
                        if (!reader.TryReadToken(out token))
                        {
                            examined = buffer.End;
                            return;
                        }
                        else if (!token.IsSpecial)
                        {
                            if (objectStack.TryPeek(out bool inObject) && !inObject)
                            {
                                _state = ParseState.Value;
                                goto InlineValue;
                            }

                            _writer.WritePropertyName(token.AsIdentifier());
                            break;
                        }

                        switch (token.AsSpecial())
                        {
                            case SpecialTokens.Equals:
                                _state = ParseState.Value;
                                break;
                            //These values can all be used as identifiers
                            case SpecialTokens.LPQStr:
                            case SpecialTokens.LPStr:
                            case SpecialTokens.Int:
                            case SpecialTokens.UInt:
                                if (objectStack.Peek())
                                {
                                    _state = ParseState.IdentifierKey;
                                }
                                goto InlineValue;
                            //
                            case SpecialTokens.Close when objectStack.Count == 1:
                                if (reader.IsNext(PKZIP_MAGIC, false))
                                {
                                    _state = ParseState.ContainerToRoot;
                                }
                                goto InlineValue;
                            case SpecialTokens.Open:
                            case SpecialTokens.Close:
                            default:
                                _state = ParseState.Value;
                                goto InlineValue;
                        }
                        break;
                    //needed to finish writing container end
                    case ParseState.ContainerToRoot:
                    //needed for idstr object property names
                    case ParseState.IdentifierKey:
                    case ParseState.Value:
                        if (!reader.TryReadToken(out token))
                        {
                            examined = buffer.End;
                            return;
                        }
                    InlineValue:
                        var initState = _state;
                        if (!TryReadValue(ref reader, token))
                        {
                            examined = buffer.End;
                            return;
                        }

                        _state = initState == _state ? ParseState.Token : _state;
                        break;
                    case ParseState.Container:
                        if (!TryPeekContainerType(ref reader, out var containerType) || containerType == null)
                        {
                            examined = buffer.End;
                            return;
                        }

                        switch (containerType.Value)
                        {
                            case ContainerType.Object:
                                _writer.WriteStartObject();
                                objectStack.Push(true);
                                break;
                            case ContainerType.Array:
                                _writer.WriteStartArray();
                                objectStack.Push(false);
                                break;
                        }

                        //Debug.Indent();
                        _state = ParseState.Token;
                        break;
                    case ParseState.DecompressGamestate:
                        return;
                }

                consumed = reader.Position;
                examined = consumed;
            }
        }
    }
}
