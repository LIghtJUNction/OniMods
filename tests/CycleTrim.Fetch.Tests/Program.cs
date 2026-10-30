using System;
using HarmonyLib;

namespace CycleTrim.Fetch.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var harmony = new Harmony("cycletrim.tests.fetch-buffer");
            try
            {
                if (args.Length > 1 || (args.Length == 1 && args[0] != "--benchmark"))
                    throw new ArgumentException("expected no arguments or --benchmark");
                harmony.PatchAll(typeof(Program).Assembly);
                if (args.Length == 1) FetchBenchmark.Run();
                else FetchRegression.Run();
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }
    }
}
