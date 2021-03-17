using LibCK3.Generator;
using System.Buffers;
using System.Linq;
using Xunit;

namespace LibCK3.Tests
{
    public class GeneratorTests
    {
        [Fact]
        public void GenerateEmptyContainer()
        {
            var frag = new BinFragment();
            var seq = frag.Open().Close().Build();

            Assert.Equal(
                seq.ToArray(),
                new byte[] {
                    /*{*/0x03, 0x00,
                    /*}*/0x04, 0x00
                });
        }

        [Fact]
        public void GenerateIntArray()
        {
            var frag = new BinFragment();
            var seq = frag.Open().Int(1).Int(2).Int(3).Close().Build();

            Assert.Equal(
                seq.ToArray(),
                new byte[] {
                    /*{*/0x03, 0x00,
                    /*int*/0xC, 0x00, /*1*/ 0x01, 0x00, 0x00, 0x00,
                    /*int*/0xC, 0x00, /*2*/ 0x02, 0x00, 0x00, 0x00,
                    /*int*/0xC, 0x00, /*3*/ 0x03, 0x00, 0x00, 0x00,
                    /*}*/0x04, 0x00
                });
        }
    }
}
