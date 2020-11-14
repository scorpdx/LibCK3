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

namespace LibCK3.Parsing
{
    public class CK3Bin
    {
        private readonly Pipe _pipe;
        private readonly Stream _stream;

        public CK3Bin(Stream stream)
        {
            _pipe = new Pipe();
            _stream = stream;
        }
        public CK3Bin(string path) : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
        }
        
        public async Task ParseAsync(CancellationToken token = default)
        {
            var streamReadTask = _stream.CopyToAsync(_pipe.Writer, token);

            var reader = _pipe.Reader;
            await ReadPipeAsync(reader, token);

            await streamReadTask;
        }

        private async Task ReadPipeAsync(PipeReader reader, CancellationToken cancelToken = default)
        {
            bool TryReadChecksum(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
            {
                // Look for a EOL in the buffer.
                SequencePosition? position = buffer.PositionOf((byte)'\n');

                if (position == null)
                {
                    line = default;
                    return false;
                }

                // Skip the line + the \n.
                line = buffer.Slice(0, position.Value);
                buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                return true;
            }

            bool TryReadToken(ref ReadOnlySequence<byte> buffer, out (string token, ControlTokens control) tokenSet)
            {
                tokenSet = default;

                var reader = new SequenceReader<byte>(buffer);
                if (!reader.TryReadLittleEndian(out short id))
                    return false;

                var token = CK3Tokens.Tokens[(ushort)id];
                Debug.WriteLine($"{id}: {token}");

                if (!reader.TryReadLittleEndian(out short controlId))
                    return false;

                var control = (ControlTokens)controlId;
                Debug.WriteLine(control);

                tokenSet = (token, control);
                return true;
            }

            //bool TryReadValue(ref ReadOnlySequence<byte> buffer, out ControlTokens control)
            //{

            //}

            ///
            // 0 = checksum
            // 1 = token
            // 2 = token close
            var state = 0;

            while (!cancelToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancelToken);
                var buffer = result.Buffer;

                ReadOnlySequence<byte> line;
                switch(state)
                {
                    case 0:
                        {
                            var readChecksum = TryReadChecksum(ref buffer, out line);
                            if(readChecksum)
                            {
                                state = 1;
                            }
                            break;
                        }
                    case 1:
                        {
                            if (TryReadToken(ref buffer, out var tokenSet))
                            {
                                var (readToken, control) = tokenSet;
                            }
                            else
                            {

                            }

                            break;
                        }
                    case 2:
                        {

                            break;
                        }
                }
            }
            cancelToken.ThrowIfCancellationRequested();
        }
    }
}
