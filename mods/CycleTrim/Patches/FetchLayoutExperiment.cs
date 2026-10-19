using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace CycleTrim.Patches
{
    // Explicit lab option only. The default remains the established layout.
    internal sealed class FetchLayoutExperiment : MonoBehaviour
    {
        internal static bool Compact { get; private set; }
        internal static bool Enabled { get; private set; }
        private static long baselineCalls;
        private static long compactCalls;
        private string control;
        private string previous;
        private float nextRead;
        private int pid;

        internal static void StartIfRequested()
        {
            const string prefix = "--cycletrim-layout-control=";
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (!arg.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var owner = new GameObject("CycleTrim.FetchLayoutExperiment");
                UnityEngine.Object.DontDestroyOnLoad(owner);
                var controller = owner.AddComponent<FetchLayoutExperiment>();
                controller.control = Path.GetFullPath(arg.Substring(prefix.Length));
                Enabled = true;
                using (var process = Process.GetCurrentProcess()) controller.pid = process.Id;
                UnityEngine.Debug.Log("[CycleTrim][LayoutLab] baseline; explicit control active");
                return;
            }
        }

        internal static void RecordUpdate(bool compact)
        {
            if (!Enabled) return;
            if (compact) Interlocked.Increment(ref compactCalls);
            else Interlocked.Increment(ref baselineCalls);
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < nextRead || control == null) return;
            nextRead = Time.realtimeSinceStartup + 0.25f;
            try
            {
                if (!File.Exists(control)) return;
                var requested = File.ReadAllText(control).Trim();
                if (requested == previous) return;
                var parts = requested.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || (parts[0] != "baseline" && parts[0] != "compact"))
                    throw new InvalidDataException("Expected baseline|compact REQUEST_ID");
                Compact = parts[0] == "compact";
                var temp = control + ".ack.tmp";
                File.WriteAllText(temp, pid + " " + requested + " " +
                    Interlocked.Read(ref baselineCalls) + " " + Interlocked.Read(ref compactCalls));
                var destination = control + ".ack";
                if (File.Exists(destination)) File.Replace(temp, destination, null);
                else File.Move(temp, destination);
                previous = requested;
                UnityEngine.Debug.Log("[CycleTrim][LayoutLab] acknowledged " + requested);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[CycleTrim][LayoutLab] " + ex.Message);
            }
        }
    }
}
