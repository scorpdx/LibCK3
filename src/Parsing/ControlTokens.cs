using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibCK3.Parsing
{
    public enum ControlTokens
    {
        None = 0,
        Equals = 0x1,
        Open = 0x3,
        Close = 0x4,
        //0xB = id
        Int = 0xC,
        Float = 0xD,
        Bool = 0xE,
        /// <summary>
        /// Length-prefixed quoted string
        /// </summary>
        LPQStr = 0xF,
        //0x10
        //0x11
        //0x12
        //0x13
        //0x14
        //0x15 = idtype
        UInt = 0x14,
        ULong = 0x29C
    }
}
