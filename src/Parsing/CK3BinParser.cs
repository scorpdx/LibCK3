using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace LibCK3.Parsing
{
    internal sealed class CK3BinParser
    {
        private const int CHECKSUM_LENGTH = 23; //"SAV" + checksum[20], followed by '\n' delimiter

        private static readonly byte[] PKZIP_MAGIC = new[] { (byte)0x50, (byte)0x4b, (byte)0x03, (byte)0x04 };
        private static ReadOnlySpan<byte> PkZipMagic => PKZIP_MAGIC;

        private static readonly byte[] EQUAL_BYTES = BitConverter.GetBytes((ushort)SpecialTokens.Equals);
        private static ReadOnlySpan<byte> EqualBytes => EQUAL_BYTES;

        internal enum ParseState
        {
            Checksum,
            Token,
            IdentifierKey,
            Value,
            HiddenValue,
            Container,
            ContainerToRoot,
            DecompressGamestate
        }

        private enum ContainerType
        {
            Object,
            Array,
            HiddenObject
        }

        private ParseState _state;
        public ParseState State => _state;

        private readonly Stack<ContainerType> _containerStack;
        private readonly Stack<ValueOverlayFlags> _overlayStack;
        private readonly Utf8JsonWriter _writer;

        public CK3BinParser(ParseState initialState, Utf8JsonWriter writer)
        {
            _state = initialState;
            _writer = writer;
            _containerStack = new Stack<ContainerType>();
            _overlayStack = new Stack<ValueOverlayFlags>();
            _overlayStack.Push(default);
        }

        private void HandleTokenOverlay(CK3Token token)
        {
            var currentOverlay = _overlayStack.Pop();
            var mask = token.GetOverlay();

            //Last flattened token
            if (currentOverlay.HasFlag(ValueOverlayFlags.Flatten) && !mask.HasFlag(ValueOverlayFlags.Flatten))
            {
                currentOverlay &= ~ValueOverlayFlags.Flatten;
                _writer.WriteEndArray();
                _writer.WritePropertyName(token.AsIdentifier());
            }
            //We don't write property names for flattened tokens except the first
            else if (!currentOverlay.HasFlag(ValueOverlayFlags.Flatten))
            {
                _writer.WritePropertyName(token.AsIdentifier());
            }

            //First flattened token
            if (mask.HasFlag(ValueOverlayFlags.Flatten) && !currentOverlay.HasFlag(ValueOverlayFlags.Flatten))
            {
                _writer.WriteStartArray();
            }

            _overlayStack.Push(currentOverlay | mask);
        }

        private bool HandleInlineValue(ref SequenceReader<byte> reader, CK3Token token)
        {
            var initState = _state;
            if (!TryReadValue(ref reader, token))
            {
                return false;
            }

            if(_state == ParseState.HiddenValue)
            {
                _containerStack.Pop();
                _writer.WriteEndObject();
            }

            _state = initState == _state ? ParseState.Token : _state;
            return true;
        }

        public void ParseSequence(ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
        {
            var reader = new SequenceReader<byte>(buffer);
            consumed = buffer.Start;
            examined = consumed;
            while (!reader.End)
            {
                CK3Token token;
                switch (_state)
                {
                    case ParseState.Checksum:
                        if (!TryReadChecksum(ref reader, out var checksum))
                        {
                            //if we can't find a checksum within the first CHECKSUM_LENGTH bytes, skip it
                            if (reader.Consumed > CHECKSUM_LENGTH)
                            {
                                _state = ParseState.Token;
                                return;
                            }

                            goto FailToParse;
                        }

                        _writer.WriteString("checksum", checksum);
                        _state = ParseState.Token;
                        break;
                    case ParseState.Token:
                        if (!reader.TryReadToken(out token))
                        {
                            goto FailToParse;
                        }
                        else if (!token.IsSpecial)
                        {
                            if (_containerStack.TryPeek(out var container) && container == ContainerType.Array)
                            {
                                _state = ParseState.Value;
                                goto InlineValue;
                            }

                            HandleTokenOverlay(token);
                            break;
                        }

                        switch (token.AsSpecial())
                        {
                            case SpecialTokens.Equals when _containerStack.TryPeek(out var container) && container == ContainerType.HiddenObject:
                                _state = ParseState.HiddenValue;
                                break;
                            case SpecialTokens.Equals:
                                _state = ParseState.Value;
                                break;
                            //These values can all be used as identifiers
                            case SpecialTokens.LPQStr:
                            case SpecialTokens.LPStr:
                            case SpecialTokens.Int:
                            case SpecialTokens.UInt:
                                if (_containerStack.Peek() == ContainerType.Object)
                                {
                                    _state = ParseState.IdentifierKey;
                                }
                                goto InlineValue;
                            //
                            case SpecialTokens.Close when _containerStack.Count == 1:
                                _state = ParseState.ContainerToRoot;
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
                    //needed for hidden objects
                    case ParseState.HiddenValue:
                    case ParseState.Value:
                        if (!reader.TryReadToken(out token))
                        {
                            goto FailToParse;
                        }
                    InlineValue:
                        if (!HandleInlineValue(ref reader, token))
                        {
                            goto FailToParse;
                        }
                        break;
                    case ParseState.Container:
                        if (!TryPeekContainerType(reader, out var containerType) || containerType == null)
                        {
                            goto FailToParse;
                        }

                        switch (containerType.Value)
                        {
                            case ContainerType.Object:
                                _writer.WriteStartObject();
                                _containerStack.Push(ContainerType.Object);
                                break;
                            case ContainerType.Array:
                                _writer.WriteStartArray();
                                _containerStack.Push(ContainerType.Array);
                                break;
                        }

                        _state = ParseState.Token;
                        break;
                    case ParseState.DecompressGamestate:
                        return;
                }

                consumed = reader.Position;
                examined = consumed;
            }
            return;
        FailToParse:
            examined = buffer.End;
            return;
        }

        static bool TryReadChecksum(ref SequenceReader<byte> reader, out ReadOnlySpan<byte> line)
            => reader.TryReadTo(out line, (byte)'\n') && line.Length == CHECKSUM_LENGTH;

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

        bool HiddenObjectAhead(ref SequenceReader<byte> reader)
        {
            if (_containerStack.TryPeek(out var inObject) && inObject == ContainerType.Array && reader.IsNext(EqualBytes))
            {
                //open object before writing property name
                _containerStack.Push(ContainerType.HiddenObject);
                _writer.WriteStartObject();
                return true;
            }

            return false;
        }

        bool ShouldWriteIdentifier(ref SequenceReader<byte> reader)
            => _state == ParseState.IdentifierKey || (_overlayStack.Peek().HasFlag(ValueOverlayFlags.HiddenObjectContainer) && HiddenObjectAhead(ref reader));

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
                    if (_overlayStack.Peek().HasFlag(ValueOverlayFlags.KeepForChildren))
                    {
                        _overlayStack.Push(_overlayStack.Peek() & ~ValueOverlayFlags.KeepForChildren);
                    }
                    else
                    {
                        _overlayStack.Push(ValueOverlayFlags.None);
                    }

                    _state = ParseState.Container;
                    return true;
                case SpecialTokens.Close:
                    switch (_containerStack.Pop())
                    {
                        case ContainerType.Object:
                            _writer.WriteEndObject();
                            break;
                        case ContainerType.Array:
                            _writer.WriteEndArray();
                            break;
                        default: throw new InvalidOperationException("Unexpected container type in stack during close");
                    }

                    _overlayStack.Pop();

                    if (_state == ParseState.ContainerToRoot && reader.IsNext(PkZipMagic, false))
                    {
                        _state = ParseState.DecompressGamestate;
                    }

                    return true;
                case SpecialTokens.Int:
                    if (!reader.TryReadLittleEndian(out int intValue))
                        return false;

                    if (_overlayStack.Peek().HasFlag(ValueOverlayFlags.AsDate))
                    {
                        if (!CK3Date.TryParse(intValue, out var date))
                            throw new InvalidOperationException($"Failed to parse date but had {nameof(ValueOverlayFlags.AsDate)} overlay");

                        Span<byte> utf8Date = stackalloc byte[11]; //99999.99.99
                        if (!date.ToUtf8String(ref utf8Date, out int bytesWritten))
                            //This should never happen
                            throw new InvalidOperationException("utf8Date buffer was too small to format");

                        //Trim unwritten ends
                        utf8Date = utf8Date[..bytesWritten];
                        if (ShouldWriteIdentifier(ref reader))
                        {
                            _writer.WritePropertyName(utf8Date);
                        }
                        else
                        {
                            _writer.WriteStringValue(utf8Date);
                        }

                        var overlay = _overlayStack.Pop();
                        if (!overlay.HasFlag(ValueOverlayFlags.Repeats))
                        {
                            overlay &= ~ValueOverlayFlags.AsDate;
                        }
                        _overlayStack.Push(overlay);
                    }
                    else
                    {
                        if (ShouldWriteIdentifier(ref reader))
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

                    if (ShouldWriteIdentifier(ref reader))
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

                    double doubleValue;
                    if (_overlayStack.Peek().HasFlag(ValueOverlayFlags.AsQ))
                    {
                        var scaledDouble = Math.ScaleB(ck3DoubleValue, -15);
                        doubleValue = Math.Round(scaledDouble, digits: 5);

                        var overlay = _overlayStack.Pop();
                        if (!overlay.HasFlag(ValueOverlayFlags.Repeats))
                        {
                            overlay &= ~ValueOverlayFlags.AsQ;
                        }
                        _overlayStack.Push(overlay);
                    }
                    else
                    {
                        doubleValue = ck3DoubleValue / 1000D;
                    }

                    _writer.WriteNumberValue(doubleValue);
                    return true;
                case SpecialTokens.LPQStr:
                case SpecialTokens.LPStr:
                    //No hidden value peeking supported, must come after value read
                    return TryReadLPQStr(ref reader, _state == ParseState.IdentifierKey);
                case SpecialTokens.RGB:
                    if (!reader.TryReadToken(out var openToken))
                        return false;

                    Debug.Assert(openToken.AsSpecial() == SpecialTokens.Open);

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

                    if (!reader.TryReadToken(out var closeToken))
                        return false;

                    Debug.Assert(closeToken.AsSpecial() == SpecialTokens.Close);

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

        static bool TryPeekContainerType(SequenceReader<byte> copy, out ContainerType? containerType)
        {
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
                }

                containerType = ContainerType.Array;
                return true;
            }
        }
    }
}
