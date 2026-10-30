using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// This driver selects existing benchmark methods; the timed loops remain in
// the original host assemblies. Each invocation loads exactly one revision.
internal static class Program
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static Assembly target = null!;
    private static int pair;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 5 || !int.TryParse(args[4], out pair) || pair < 0 || pair >= 7)
                throw new ArgumentException("expected assembly family scenario mode pair(0..6)");
            string path = Path.GetFullPath(args[0]);
            string directory = Path.GetDirectoryName(path)!;
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string dependency = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(dependency) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency) : null;
            };
            target = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            Console.WriteLine("R3_CONFIG " + JsonSerializer.Serialize(new
            {
                family = args[1], scenario = args[2], mode = args[3], pair,
                assembly_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                order = pair % 2 == 0 ? "original,patched" : "patched,original"
            }));
            if (args[1] == "regression")
            {
                string suite = args[2] switch
                {
                    "animation" => "AnimationSymbolRegression",
                    "conveyor" => "ConveyorPathRegression",
                    "power" => "PowerOverlayRegression",
                    _ => throw new ArgumentException("unknown regression family")
                };
                Call(Test(suite), "Run");
            }
            else if (args[1] == "animation") Animation(args[2], args[3]);
            else if (args[1] == "conveyor") Conveyor(args[2]);
            else if (args[1] == "power") Power(args[2]);
            else throw new ArgumentException("unknown family");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error is TargetInvocationException invocation ? invocation.InnerException : error);
            return 1;
        }
    }

    private static Type Type(string name) => target.GetType(name, throwOnError: true)!;
    private static Type Test(string name) => Type("CycleTrim.Optimizations.Tests." + name);
    private static object? Call(Type type, string method, params object?[] args) =>
        type.GetMethod(method, Static)!.Invoke(null, args);
    private static object Field(object value, string name) => value.GetType().GetField(name, Instance)!.GetValue(value)!;

    private static void Configure(string option)
    {
        var method = Type("CycleTrim.Core.CycleTrimPatchOptions").GetMethod("Configure", Static)!;
        object?[] values = method.GetParameters().Select(parameter => parameter.HasDefaultValue
            ? parameter.DefaultValue : (object)(parameter.Name != "performanceDiagnostics")).ToArray();
        int index = Array.FindIndex(method.GetParameters(), parameter => parameter.Name == option);
        if (index < 0) throw new InvalidOperationException("option missing: " + option);
        values[index] = true;
        method.Invoke(null, values);
    }

    private static object Harmony(string owner)
    {
        var harmony = AssemblyLoadContext.Default.LoadFromAssemblyPath(
            Path.Combine(Path.GetDirectoryName(target.Location)!, "0Harmony.dll"));
        return Activator.CreateInstance(harmony.GetType("HarmonyLib.Harmony", true)!, owner)!;
    }

    private static void CheckPair(object first, object second, string counter)
    {
        if (!Field(first, "Checksum").Equals(Field(second, "Checksum")))
            throw new InvalidOperationException("original/patched checksum mismatch");
        if (counter.Length != 0 && !Field(first, counter).Equals(Field(second, counter)))
            throw new InvalidOperationException("original/patched counter mismatch: " + counter);
    }

    private static void Emit(object value, bool patched, string counter)
    {
        Console.WriteLine("R3_SAMPLE " + JsonSerializer.Serialize(new
        {
            variant = patched ? "patched" : "original", pair,
            milliseconds = (double)Field(value, "Milliseconds"),
            allocated_bytes = (long)Field(value, "Allocated"),
            checksum = Field(value, "Checksum"),
            counter_name = counter, counter = Field(value, counter)
        }));
    }

    private static void Conveyor(string name)
    {
        Configure("conveyorPathLookup");
        var scenario = name switch
        {
            "long-misses" => (100, 32, 256, Enumerable.Range(20000, 128).ToArray()),
            "shared-rails" => (100, 32, 256, new[] { 65000 }.Concat(Enumerable.Range(0, 126)).Concat(new[] { 65001 }).ToArray()),
            "early-hits" => (1000, 1, 1024, new[] { 65000 }.Concat(Enumerable.Repeat(0, 126)).Concat(new[] { 65001 }).ToArray()),
            "single-query" => (1000, 1, 1024, new[] { 65000 }),
            "short-paths" => (1000, 8, 16, Enumerable.Range(20000, 16).ToArray()),
            "contained-path" => (100000, 8, 256, Enumerable.Range(0, 32).ToArray()),
            _ => throw new ArgumentException("unknown conveyor scenario")
        };
        var harmony = Harmony("CycleTrim.Tests.ConveyorBenchmark");
        object Measure(bool patched) => Call(Test("ConveyorPathBenchmark"), "Measure", harmony, patched,
            scenario.Item1, scenario.Item2, scenario.Item3, scenario.Item4)!;
        bool firstPatched = pair % 2 != 0;
        object first = Measure(firstPatched), second = Measure(!firstPatched);
        CheckPair(first, second, "Reads");
        Emit(first, firstPatched, "Reads");
        Emit(second, !firstPatched, "Reads");
    }

    private static void Power(string name)
    {
        Configure("powerOverlay");
        int unique = name switch
        {
            "repeated-circuits" => 32,
            "all-distinct-circuits" => 512,
            _ => throw new ArgumentException("unknown power scenario")
        };
        var harmony = Harmony("CycleTrim.Tests.PowerOverlayBenchmark");
        object Measure(bool patched) => Call(Test("PowerOverlayBenchmark"), "Measure", harmony, patched, unique)!;
        bool firstPatched = pair % 2 != 0;
        object first = Measure(firstPatched), second = Measure(!firstPatched);
        CheckPair(first, second, "");
        Emit(first, firstPatched, "Calls");
        Emit(second, !firstPatched, "Calls");
    }

    private static void Animation(string name, string mode)
    {
        Call(Test("AnimationSymbolFixture"), "InstallOriginalCopies");
        Call(Test("AnimationSymbolRegression"), "Enable");
        var parameters = name switch
        {
            "repeat-single" => (1, false, false), "change-single" => (1, true, false),
            "repeat-range4" => (4, false, false), "repeat-range256" => (256, false, false),
            "change-range4" => (4, true, false), "change-range256" => (256, true, false),
            "change-last-range256" => (256, true, true),
            _ => throw new ArgumentException("unknown animation scenario")
        };
        bool consume = mode switch
        {
            "setter" => false, "consumer" => true,
            _ => throw new ArgumentException("unknown animation mode")
        };
        var scenarioType = Test("AnimationSymbolBenchmark").GetNestedType("Scenario", BindingFlags.NonPublic)!;
        var constructor = scenarioType.GetConstructor(Instance, null,
            new[] { typeof(string), typeof(int), typeof(bool), typeof(bool) }, null)!;
        var scenario = constructor.Invoke(new object[] { name, parameters.Item1, parameters.Item2, parameters.Item3 });
        object Measure(bool patched, int updates, int sample) => Call(Test("AnimationSymbolBenchmark"), "Measure",
            scenario, consume, patched, updates, sample)!;
        for (int warmup = 0; warmup < 3; warmup++)
        {
            Measure(false, 5000, -1);
            Measure(true, 5000, -1);
        }
        bool firstPatched = pair % 2 != 0;
        object first = Measure(firstPatched, 30000, pair), second = Measure(!firstPatched, 30000, pair);
        CheckPair(first, second, "");
        if (consume)
        {
            object original = firstPatched ? second : first, patched = firstPatched ? first : second;
            long expected = parameters.Item2 ? 30000 : 0;
            if ((long)Field(original, "Copies") != 30000 || (long)Field(patched, "Copies") != expected
                || (long)Field(original, "CopiedBytes") != 30000L * 256 * 48
                || (long)Field(patched, "CopiedBytes") != expected * 256 * 48)
                throw new InvalidOperationException("animation consumer copy contract mismatch");
        }
    }
}
