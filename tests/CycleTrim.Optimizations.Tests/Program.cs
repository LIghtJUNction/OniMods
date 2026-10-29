using System;

namespace CycleTrim.Optimizations.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--benchmark-save")
                {
                    SaveMetadataBenchmark.Run();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-power")
                {
                    PowerOverlayRegression.Benchmark();
                    return 0;
                }
                if (args.Length != 0)
                    throw new ArgumentException("expected no arguments, --benchmark-save or --benchmark-power");
                // The compatibility test deliberately loads an unknown dynamic
                // assembly. Benchmarks therefore run in separate clean processes.
                PowerOverlayRegression.Run();
                SaveMetadataRegression.Run();
                Console.WriteLine("PASS CycleTrim optimization host regressions (synthetic game fixtures)");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
