using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LibCK3.Parsing
{
    public class CompressedGamestateReader
    {
        internal const string GAMESTATE_ENTRY = "gamestate";
        private const uint PKZIP_MAGIC_UINT = 0x04034b50;

        internal static readonly byte[] GAMESTATE = Encoding.UTF8.GetBytes(GAMESTATE_ENTRY);

        private readonly PipeReader _zipReader;
        private readonly PipeWriter _binWriter;

        public CompressedGamestateReader(PipeReader zipReader, PipeWriter binWriter)
        {
            _zipReader = zipReader;
            _binWriter = binWriter;
        }

        public async Task<(uint compressedSize, uint uncompressedSize, uint crc32)> ParseAsync(CancellationToken token = default)
        {
            (uint CompressedSize, uint UncompressedSize, uint Crc32) fileInfo = default;
            uint CompressedSizeRemaining = 0;
            bool copying = false;

            var zipEntryPipe = new Pipe();
            var entryReader = zipEntryPipe.Reader;
            var entryWriter = zipEntryPipe.Writer;

            await using var entryReaderStream = entryReader.AsStream();
            await using var deflateStream = new DeflateStream(entryReaderStream, CompressionMode.Decompress);
            var pipelineTask = deflateStream.CopyToAsync(_binWriter, token);

            while (true)
            {
                var result = await _zipReader.ReadAsync(token);
                if (result.IsCanceled || (result.IsCompleted && result.Buffer.IsEmpty))
                    break;

                if (copying)
                {
                    Debug.Assert(CompressedSizeRemaining > 0);

                    ReadOnlySequence<byte> buffer = result.Buffer.Length < CompressedSizeRemaining
                        ? result.Buffer
                        : result.Buffer.Slice(result.Buffer.Start, CompressedSizeRemaining);

                    CopyBuffer(buffer, entryWriter, out var consumed);
                    _zipReader.AdvanceTo(consumed);
                    await entryWriter.FlushAsync(token);

                    CompressedSizeRemaining -= (uint)buffer.Length;
                    if (CompressedSizeRemaining == 0)
                    {
                        await entryWriter.CompleteAsync();
                        break;
                    }
                }
                else
                {
                    if (TryParseHeader(result.Buffer, out fileInfo, out var consumed))
                    {
                        CompressedSizeRemaining = fileInfo.CompressedSize;

                        copying = true;
                        _zipReader.AdvanceTo(consumed);
                    }
                }
            }

            await _zipReader.CompleteAsync();
            await pipelineTask;

            await _binWriter.FlushAsync(token);
            await _binWriter.CompleteAsync();

            return fileInfo;
        }

        private static void CopyBuffer(ReadOnlySequence<byte> buffer, PipeWriter writer, out SequencePosition consumed)
        {
            var firstSpan = buffer.FirstSpan;
            var writeSpan = writer.GetSpan(firstSpan.Length);
            firstSpan.CopyTo(writeSpan);
            writer.Advance(firstSpan.Length);
            consumed = buffer.GetPosition(firstSpan.Length, buffer.Start);
        }

        [StructLayout(LayoutKind.Explicit, Size = 30)]
        private readonly struct LocalZipHeader
        {
            [FieldOffset(0)] public readonly uint Signature;
            [FieldOffset(4)] public readonly ushort/*ZipVersionNeededValues*/ VersionToExtract;
            [FieldOffset(6)] public readonly ushort/*BitFlagValues*/ Flags;
            [FieldOffset(8)] public readonly ushort/*CompressionMethodValues*/ CompressionMethod;
            [FieldOffset(10)] public readonly uint DosTime;
            [FieldOffset(14)] public readonly uint Crc32;
            [FieldOffset(18)] public readonly uint CompressedSize;
            [FieldOffset(22)] public readonly uint UncompressedSize;
            [FieldOffset(26)] public readonly ushort FilenameLength;
            [FieldOffset(28)] public readonly ushort ExtraLength;
        }

        private static bool TryParseHeader(ReadOnlySequence<byte> buffer, out (uint CompressedSize, uint UncompressedSize, uint Crc32) fileInfo, out SequencePosition consumed)
        {
            var reader = new SequenceReader<byte>(buffer);

            //sig
            if (!reader.TryRead<LocalZipHeader>(out var header))
            {
                fileInfo = default;
                consumed = buffer.Start;
                return false;
            }

            if (header.Signature != PKZIP_MAGIC_UINT)
                throw new InvalidOperationException("Invalid signature");

            //filename
            if (header.FilenameLength > 256)
                throw new InvalidOperationException("Filename length too large in local file header");

            Span<byte> filenameBuf = stackalloc byte[header.FilenameLength];
            if (!reader.TryCopyTo(filenameBuf))
            {
                fileInfo = default;
                consumed = buffer.Start;
                return false;
            }

            if (!filenameBuf.SequenceEqual(GAMESTATE))
                throw new InvalidOperationException("Unexpected filename");

            reader.Advance(header.FilenameLength);

            //extra
            if (reader.Remaining < header.ExtraLength)
            {
                fileInfo = default;
                consumed = buffer.Start;
                return false;
            }

            reader.Advance(header.ExtraLength);

            fileInfo = (header.CompressedSize, header.UncompressedSize, header.Crc32);
            consumed = reader.Position;
            return true;
        }
    }
}
