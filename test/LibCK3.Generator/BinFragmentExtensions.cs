using LibCK3.Parsing;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibCK3.Generator
{
    public static class BinFragmentExtensions
    {
        private static BinFragment Append(this BinFragment fragment, SpecialTokens token)
            => fragment.Append(BitConverter.GetBytes((ushort)token));

        public static BinFragment Open(this BinFragment fragment)
            => fragment.Append(SpecialTokens.Open);

        public static BinFragment Close(this BinFragment fragment)
            => fragment.Append(SpecialTokens.Close);
    }
}
