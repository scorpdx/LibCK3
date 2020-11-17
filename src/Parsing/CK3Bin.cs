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

        public bool IsControl => ((ControlTokens)ID) switch {
            //
            ControlTokens.Equals => true,
            ControlTokens.Open => true,
            ControlTokens.Close => true,
            //type
            ControlTokens.Int => true,
            ControlTokens.LPQStr => true,
            _ => false
        };

        public ControlTokens AsControl() => (ControlTokens)ID;

        public string AsIdentifier() => CK3Tokens.Tokens[ID];
    }

    public class CK3Bin
    {
        private readonly Pipe _pipe;
        private readonly Stream _stream;

        private readonly Utf8JsonWriter _writer;

        public CK3Bin(Stream stream, Utf8JsonWriter writer)
        {
            _pipe = new Pipe();
            _stream = stream;
            _writer = writer;
        }
        public CK3Bin(string path, Utf8JsonWriter writer) : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), writer)
        {
        }

        public async Task ParseAsync(CancellationToken token = default)
        {
            var streamReadTask = _stream.CopyToAsync(_pipe.Writer, token);
            var reader = _pipe.Reader;
            await ReadPipeAsync(reader, token);
            await streamReadTask;
        }

        private async Task ReadPipeAsync(PipeReader pipeReader, CancellationToken cancelToken = default)
        {
            _writer.WriteStartObject();

            bool hasReadChecksum = false;

            while (!cancelToken.IsCancellationRequested)
            {
                var result = await pipeReader.ReadAsync(cancelToken);
                ParseSequence(result.Buffer, ref hasReadChecksum);
                ///
                // 0 = checksum
                // 1 = token
                // 2 = token close
                //var state = 0;

                //ReadOnlySequence<byte> line;
                //switch (state)
                //{
                //    case 0:
                //        {
                //            var readChecksum = TryReadChecksum(ref buffer, out line);
                //            if (readChecksum)
                //            {
                //                state = 1;
                //            }
                //            break;
                //        }
                //    case 1:
                //        {
                //            if (TryReadToken(ref buffer, out var tokenSet))
                //            {
                //                state = 2;
                //            }
                //            else
                //            {

                //            }

                //            break;
                //        }
                //    case 2:
                //        {
                //            if (TryReadValue(ref buffer, out var control))
                //            {
                //                state = 1;
                //            }
                //            break;
                //        }
                //}
            }
            cancelToken.ThrowIfCancellationRequested();

            _writer.WriteEndObject();
        }

        private void ParseSequence(ReadOnlySequence<byte> buffer, ref bool hasReadChecksum)
        {
            bool TryReadChecksum(ref SequenceReader<byte> reader, out ReadOnlySpan<byte> line)
                => reader.TryReadTo(out line, (byte)'\n');

            bool TryReadPair(ref SequenceReader<byte> reader)
            {
                if (!TryReadToken(ref reader, out var token))
                    return false;

                if(!token.IsControl)
                {
                    //is identifier, so read the '='
                    if(!TryReadToken(ref reader, out var controlToken))
                    {
                        reader.Rewind(sizeof(short));
                        return false;
                    }

                    switch(controlToken.AsControl())
                    {
                        case ControlTokens.Equals:
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                    Debug.WriteLine($"tag={token.AsIdentifier()}");
                }
                else
                {
                    reader.Rewind(sizeof(short));
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
                if (!reader.TryReadLittleEndian(out short id))
                {
                    token = default;
                    return false;
                }

                token = new CK3Token((ushort)id);
                return true;
            }

            bool TryReadValue(ref SequenceReader<byte> reader, CK3Token prevToken)
            {
                if (!reader.TryReadLittleEndian(out short id))
                {
                    return false;
                }

                var token = new CK3Token((ushort)id);
                Debug.Assert(token.IsControl);

                switch (token.AsControl())
                {
                    case ControlTokens.Open:
                        Debug.WriteLine("{");
                        Debug.Indent();

                        Debug.Assert(!prevToken.IsControl);
                        _writer.WriteStartObject(prevToken.AsIdentifier());

                        while (TryReadPair(ref reader))//, out var x, out var y))
                        {
                            //Debug.WriteLine($"-->{x}");
                            //Debug.WriteLine($"-->{y}");
                        }

                        if(!TryReadValue(ref reader, prevToken))
                        {
                            return false;
                        }

                        return true;
                    case ControlTokens.Close:
                        Debug.WriteLine("}");
                        Debug.Unindent();

                        _writer.WriteEndObject();
                        return true;
                    case ControlTokens.Int:
                        if (!reader.TryReadLittleEndian(out int intValue))
                        {
                            //element = default;
                            return false;
                        }

                        Debug.WriteLine($"int={intValue}");
                        Debug.Assert(!prevToken.IsControl);
                        _writer.WriteNumber(prevToken.AsIdentifier(), intValue);

                        return true;
                    case ControlTokens.LPQStr:
                        if (!reader.TryReadLittleEndian(out short strLen))
                        {
                            return false;
                        }

                        var strSlice = reader.UnreadSpan.Slice(0, strLen);
                        if (strSlice.Length != strLen)
                        {
                            reader.Rewind(sizeof(short));
                            return false;
                        }

                        Debug.WriteLine($"str={Encoding.UTF8.GetString(strSlice)}");
                        Debug.Assert(!prevToken.IsControl);
                        _writer.WriteString(prevToken.AsIdentifier(), strSlice);
                        reader.Advance(strLen);
                        return true;

                        //var pos = reader.Position;
                        //if (!reader.IsNext((byte)'"', true))
                        //{
                        //    throw new InvalidOperationException();
                        //}

                        //if (reader.TryReadTo(out ReadOnlySpan<byte> str, (byte)'"'))
                        //{
                        //    Debug.WriteLine($"str:{Encoding.UTF8.GetString(str)}");
                        //}
                        //else
                        //{
                        //    throw new InvalidOperationException();
                        //}

                        //value = default;
                        //return true;
                    default:
                        throw new InvalidOperationException();
                }
            }

            var reader = new SequenceReader<byte>(buffer);

            if (!hasReadChecksum)
            {
                if (!TryReadChecksum(ref reader, out var checksum))
                    return;

                _writer.WriteString("checksum", checksum);

                hasReadChecksum = true;
            }

            while (TryReadPair(ref reader))//, out var token, out var value))
            {
                Debug.WriteLine(reader.Position);
                //Debug.WriteLine(token);
                //Debug.WriteLine(value);
            }

            int PKZIP_MAGIC = BitConverter.ToInt32(new[] { (byte)0x50, (byte)0x4b, (byte)0x03, (byte)0x04 });
            if(reader.TryReadLittleEndian(out int next) && next == PKZIP_MAGIC)
            {

            }
        }
    }
}
