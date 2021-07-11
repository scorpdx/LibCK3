using LibCK3.Tokens;
using System.Diagnostics;
using System.Text.Json;

namespace LibCK3.Parsing
{
    [DebuggerDisplay("(ID:{ID}, Special: {IsSpecial}, Identifier: {AsIdentifier()})")]
    public readonly struct CK3Token
    {
        private readonly ushort ID;

        public CK3Token(ushort ID) => this.ID = ID;

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
            SpecialTokens.Bool => true,
            SpecialTokens.Int => true,
            SpecialTokens.UInt => true,
            SpecialTokens.ULong => true,
            SpecialTokens.Float => true,
            SpecialTokens.Double => true,
            SpecialTokens.LPQStr => true,
            SpecialTokens.LPStr => true,
            SpecialTokens.RGB => true,
            _ => false
        };

        public SpecialTokens AsSpecial() => (SpecialTokens)ID;

        public JsonEncodedText AsIdentifier() => CK3Tokens.Tokens[ID];

        public ValueOverlayFlags GetOverlay()
            => ValueOverlays.Overlays.TryGetValue(ID, out var overlayFlags) ? overlayFlags : ValueOverlayFlags.None;
    }
}
