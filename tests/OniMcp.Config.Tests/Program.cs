using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Support;
using PeterHan.PLib;
using PeterHan.PLib.Options;

internal static class Program
{
    private static readonly FieldInfo CurrentField = typeof(OniMcpOptions)
        .GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);

    private static void Main()
    {
        Run("missing config keeps authentication opt-in", MissingConfig);
        Run("loopback hosts stay available without authentication", LoopbackHosts);
        Run("unauthenticated remote hosts fail closed", RemoteHostsRequireAuthentication);
        Run("authenticated remote HTTP is allowed with a plaintext warning", AuthenticatedRemoteHost);
        Run("invalid initial config remains untouched and prevents startup", InvalidInitialConfig);
        Run("unreadable initial config prevents startup", UnreadableInitialConfig);
        Run("failed reload retains active authentication and original file", FailedReload);
        Run("legacy config preserves authentication during migration", LegacyConfig);
        Run("concurrent first access publishes a single configuration", ConcurrentInitialization);
        Run("concurrent saves expose complete JSON to readers", AtomicSaves);
        Run("failed save retains current configuration and cleans temporary files", FailedSave);
        Run("options status stays compact, token stays masked, and support link opens", OptionsSurface);
        Console.WriteLine("PASS: 12 OniMcp config regression tests");
    }

    private static void Run(string name, Action test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "OniMcp.Config.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        OniMcpPaths.ConfigPath = Path.Combine(directory, "OniMcpConfig.json");
        OniMcpPaths.ModPath = directory;
        CurrentField.SetValue(null, null);
        try
        {
            test();
            Console.WriteLine("PASS: " + name);
        }
        finally
        {
            CurrentField.SetValue(null, null);
            Directory.Delete(directory, true);
        }
    }

    private static void MissingConfig()
    {
        var options = OniMcpOptions.Current;
        Check(!options.AuthEnabled && options.Host == "localhost", "New installs must keep the existing defaults.");
        Check(!string.IsNullOrEmpty(options.AuthToken), "New installs need a persistent token ready for opt-in.");
        Check(JObject.Parse(File.ReadAllText(OniMcpPaths.ConfigPath))["AuthToken"].Value<string>() == options.AuthToken,
            "The default token must be saved exactly once.");
    }

    private static void LoopbackHosts()
    {
        foreach (string host in new[] { "localhost", "127.0.0.1", "::1", "[::1]" })
        {
            var options = new OniMcpOptions { Host = host, AuthEnabled = false };
            OniMcpOptions.Save(options);
            options.ValidateListenSecurity();
            string[] prefixes = options.ListenPrefixes.ToArray();
            Check(prefixes.Length > 0, "Loopback host produced no listener prefixes: " + host);
            Check(prefixes.All(prefix => !prefix.Contains("http://+:")), "Loopback host widened to a wildcard listener: " + host);
        }

        var ipv6 = new OniMcpOptions { Host = "::1", AuthEnabled = false };
        Check(ipv6.ListenPrefixes.All(prefix => prefix.Contains("http://[::1]:")), "IPv6 loopback prefixes must use bracketed URI syntax.");
        Check(ipv6.EndpointUrl.StartsWith("http://[::1]:", StringComparison.Ordinal), "IPv6 loopback endpoint URL is malformed.");
    }

    private static void RemoteHostsRequireAuthentication()
    {
        foreach (string host in new[] { "0.0.0.0", "192.0.2.10", "2001:db8::10", "mcp.example.test" })
        {
            var options = new OniMcpOptions { Host = host, AuthEnabled = false };
            ExpectFailure(options.ValidateListenSecurity);
            ExpectFailure(() => OniMcpOptions.Save(options));
            Check(!File.Exists(OniMcpPaths.ConfigPath), "Unsafe remote settings were persisted for " + host);
        }
    }

    private static void AuthenticatedRemoteHost()
    {
        var options = new OniMcpOptions
        {
            Host = "0.0.0.0",
            AuthEnabled = true,
            AuthToken = "remote-secret"
        };
        OniMcpOptions.Save(options);
        options.ValidateListenSecurity();
        Check(!string.IsNullOrEmpty(options.PlaintextRemoteWarning), "Remote HTTP must expose a plaintext/TLS warning.");
        Check(options.ListenPrefixes.Any(prefix => prefix == "http://+:8788/"), "Authenticated wildcard bind did not produce the expected listener prefix.");

        var local = new OniMcpOptions { Host = "localhost", AuthEnabled = false };
        Check(string.IsNullOrEmpty(local.PlaintextRemoteWarning), "Loopback-only HTTP must not be labeled as remote exposure.");
    }

    private static void InvalidInitialConfig()
    {
        const string malformed = "{\"AuthEnabled\":true,\"AuthToken\":\"secret\"";
        File.WriteAllText(OniMcpPaths.ConfigPath, malformed);
        ExpectFailure(() => { var ignored = OniMcpOptions.Current; });
        Check(File.ReadAllText(OniMcpPaths.ConfigPath) == malformed, "Failed reads must not destroy the original file.");
        Check(CurrentField.GetValue(null) == null, "Invalid initial settings must not publish unauthenticated defaults.");
    }

    private static void UnreadableInitialConfig()
    {
        Directory.CreateDirectory(OniMcpPaths.ConfigPath);
        ExpectFailure(() => { var ignored = OniMcpOptions.Current; });
        Check(CurrentField.GetValue(null) == null, "A path that cannot be read must not publish defaults.");
    }

    private static void FailedReload()
    {
        var options = new OniMcpOptions { AuthEnabled = true, AuthToken = "retain-me", Port = 9234 };
        OniMcpOptions.Save(options);
        File.WriteAllText(OniMcpPaths.ConfigPath, "incomplete write");
        OniMcpOptions.Reload();
        Check(ReferenceEquals(options, OniMcpOptions.Current), "A failed reload must preserve active settings.");
        Check(OniMcpOptions.Current.AuthEnabled && OniMcpOptions.Current.AuthToken == "retain-me", "Authentication changed.");
        Check(File.ReadAllText(OniMcpPaths.ConfigPath) == "incomplete write", "Reload must leave the file available for recovery.");
    }

    private static void LegacyConfig()
    {
        File.WriteAllText(OniMcpPaths.ConfigPath, "{\"AuthEnabled\":true,\"AuthToken\":\" token \",\"Port\":99999}");
        var options = OniMcpOptions.Current;
        Check(options.AuthEnabled && options.AuthToken == "token", "Migration must retain opted-in authentication.");
        Check(options.Port == 8788 && options.SecurityMigrationVersion == 1, "Migration and validation did not run.");
        OniMcpOptions.Reload();
        Check(OniMcpOptions.Current.AuthEnabled && OniMcpOptions.Current.AuthToken == "token", "Migrated settings did not persist.");
    }

    private static void ConcurrentInitialization()
    {
        var observed = new OniMcpOptions[16];
        using (var start = new ManualResetEventSlim())
        {
            var threads = Enumerable.Range(0, observed.Length).Select(index => new Thread(() =>
            {
                start.Wait();
                observed[index] = OniMcpOptions.Current;
            })).ToArray();
            foreach (var thread in threads)
                thread.Start();
            start.Set();
            foreach (var thread in threads)
                thread.Join();
        }
        Check(observed.All(options => ReferenceEquals(options, observed[0])), "Concurrent readers received different settings / tokens.");
        Check(JObject.Parse(File.ReadAllText(OniMcpPaths.ConfigPath))["AuthToken"].Value<string>() == observed[0].AuthToken,
            "The published token differs from the saved token.");
    }

    private static void AtomicSaves()
    {
        OniMcpOptions.Save(new OniMcpOptions());
        using (var stop = new CancellationTokenSource())
        using (var started = new ManualResetEventSlim())
        {
            int reads = 0;
            var reader = Task.Run(() =>
            {
                do
                {
                    using (var stream = new FileStream(OniMcpPaths.ConfigPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    using (var text = new StreamReader(stream))
                        JObject.Parse(text.ReadToEnd());
                    Interlocked.Increment(ref reads);
                    started.Set();
                } while (!stop.IsCancellationRequested);
            });
            try
            {
                Check(started.Wait(TimeSpan.FromSeconds(10)), "The config reader did not start.");
                Parallel.For(0, 100, index => OniMcpOptions.Save(new OniMcpOptions
                {
                    AuthEnabled = true,
                    AuthToken = new string((char)('a' + index % 26), 32768),
                    Port = 9000 + index
                }));
            }
            finally
            {
                stop.Cancel();
                reader.GetAwaiter().GetResult();
            }
            Check(reads > 1, "The test did not observe concurrent config reads.");
            var saved = JObject.Parse(File.ReadAllText(OniMcpPaths.ConfigPath));
            Check(saved["Port"].Value<int>() == OniMcpOptions.Current.Port, "Disk and active settings disagree.");
        }
        Check(!Directory.GetFiles(OniMcpPaths.ModPath, "*.tmp").Any(), "Successful saves left temporary files.");
    }

    private static void FailedSave()
    {
        var options = new OniMcpOptions { AuthEnabled = true, AuthToken = "keep-me" };
        OniMcpOptions.Save(options);
        string originalPath = OniMcpPaths.ConfigPath;
        string original = File.ReadAllText(originalPath);
        OniMcpPaths.ConfigPath = Path.Combine(OniMcpPaths.ModPath, "blocked");
        Directory.CreateDirectory(OniMcpPaths.ConfigPath);
        ExpectFailure(() => OniMcpOptions.Save(new OniMcpOptions { AuthEnabled = false }));
        Check(ReferenceEquals(options, OniMcpOptions.Current), "A failed save replaced the active settings.");
        Check(File.ReadAllText(originalPath) == original, "A failed save changed the previous file.");
        Check(!Directory.GetFiles(OniMcpPaths.ModPath, "*.tmp").Any(), "A failed replacement left a temporary file.");
    }

    private static void OptionsSurface()
    {
        OniMcpPaths.ConfigPath = Path.Combine(OniMcpPaths.ModPath, new string('x', 240), "OniMcpConfig.json");
        var options = new OniMcpOptions { Host = new string('h', 100), AuthToken = "never-show-this-token" };
        var entries = options.CreateOptions().ToArray();
        var status = entries.OfType<TextBlockOptionsEntry>().Single();
        Check(status.Option.Title.Split('\n').All(line => line.Length <= 60), "Long paths or hosts widened the options dialog.");
        Check(!status.Option.Title.Contains(OniMcpPaths.ConfigPath), "The full config path appeared in the dialog.");
        Check(status.Option.Tooltip.Contains(OniMcpPaths.ConfigPath), "The full config path is not available in the status tooltip.");
        Check(entries.OfType<TextBlockOptionsEntry>().All(entry => !entry.Option.Title.Contains(options.AuthToken)),
            "The token appeared in visible status text.");
        Check(typeof(OniMcpOptions).GetProperty(nameof(OniMcpOptions.AuthToken))
            .GetCustomAttributes(typeof(DynamicOptionAttribute), false).Length == 1,
            "The token no longer uses the masked input handler.");

        var support = entries.OfType<ButtonOptionsEntry>().Single(entry => entry.Name == "OpenProjectSupport");
        Check(support.Option.Category == "Support" && support.Option.Title.Contains("api.lmm.best"),
            "The optional support link is not recognizable in its own section.");
        ((Action<object>)support.Value)(null);
        Check(UnityEngine.Application.LastOpenedUrl == "https://api.lmm.best", "The support button opened the wrong URL.");
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException) { return; }
        throw new Exception("Expected the operation to fail.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
