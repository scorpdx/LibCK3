using LibCK3.Generator;
using LibCK3.Parsing;
using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LibCK3.Tests
{
    public class ParsingTests
    {
        private const string SAVE_PATH = "assets/save.ck3";
        private const string META_PATH = "assets/meta_header.ck3";
        private const string META_JSON_PATH = "assets/meta_header.json";
        private const int CHECKSUM_LENGTH = 24;

        private static Utf8JsonWriter GetTestWriter(out Func<byte[]> flushFunc)
        {
            var ms = new MemoryStream();
            var writer = new Utf8JsonWriter(ms/*, new JsonWriterOptions() { Indented = true }*/);
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
        private static Task<byte[]> ParseFragment(BinFragment fragment) => CK3Bin.ParseFragmentAsync(fragment.Build().ToArray());
        private static async Task<string> ParseFragmentString(BinFragment fragment) => Encoding.UTF8.GetString(await ParseFragment(fragment));

        [Fact]
        public async Task ParseFullSave()
        {
            var bin = new CK3Bin(SAVE_PATH, GetTestWriter(out var flush));
            await bin.ParseAsync();

            var results = flush();
            Assert.NotEmpty(results);
        }

        [Fact]
        public async Task ParseMeta()
        {
            var bin = new CK3Bin(META_PATH, GetTestWriter(out var flush));
            await bin.ParseAsync();

            var results = flush();
            var expected = await File.ReadAllBytesAsync(META_JSON_PATH);

            Assert.Equal(expected, results);
        }

        [Fact]
        public async Task ParseMetaNoChecksum()
        {
            var metaBytes = await File.ReadAllBytesAsync(META_PATH);
            //skip past checksum
            using var metaStream = new MemoryStream(metaBytes, CHECKSUM_LENGTH, metaBytes.Length - CHECKSUM_LENGTH, false);

            var bin = new CK3Bin(metaStream, GetTestWriter(out var flush));
            await bin.ParseAsync();

            var results = flush();
            var expected = await File.ReadAllBytesAsync(META_JSON_PATH);

            //strip opening "{" from each
            results = results[1..];
            expected = expected[1..];

            //strip checksum from expected json
            var checksumJsonLength = Encoding.UTF8.GetByteCount(@"""checksum"":""SAV0103caf29816000075c3"",");
            expected = expected[checksumJsonLength..];

            Assert.Equal(expected, results);
        }

        [Fact]
        public async Task ParseThrowsOnUnnamedEmptyObject()
        {
            var frag = new BinFragment().Open().Close();

            byte[] results;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => results = await ParseFragment(frag));

            //Assert.Equal("", Encoding.UTF8.GetString(results));
        }

        [Fact]
        public async Task ParseEmptyObject()
        {
            var frag = new BinFragment().Identifier("meta_data").Eq().Open().Close();
            var results = await ParseFragment(frag);

            Assert.Equal("{\"meta_data\":{}}", Encoding.UTF8.GetString(results));
        }

        [Fact]
        public async Task ParseIdentifierIntPair()
        {
            var frag = new BinFragment().Identifier("save_game_version").Eq().Int(3);
            var results = await ParseFragment(frag);

            var str = Encoding.UTF8.GetString(results);

            Assert.Equal("{\"save_game_version\":3}", str);
        }

        [Fact]
        public async Task ParseDuplicateKeys_Empty()
        {
            var frag = new BinFragment()
                .Open()
                  .Identifier("triggered_event").Eq().Open().Close()
                  .Identifier("triggered_event").Eq().Open().Close()
                .Close();

            var str = await ParseFragmentString(frag);
            Assert.Equal("{\"triggered_event\":[{},{}]}", str);
        }

        [Fact]
        public async Task ParseHiddenObject()
        {
            var frag = new BinFragment()
                .Identifier("levels")
                .Eq()
                .Open()
                    .Int(1)
                    .Int(2)
                    .Int(3).Eq().Int(4)
                    .Int(5)
                .Close();
            var results = await ParseFragment(frag);

            var str = Encoding.UTF8.GetString(results);

            Assert.Equal("{\"levels\":[1,2,{\"3\":4},5]}", str);
        }

        private Stream OpenSaveAndAdvanceToZip()
        {
            var fs = File.OpenRead(SAVE_PATH);
            using var br = new BinaryReader(fs, Encoding.UTF8, true);
            while (br.ReadUInt32() != CompressedGamestateReader.PKZIP_MAGIC_UINT) ;
            fs.Seek(-sizeof(uint), SeekOrigin.Current);
            return fs;
        }

        [Fact]
        public async Task UnzipSave()
        {
            using var gamestateZipStream = OpenSaveAndAdvanceToZip();
            using var binStream = new MemoryStream();
            var reader = PipeReader.Create(gamestateZipStream);
            var writer = PipeWriter.Create(binStream, new StreamPipeWriterOptions(leaveOpen: true));

            var blindzip = new CompressedGamestateReader(reader, writer);
            var (compressedSize, uncompressedSize, crc32) = await blindzip.ParseAsync();

            Assert.Equal(uncompressedSize, binStream.Length);
            Assert.Equal(crc32, Crc32.ComputeChecksum(binStream.ToArray()));
        }
    }
}
