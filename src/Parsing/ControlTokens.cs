using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibCK3.Parsing
{
    internal enum ControlTokens
    {
        Equals = 0x1,
        Open = 0x3,
        Close = 0x4,
        Int = 0xC,
        Float = 0xD,
        Bool = 0xE,
        String = 0xF,
        UInt = 0x14,
        ULong = 0x29C
    }
}
