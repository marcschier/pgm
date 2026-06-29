// Copyright (c) marcschier. Licensed under the MIT License.

using BenchmarkDotNet.Running;

namespace Pgm.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
