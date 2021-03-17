using System;
using System.Buffers;

namespace LibCK3.Generator
{
    public class BinFragment : ReadOnlySequenceSegment<byte>
    {
        private readonly ReadOnlySequenceSegment<byte> _start;

        private BinFragment(ReadOnlySequenceSegment<byte> start, ReadOnlyMemory<byte> memory) : this(memory)
        {
            _start = start;
        }
        public BinFragment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }
        public BinFragment()
        {
        }

        public BinFragment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new BinFragment(_start ?? this, memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };

            Next = segment;
            return segment;
        }

        public ReadOnlySequence<byte> Build() => new(_start, 0, this, Memory.Length);
    }
}
