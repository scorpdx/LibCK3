using System;
using System.IO;
using System.IO.Pipelines;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static LibCK3.Parsing.CK3BinParser;

namespace LibCK3.Parsing
{
    public sealed class CK3Bin
    {
        private readonly PipeReader _readPipe;
        private readonly Utf8JsonWriter _writer;

        private readonly CK3BinParser _parser;
        private readonly bool _parseGamestate;

        private CK3Bin(PipeReader readPipe, Utf8JsonWriter writer, ParseState state)
        {
            _readPipe = readPipe;
            _writer = writer;
            _parser = new(state, _writer);
        }
        public CK3Bin(Stream stream, Utf8JsonWriter writer, bool parseGamestate = true) : this(PipeReader.Create(stream), writer, state: ParseState.Checksum)
        {
            _parseGamestate = parseGamestate;
        }
        public CK3Bin(string path, Utf8JsonWriter writer, bool parseGamestate = true)
            : this(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), writer, parseGamestate)
        {
        }

        private static Utf8JsonWriter GetTestWriter(out Func<byte[]> flushFunc)
        {
            var ms = new MemoryStream();
            var writer = new Utf8JsonWriter(ms);
            flushFunc = () =>
            {
                using (ms)
                using (writer)
                {
                    writer.Flush();
                    return ms.ToArray();
                }
            };

            return writer;
        }
        public static async Task<byte[]> ParseFragmentAsync(byte[] fragment)
        {
            using var msFrag = new MemoryStream(fragment);
            var bin = new CK3Bin(PipeReader.Create(msFrag), GetTestWriter(out var flush), ParseState.Token);

            await bin.ParseAsync();
            return flush();
        }

        public ValueTask ParseAsync(CancellationToken token = default)
            => ReadPipeAsync(_readPipe, token);

        private async ValueTask ReadPipeAsync(PipeReader pipeReader, CancellationToken cancelToken = default)
        {
            _writer.WriteStartObject();

            try
            {
                while (!cancelToken.IsCancellationRequested)
                {
                    var result = await pipeReader.ReadAsync(cancelToken);
                    if (result.IsCanceled || (result.IsCompleted && result.Buffer.IsEmpty))
                    {
                        break;
                    }

                    _parser.ParseSequence(result.Buffer, out var consumed, out var examined);
                    pipeReader.AdvanceTo(consumed, examined);

                    if (_parser.State == ParseState.DecompressGamestate)
                    {
                        if (!_parseGamestate)
                        {
                            break;
                        }

                        _writer.WritePropertyName(CompressedGamestateReader.GAMESTATE);

                        //await using var pipeStream = pipeReader.AsStream(true);
                        //using var zip = new System.IO.Compression.ZipArchive(pipeStream, System.IO.Compression.ZipArchiveMode.Read, true, System.Text.Encoding.UTF8);

                        //await using var gamestateStream = zip.GetEntry("gamestate").Open();

                        //var gamestateBin = new CK3Bin(PipeReader.Create(gamestateStream), _writer, ParseState.Token);
                        //await gamestateBin.ParseAsync(cancelToken);

                        {
                            var gamestatePipe = new Pipe();
                            var cgreader = new CompressedGamestateReader(pipeReader, gamestatePipe.Writer);
                            cgreader.ParseAsync(cancelToken);

                            var gamestateBin = new CK3Bin(gamestatePipe.Reader, _writer, ParseState.Token);
                            var parseGamestateTask = gamestateBin.ParseAsync(cancelToken);

                            await parseGamestateTask;
                        }
                        break;
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
    }
}
