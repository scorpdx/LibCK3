using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LibCK3.Parsing
{
    [StructLayout(LayoutKind.Sequential)]
    [DebuggerDisplay("(ID:{ID}, Control: {IsControl}, Identifier: {AsIdentifier()})")]
    public readonly struct CK3Token
    {
        private readonly ushort ID;

        public CK3Token(ushort ID)
        {
            this.ID = ID;
        }

        public bool IsSpecial => IsControl || IsType;

        public bool IsControl => AsSpecial() switch
        {
            SpecialTokens.Equals => true,
            SpecialTokens.Open => true,
            SpecialTokens.Close => true,
            _ => false
        };

        public bool IsType => AsSpecial() switch
        {
            SpecialTokens.Int => true,
            SpecialTokens.Float => true,
            SpecialTokens.Bool => true,
            SpecialTokens.LPQStr => true,
            SpecialTokens.UInt => true,
            _ => false
        };

        public SpecialTokens AsSpecial() => (SpecialTokens)ID;

        public JsonEncodedText AsIdentifier() => CK3Tokens.Tokens[ID];
    }
}
