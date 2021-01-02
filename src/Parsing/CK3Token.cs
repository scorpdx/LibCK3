using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LibCK3.Parsing
{
    [StructLayout(LayoutKind.Sequential)]
    [DebuggerDisplay("(ID:{ID}, Control: {IsControl}, Identifier: {AsIdentifier()})")]
    public readonly ref struct CK3Token
    {
        private readonly ushort ID;

        public CK3Token(ushort ID)
        {
            this.ID = ID;
        }

        public bool IsControl => AsControl() switch
        {
            //
            ControlTokens.Equals => true,
            ControlTokens.Open => true,
            ControlTokens.Close => true,
            //type
            ControlTokens.Int => true,
            ControlTokens.Float => true,
            ControlTokens.LPQStr => true,
            ControlTokens.UInt => true,
            _ => false
        };

        public ControlTokens AsControl() => (ControlTokens)ID;

        public string AsIdentifier() => CK3Tokens.Tokens[ID];
    }
}
