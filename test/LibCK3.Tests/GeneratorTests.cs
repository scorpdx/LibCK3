﻿using LibCK3.Generator;
using LibCK3.Parsing;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace LibCK3.Tests
{
    public class GeneratorTests
    {
        private static readonly byte[] Open = BitConverter.GetBytes((ushort)SpecialTokens.Open);
        private static readonly byte[] Close = BitConverter.GetBytes((ushort)SpecialTokens.Close);

        [Fact]
        public void GenerateEmptyObject()
        {
            var frag = new BinFragment();
            var seq = frag.Open().Close().Build();

            Assert.Equal(
                seq.ToArray(),
                /* {} */
                new byte[] { 0x03, 0x00, 0x04, 0x00 });
        }
    }
}