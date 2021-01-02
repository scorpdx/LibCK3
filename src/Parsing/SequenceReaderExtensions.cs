using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace LibCK3.Parsing
{
    public static class SequenceReaderExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryRead<T>(ref this SequenceReader<byte> reader, out T value) where T : unmanaged
            => SequenceMarshal.TryRead(ref reader, out value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadLittleEndian(ref this SequenceReader<byte> reader, out ushort value)
        {
            bool result = reader.TryReadLittleEndian(out short shortValue);
            value = (ushort)shortValue;
            return result;
        }

        public static bool TryReadToken(ref this SequenceReader<byte> reader, out CK3Token token)
        {
            if (!reader.TryReadLittleEndian(out ushort id))
            {
                token = default;
                return false;
            }

            token = new CK3Token(id);
            return true;
        }
    }
}
