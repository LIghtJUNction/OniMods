using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using HarmonyLib;
using KMod;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace CycleTrim.Conformance.Memory
{
    // Separate test mod. No GC, unloading, allocator policy or gameplay changes.
    public sealed class MemoryProbe : UserMod2
    {
        private const string ManagedSha = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        private static string root;
        private static string lastInput;
        private static int pid;
        private static int lastRequest;
        private static float nextCheck;

        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            string[] args = Environment.GetCommandLineArgs();
            int control = Array.IndexOf(args, "--oniharness-control");
            if (Array.IndexOf(args, "--cycletrim-memory-probe") < 0 || control < 0 || control + 1 >= args.Length)
                return;
            root = Path.GetFullPath(args[control + 1]);
            if (!Directory.Exists(root))
                throw new InvalidOperationException("Memory probe needs an existing owned control directory");
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(typeof(Game).Assembly.Location))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (actual != ManagedSha)
                    throw new InvalidOperationException("Memory probe requires the pinned installed managed assembly");
            }
            using (var process = Process.GetCurrentProcess()) pid = process.Id;
            var update = AccessTools.Method(typeof(Game), "Update");
            if (update == null) throw new InvalidOperationException("Pinned Game.Update entry is absent");
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(MemoryProbe), nameof(Observe)));
            UnityEngine.Debug.Log("[CycleTrim.MemoryProbe] Read-only explicit memory snapshots armed");
        }

        private static void Observe()
        {
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 1;
            if (Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading()
                || SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused) return;
            string requestPath = Path.Combine(root, "memory-probe-request.json");
            if (!File.Exists(requestPath)) return;
            JObject result = null;
            try
            {
                if (new FileInfo(requestPath).Length > 512)
                    throw new InvalidOperationException("Memory request exceeds 512 bytes");
                string input = File.ReadAllText(requestPath);
                if (input == lastInput) return;
                lastInput = input;
                var request = JObject.Parse(input);
                int sequence = (int)request["request"];
                if (sequence <= lastRequest) return;
                var status = MemoryRequest.Validate(request,
                    File.ReadAllText(Path.Combine(root, "instance-status.json")), pid, System.DateTime.UtcNow);
                if (GameClock.Instance == null)
                    throw new InvalidOperationException("Native clock is absent");
                double nativeSeconds = GameClock.Instance.GetTime();
                if ((double?)status["simulationSeconds"] != nativeSeconds)
                    throw new InvalidOperationException("Wait for telemetry to publish the same paused native clock");
                lastRequest = sequence;
                var processMemory = ReadProcessMemory();
                // Read counters before constructing the response. These values
                // can overlap; they are not categories to add together.
                long allocated = Profiler.GetTotalAllocatedMemoryLong();
                long reserved = Profiler.GetTotalReservedMemoryLong();
                long unused = Profiler.GetTotalUnusedReservedMemoryLong();
                long monoUsed = Profiler.GetMonoUsedSizeLong();
                long monoHeap = Profiler.GetMonoHeapSizeLong();
                long managed = GC.GetTotalMemory(false);
                int maxGeneration = GC.MaxGeneration;
                var collections = new JArray();
                for (int generation = 0; generation <= maxGeneration; generation++)
                    collections.Add(GC.CollectionCount(generation));
                result = new JObject
                {
                    ["schema"] = 1, ["request"] = sequence, ["pid"] = pid,
                    ["runNonce"] = status["runNonce"].DeepClone(),
                    ["identity"] = status["identity"].DeepClone(),
                    ["simulationSeconds"] = nativeSeconds,
                    ["activeSavePath"] = status["activeSavePath"].DeepClone(),
                    ["paused"] = true, ["frame"] = Time.frameCount,
                    ["graphicsDevice"] = SystemInfo.graphicsDeviceType.ToString(),
                    ["unityAllocatedBytes"] = allocated, ["unityReservedBytes"] = reserved,
                    ["unityUnusedReservedBytes"] = unused,
                    ["monoUsedBytes"] = monoUsed, ["monoHeapBytes"] = monoHeap,
                    ["managedReportedBytes"] = managed,
                    ["gcMaxGeneration"] = maxGeneration, ["gcCollections"] = collections,
                    ["processMemoryKiB"] = processMemory,
                    ["capturedAt"] = System.DateTime.UtcNow.ToString("o"),
                    ["complete"] = true,
                    ["scope"] = "Read-only nearby counter snapshots; overlapping counters, not exact allocator attribution"
                };
            }
            catch (Exception error)
            {
                result = new JObject { ["complete"] = false, ["pid"] = pid,
                    ["error"] = error.Message, ["capturedAt"] = System.DateTime.UtcNow.ToString("o") };
            }
            Write(result);
        }

        private static JObject ReadProcessMemory()
        {
            var result = new JObject();
            foreach (string line in File.ReadAllLines("/proc/self/smaps_rollup"))
            {
                int split = line.IndexOf(':');
                if (split < 0) continue;
                string key = line.Substring(0, split);
                if (key != "Rss" && key != "Pss" && key != "Swap" && key != "SwapPss"
                    && key != "Anonymous" && key != "Private_Dirty" && key != "Private_Clean"
                    && key != "Shared_Dirty" && key != "Shared_Clean") continue;
                string value = line.Substring(split + 1).Trim().Split(' ')[0];
                result[key] = long.Parse(value, CultureInfo.InvariantCulture);
            }
            return result;
        }

        private static void Write(JObject result)
        {
            if (result == null) return;
            try
            {
                string path = Path.Combine(root, "memory-probe.json");
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, result.ToString());
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("[CycleTrim.MemoryProbe] Receipt failed: " + error.Message);
            }
        }
    }
}
