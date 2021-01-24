using BenchmarkDotNet.Running;
using LibCK3.Benchmarks;
using System;

var summary = BenchmarkRunner.Run<ParsingBench>();