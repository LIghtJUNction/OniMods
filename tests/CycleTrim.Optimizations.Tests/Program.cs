using System;
using System.Diagnostics;
using System.IO;

namespace CycleTrim.Optimizations.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && (args[0] == "--regression-presentation-native"
                    || args[0] == "--benchmark-presentation-native"))
                {
                    PresentationUploadNativeRegression.Run(args[1], args[0] == "--benchmark-presentation-native");
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-save")
                {
                    SaveMetadataBenchmark.Run();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-save-scoped")
                {
                    SaveMetadataBenchmark.RunScoped();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-state-machine")
                {
                    StateMachineLookupBenchmark.Run();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-power")
                {
                    PowerOverlayRegression.Benchmark();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-conveyor")
                {
                    ConveyorPathBenchmark.Run();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-animation")
                {
                    AnimationSymbolBenchmark.Run();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--test-conduit-visuals")
                {
                    RunFamily("conduit");
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-conduit")
                {
                    ConduitVisualBenchmark.Run();
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--benchmark-pickup-highlight")
                {
                    PickupHighlightBenchmark.Run();
                    return 0;
                }
                const string regressionPrefix = "--regression-";
                if (args.Length == 1 && args[0].StartsWith(regressionPrefix, StringComparison.Ordinal))
                {
                    RunFamily(args[0].Substring(regressionPrefix.Length));
                    return 0;
                }
                if (args.Length != 0)
                    throw new ArgumentException("expected no arguments or --benchmark-"
                        + "save, save-scoped, power, conveyor, animation, conduit, pickup-highlight or state-machine; "
                        + "--regression- selects one family");
                // Several negative cases irreversibly load another provider or
                // a growable assembly. Each complete family gets its own process
                // so those real invalidations cannot poison a later family's
                // clean-environment cases. Benchmark entry points stay above.
                // Conveyor's fresh native detours and warmed index invalidation
                // have separate assertions; neither assumes retroactive uninlining.
                foreach (var family in new[] { "conveyor", "conveyor-compat", "power", "power-provider", "save", "animation", "state-machine", "conduit", "zoom-animation", "frame-rate", "presentation-upload", "pickup-highlight" })
                    RunIsolated(family);
                Console.WriteLine("PASS CycleTrim optimization host regressions (synthetic game fixtures)");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void RunFamily(string family)
        {
            switch (family)
            {
                case "conveyor": ConveyorPathRegression.Run(); break;
                case "conveyor-compat": ConveyorPathRegression.RunCompatibility(); break;
                case "power":
                    PowerOverlayCacheRegression.Run();
                    PowerOverlayRegression.Run();
                    break;
                case "power-provider": PowerOverlayRegression.RunProviderRegression(); break;
                case "save": SaveMetadataRegression.Run(); break;
                case "animation": AnimationSymbolRegression.Run(); break;
                case "zoom-animation": ZoomAnimationRegression.Run(); break;
                case "frame-rate": FrameRateDiagnosticsRegression.Run(); break;
                case "presentation-upload": PresentationUploadRegression.Run(); break;
                case "pickup-highlight": PickupHighlightRegression.Run(); break;
                case "conduit":
                    ConduitVisualRefreshGateRegression.Run();
                    ConduitVisualRegression.Run();
                    break;
                case "state-machine": StateMachineLookupRegression.Run(); break;
                default: throw new ArgumentException("unknown regression family: " + family);
            }
        }

        private static void RunIsolated(string family)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("cannot locate the current regression host");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet",
                StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--regression-" + family);
            using (var child = Process.Start(start))
            {
                if (child == null)
                    throw new InvalidOperationException("cannot start regression family: " + family);
                child.WaitForExit();
                if (child.ExitCode != 0)
                    throw new InvalidOperationException("regression family failed: " + family
                        + "; exit=" + child.ExitCode);
            }
        }
    }
}
