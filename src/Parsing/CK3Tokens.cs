﻿using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LibCK3.Parsing
{
    public static class CK3Tokens
    {
        private const string TOKEN_FILE = "assets/ck3.tok";

        private static readonly Lazy<ImmutableDictionary<ushort, JsonEncodedText>> _tokens = new(() =>
          File.ReadLines(TOKEN_FILE, Encoding.UTF8)
              //Ignore comments
              .Where(l => !l.StartsWith('#'))
              //
              .Select(l => l.Split(' '))
              //Remove the 0x prefix before parsing, if it's present
              .Select(a => (a[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? a[0][2..] : a[0], a[1]))
              //Convert to ushort
              .Select(t => (ushort.Parse(t.Item1, System.Globalization.NumberStyles.AllowHexSpecifier), t.Item2))
              //
              .ToImmutableDictionary(t => t.Item1, t => JsonEncodedText.Encode(t.Item2))
        );

        public static IImmutableDictionary<ushort, JsonEncodedText> Tokens => _tokens.Value;
    }
}
