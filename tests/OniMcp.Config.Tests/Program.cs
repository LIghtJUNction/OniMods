using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Support;

internal static class Program
{
    private static readonly FieldInfo CurrentField = typeof(OniMcpOptions)
        .GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);

    private static void Main()
    {
        Run("missing config keeps authentication opt-in", MissingConfig);
        Run("invalid initial config remains untouched and prevents startup", InvalidInitialConfig);
        Run("unreadable initial config prevents startup", UnreadableInitialConfig);
        Run("failed reload retains active authentication and original file", FailedReload);
        Run("legacy config preserves authentication during migration", LegacyConfig);
        Run("concurrent first access publishes a single configuration", ConcurrentInitialization);
        Run("concurrent saves expose complete JSON to readers", AtomicSaves);
        Run("failed save retains current configuration and cleans temporary files", FailedSave);
        Console.WriteLine("PASS: 8 OniMcp config regression tests");
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
