using System;
using System.IO;
using System.Threading.Tasks;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class FetchLayoutControlRegression
    {
        private static int assertions;

        internal static void Run()
        {
            Check(FetchLayoutControl.FindControlPath(new[] { "game", "--unrelated" }) == null,
                "ordinary launch has no experiment path");
            Throws<ArgumentException>(() => FetchLayoutControl.FindControlPath(
                new[] { "--cycletrim-layout-control=" }), "empty argument");
            Throws<ArgumentException>(() => FetchLayoutControl.FindControlPath(
                new[] { "--cycletrim-layout-control=one", "--cycletrim-layout-control=two" }),
                "duplicate argument");
            Throws<ArgumentOutOfRangeException>(() => new FetchLayoutControl("control", 0),
                "positive process identity");
            var directory = Path.Combine(Path.GetTempPath(), "cycletrim-layout-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "layout.ctrl");
                Check(FetchLayoutControl.FindControlPath(new[] { "--cycletrim-layout-control=" + path }) == path,
                    "explicit argument resolves path");
                var control = new FetchLayoutControl(path, 123);
                Check(!control.Compact, "new session starts with baseline");
                Check(!control.Poll(true), "missing request has no acknowledgment");
                File.WriteAllText(path, "compact paused_request");
                Check(!control.Poll(false), "running or unloaded game defers request");
                Check(!control.Compact && !File.Exists(path + ".ack"), "deferral preserves baseline");
                control.RecordUpdate(false);
                Check(control.Poll(true) && control.Compact, "paused game accepts compact");
                Check(File.ReadAllText(path + ".ack") == "123 compact paused_request 1 0",
                    "acknowledgment binds process, nonce and actual counters");
                var acknowledgment = File.ReadAllText(path + ".ack");
                control.RecordUpdate(true);
                Check(!control.Poll(true), "same request is not acknowledged twice");
                Check(File.ReadAllText(path + ".ack") == acknowledgment, "duplicate leaves evidence intact");
                Parallel.For(0, 1000, index => control.RecordUpdate(true));
                File.WriteAllText(path, "compact snapshot_2");
                Check(control.Poll(true), "fresh nonce snapshots same layout");
                Check(File.ReadAllText(path + ".ack") == "123 compact snapshot_2 1 1001",
                    "concurrent call counters are exact");

                foreach (var invalid in new[] { "", "compact", "unknown nonce", "compact a b",
                    "compact n\nonce", "compact ../nonce", "compact \u0000", "compact \u4e2d",
                    "compact " + new string('x', 65), new string('x', 129) })
                {
                    File.WriteAllText(path, invalid);
                    var before = File.ReadAllText(path + ".ack");
                    Throws<InvalidDataException>(() => control.Poll(true), "invalid control request");
                    Check(control.Compact && File.ReadAllText(path + ".ack") == before,
                        "invalid request preserves selected layout and previous acknowledgment");
                }

                File.WriteAllText(path, "baseline resumed");
                Check(control.Poll(true) && !control.Compact, "explicit baseline restores collector");
                Check(File.ReadAllText(path + ".ack") == "123 baseline resumed 1 1001",
                    "switching never resets actual counters");

                File.Delete(path + ".ack");
                Directory.CreateDirectory(path + ".ack");
                File.WriteAllText(path, "compact write_failure");
                Throws<IOException>(() => control.Poll(true), "acknowledgment failure");
                Check(!control.Compact, "failed publication rolls back layout");
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "failed publication cleans its temporary file");
                Directory.Delete(path + ".ack");
                Check(control.Poll(true) && control.Compact, "failed request can be retried");
                Check(File.ReadAllText(path + ".ack") == "123 compact write_failure 1 1001",
                    "retry binds the same request to a successful publication");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
            Console.WriteLine("PASS fetch layout control: " + assertions + " assertions");
        }

        private static void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { assertions++; return; }
            throw new InvalidOperationException(message + " was accepted");
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
