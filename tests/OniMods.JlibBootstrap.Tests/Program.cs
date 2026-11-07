using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KMod;
using OniMods.JlibBootstrap;

internal static class Program
{
    private static int assertions;
    private static readonly string[] Cases = {
        "missing", "disabled", "duplicate", "not-installed", "missing-library", "invalid-path",
        "bad-abi", "bad-static-id", "no-registrar", "wrong-name", "conflicting-path", "multiple-loaded", "lifecycle", "already-loaded", "missing-payload", "notices", "notice-retry"
    };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--check-package")
            {
                PackageVerifier.Check(args[1], args[2]);
                Console.WriteLine("PASS " + args[2] + " package: bootstrap gate, external Jlib implementation, no embedded PLib or game libraries.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--case")
            {
                RunCase(args[1]);
                Console.WriteLine("PASS " + args[1] + ": " + assertions + " assertions");
                return 0;
            }
            if (args.Length != 0) throw new ArgumentException("Use --check-package <ZIP> <ConsumerName>, --case <scenario>, or no arguments for host fixtures.");
            var diagnostics = new StringWriter();
            var previous = Console.Out;
            try
            {
                Console.SetOut(diagnostics);
                StartupDiagnostics.Report("FixtureConsumer", "fixture_phase", new TargetInvocationException(
                    new InvalidOperationException("/private-provider-path password=private-credential")));
            }
            finally { Console.SetOut(previous); }
            string diagnostic = diagnostics.ToString();
            Check(diagnostic.Contains("fixture_phase") && diagnostic.Contains("TargetInvocationException") && diagnostic.Contains("InvalidOperationException") &&
                diagnostic.Contains("0x") && !diagnostic.Contains("private-provider-path") && !diagnostic.Contains("private-credential"),
                "startup diagnostics retain phase, exception types and HRESULTs without private exception messages");
            Console.WriteLine("PASS sanitized startup diagnostics");
            foreach (string scenario in Cases)
            {
                var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                start.ArgumentList.Add("--case"); start.ArgumentList.Add(scenario);
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(30000)) { process.Kill(true); throw new Exception("Child fixture timed out: " + scenario); }
                    Console.Write(output.GetAwaiter().GetResult());
                    if (process.ExitCode != 0) throw new Exception("Child fixture failed: " + scenario + "\n" + error.GetAwaiter().GetResult());
                }
            }
            Console.WriteLine("PASS " + Cases.Length + " isolated bootstrap scenarios. API doubles and owned fixture assemblies only; no ONI/Mono runtime acceptance.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }

    private static string Fixture(string name, string file = "Jlib.dll")
    { return Path.Combine(AppContext.BaseDirectory, "fixtures", name, file); }

    private static Mod Provider(string root, string fixture = "good")
    {
        Directory.CreateDirectory(root);
        if (fixture != null) File.Copy(Fixture(fixture), Path.Combine(root, "Jlib.dll"));
        return new Mod { staticID = DependencyLoader.StaticId, ContentPath = root };
    }

    private static void RunCase(string scenario)
    {
        string root = Path.Combine(Path.GetTempPath(), "jlib-bootstrap-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Global.Instance = new Global();
            var consumer = new Mod { staticID = "LIghtJUNction.FixtureConsumer", ContentPath = Path.Combine(root, "consumer") };
            Directory.CreateDirectory(Path.Combine(consumer.ContentPath, "implementation"));
            File.Copy(Fixture("payload", "FixtureConsumer.dll"), Path.Combine(consumer.ContentPath, "implementation", "FixtureConsumer.dll"));
            var bootstrap = new BootstrapMod { assembly = typeof(BootstrapMod).Assembly, path = consumer.ContentPath, mod = consumer };
            var harmony = new Harmony(consumer.staticID);
            consumer.loaded_mod_data.dlls.Add(bootstrap.assembly);
            consumer.loaded_mod_data.userMod2Instances.Add(bootstrap.assembly, bootstrap);
            var mods = Global.Instance.modManager.mods;
            mods.Add(consumer); // Provider intentionally comes after its consumer.
            Mod provider = null;
            if (scenario != "missing" && scenario != "notices" && scenario != "notice-retry")
            {
                string fixture = scenario == "bad-abi" || scenario == "bad-static-id" || scenario == "no-registrar" ? scenario :
                    scenario == "missing-library" || scenario == "invalid-path" || scenario == "wrong-name" ? null : "good";
                provider = Provider(Path.Combine(root, "provider"), fixture);
                mods.Add(provider);
            }
            if (scenario == "disabled") provider.Enabled = false;
            if (scenario == "duplicate") mods.Add(Provider(Path.Combine(root, "duplicate")));
            if (scenario == "not-installed") provider.status = Mod.Status.NotInstalled;
            if (scenario == "invalid-path") provider.ContentPath = "\0private-provider-path";
            if (scenario == "wrong-name") File.Copy(typeof(Mod).Assembly.Location, Path.Combine(provider.ContentPath, "Jlib.dll"));
            Assembly earlier = null;
            if (scenario == "conflicting-path" || scenario == "multiple-loaded")
            {
                var other = Provider(Path.Combine(root, "already-loaded-elsewhere"));
                if (scenario == "multiple-loaded")
                {
                    Assembly.LoadFrom(Path.Combine(provider.ContentPath, "Jlib.dll"));
                    Assembly.LoadFile(Path.Combine(other.ContentPath, "Jlib.dll"));
                    Check(AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Jlib") == 2,
                        "fixture actually loaded two separate Jlib assemblies");
                }
                else earlier = Assembly.LoadFrom(Path.Combine(other.ContentPath, "Jlib.dll"));
            }
            if (scenario == "already-loaded") earlier = Assembly.LoadFrom(Path.Combine(provider.ContentPath, "Jlib.dll"));
            if (scenario == "lifecycle")
            {
                var disabled = Provider(Path.Combine(root, "disabled-copy")); disabled.Enabled = false; mods.Add(disabled);
            }
            if (scenario == "missing-payload") File.Delete(Path.Combine(consumer.ContentPath, "implementation", "FixtureConsumer.dll"));
            bootstrap.OnLoad(harmony);
            if (scenario == "lifecycle" || scenario == "already-loaded")
            {
                bootstrap.OnLoad(harmony);
                var payload = AppDomain.CurrentDomain.GetData("fixture.assembly") as Assembly;
                Check((int)(AppDomain.CurrentDomain.GetData("fixture.onload") ?? 0) == 1, "implementation OnLoad is forwarded exactly once");
                Check(payload != null && payload.GetName().Name == "FixtureConsumer", "implementation retains its original assembly identity");
                Check((string)AppDomain.CurrentDomain.GetData("fixture.path") == consumer.ContentPath &&
                    ReferenceEquals(AppDomain.CurrentDomain.GetData("fixture.mod"), consumer), "implementation receives native root path and owner");
                Check(ReferenceEquals(AppDomain.CurrentDomain.GetData("fixture.registered.assembly"), payload) &&
                    ReferenceEquals(AppDomain.CurrentDomain.GetData("fixture.registered.owner"), consumer) &&
                    Equals(AppDomain.CurrentDomain.GetData("fixture.ctor.saw.registration"), true), "Jlib registration precedes implementation construction and OnLoad");
                Check(harmony.Patched.Count == 1 && ReferenceEquals(harmony.Patched[0], payload), "base OnLoad patches the payload only, never the bootstrap");
                foreach (var pair in consumer.loaded_mod_data.userMod2Instances) pair.Value.OnAllModsLoaded(harmony, mods);
                bootstrap.OnAllModsLoaded(harmony, mods);
                Check((int)(AppDomain.CurrentDomain.GetData("fixture.postload") ?? 0) == 1 &&
                    ReferenceEquals(AppDomain.CurrentDomain.GetData("fixture.mods"), mods), "post-load forwards the original native roster exactly once");
                Check(consumer.loaded_mod_data.dlls.Contains(payload) && consumer.loaded_mod_data.userMod2Instances.Count == 1,
                    "post-load records payload ownership without mutating the native callback dictionary");
                var later = Assembly.LoadFrom(Path.Combine(provider.ContentPath, "Jlib.dll"));
                Check(AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Jlib") == 1 &&
                    (earlier == null || ReferenceEquals(earlier, later)), "later native-style LoadFrom reuses the shared provider assembly");
                Check(AppDomain.CurrentDomain.GetData("OniMods.JlibBootstrap.Notices.v1") == null,
                    "successful startup creates no dependency warning");
            }
            else
            {
                Check(AppDomain.CurrentDomain.GetData("fixture.onload") == null && harmony.Patched.Count == 0,
                    "dependency failure never starts or patches the implementation");
                Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "FixtureConsumer"),
                    "dependency failure never loads the implementation assembly or scans its types");
                bootstrap.OnAllModsLoaded(harmony, mods);
                Check(AppDomain.CurrentDomain.GetData("fixture.postload") == null, "blocked implementation receives no post-load callback");
                if (scenario == "notices")
                {
                    var siblingAssembly = Assembly.LoadFrom(Fixture("sibling", "SiblingConsumer.Bootstrap.dll"));
                    var sibling = (UserMod2)Activator.CreateInstance(siblingAssembly.GetType(typeof(BootstrapMod).FullName));
                    sibling.mod = consumer; sibling.path = consumer.ContentPath; sibling.assembly = siblingAssembly;
                    sibling.OnLoad(harmony);
                    Check(Global.Instance.StartedCoroutines == 1, "bootstrap copies share one BCL notice queue and native UI runner");
                }
                Global.Instance.Frame(); Global.Instance.Frame();
                Check(ConfirmDialogScreen.Messages.Count == 0, "warning waits until the native main menu exists");
                MainMenu.Instance = new MainMenu();
                if (scenario == "notice-retry") ConfirmDialogScreen.FailNextPopup = true;
                Global.Instance.Frame(); Global.Instance.Frame();
                Check(ConfirmDialogScreen.Messages.Count == 1, "dependency failure displays one native confirmation dialog");
                if (scenario == "notice-retry") Check(UnityEngine.Object.Destroyed == 1,
                    "transient native UI failure destroys the failed dialog and retries the retained notice");
                string message = ConfirmDialogScreen.Messages[0];
                Check(message.Contains("FixtureConsumer") && message.Contains("restart") && !message.Contains(root) &&
                    !message.Contains("private-provider-path") && !message.Contains("Exception"), "player message is actionable and excludes private paths or stack traces");
                if (scenario == "notices") Check(message.Contains("SiblingConsumer"), "cross-assembly failures appear in the same native message");
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
