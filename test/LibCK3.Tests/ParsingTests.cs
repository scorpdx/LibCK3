using LibCK3.Parsing;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LibCK3.Tests
{
    public class ParsingTests
    {
        private const string META_PATH = "assets/meta_header.ck3";
        private const string META_JSON_PATH = "assets/meta_header.json";
        private const int CHECKSUM_LENGTH = 24;

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
    }
}
