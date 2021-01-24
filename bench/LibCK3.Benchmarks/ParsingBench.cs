using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace LibCK3.Benchmarks
{
    [SimpleJob(RunStrategy.Throughput)]
    public class ParsingBench
    {
        private readonly LibCK3.Tests.ParsingTests parsingTests = new();

        [Benchmark]
        public async Task Parse()
        {
            await parsingTests.ParseFullSave();
        }

        [Benchmark]
        public async Task ParseMeta()
        {
            await parsingTests.ParseMeta();
        }
    }
}