using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Optimizations.Tests
{
    internal static class ConduitVisualBenchmark
    {
        private const string Owner = "CycleTrim.Tests.ConduitVisualBenchmark";
        private const int Frames = 6000;
        private const int Warmups = 3;
        private const int Pairs = 7;
        private const int InitialContent = 17;

        private enum Changes
        {
            None, Highlight, Overlay, Audio, SimulationEveryFrame,
            SimulationEveryThirdFrame, LowFrameRate
        }

        private sealed class Scenario
        {
            internal readonly string Name;
            internal readonly int Work;
            internal readonly ConduitAnimationQuality Quality;
            internal readonly Changes Change;
            internal readonly bool RequireSpeedup;
            internal readonly double Step;
            internal readonly int SimulationInterval;
            internal readonly int[] Payloads = new int[Frames];
            internal long MovingSum, StaticSum, OrderedChecksum;

            internal Scenario(string name, int work, ConduitAnimationQuality quality,
                Changes change = Changes.None, bool requireSpeedup = false)
            {
                Name = name;
                Work = work;
                Quality = quality;
                Change = change;
                RequireSpeedup = requireSpeedup;
                Step = change == Changes.LowFrameRate ? 0.125 : 1d / 60d;
                SimulationInterval = change == Changes.SimulationEveryFrame ? 1
                    : change == Changes.SimulationEveryThirdFrame ? 3 : 0;
                var simulation = 0;
                for (var frame = 0; frame < Frames; frame++)
                {
                    if (SimulationInterval > 0 && frame % SimulationInterval == 0)
                        simulation++;
                    var highlight = change == Changes.Highlight ? frame : -1;
                    var payload = unchecked(InitialContent + simulation * 31 + highlight
                        + (change == Changes.Overlay ? 7 : 0) + 51);
                    // Expected fixture payloads are computed outside timing.
                    // Their independent per-frame comparison detects stale
                    // results even when later frames catch up with a change.
                    for (var index = 0; index < work; index++)
                        payload = unchecked(payload * 1664525 + index + 1013904223);
                    Payloads[frame] = payload;
                    MovingSum += payload;
                    StaticSum += ~payload;
                    OrderedChecksum = AppendChecksum(OrderedChecksum, payload, ~payload);
                }
            }

            internal int ExpectedBuilds(bool candidate)
            {
                if (!candidate || Change == Changes.Highlight || Change == Changes.Overlay
                    || Change == Changes.Audio || Change == Changes.SimulationEveryFrame
                    || Change == Changes.LowFrameRate)
                    return Frames;
                var interval = Change == Changes.SimulationEveryThirdFrame ? 3 : (int)Quality;
                return (Frames + interval - 1) / interval;
            }
        }

        private struct Measurement
        {
            internal long Ticks, Allocated, MovingSum, StaticSum, OrderedChecksum;
            internal int Builds, EmptyBuilds, MovingUploads, StaticUploads, Draws;
            internal int SimCalls, SimulationVersion, AudioResets, AudioCalls, InvalidFrames;
            internal int AreaReads;
            internal double AnimationDelta;
            internal double Milliseconds { get { return Ticks * 1000d / Stopwatch.Frequency; } }
        }

        internal static void Run()
        {
            var harmony = new Harmony(Owner);
            try
            {
                InstallOriginals(harmony);
                Configure(ConduitAnimationQuality.Half);
                if (!ConduitVisualRefreshPatch.Install())
                    throw new InvalidOperationException("Conduit benchmark could not install the production patch");
                if (Graphics.Draws.Capacity < 2)
                    Graphics.Draws.Capacity = 2;
                Console.WriteLine("CONDUIT_VISUAL_BENCH_ENV " + JsonSerializer.Serialize(new
                {
                    runtime = RuntimeInformation.FrameworkDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    os = RuntimeInformation.OSDescription,
                    logical_processors = Environment.ProcessorCount,
                    stopwatch_frequency = Stopwatch.Frequency,
                    frames_per_sample = Frames, warmup_pairs = Warmups, measured_pairs = Pairs,
                    maximum_age_seconds = ConduitVisualRefreshGate.MaximumAgeSeconds,
                    dotnet_tiered_compilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                    complus_tiered_compilation = Environment.GetEnvironmentVariable("COMPlus_TieredCompilation"),
                    baseline = "Original RenderMesh and input mutators; original outer Render frozen through a temporary call-site transpiler.",
                    candidate = "Actual Harmony-patched fixture Render, including production gates, snapshots and mutation observers.",
                    allocation_scope = "Measured thread; fixture and first state allocation excluded; draw list reused every frame.",
                    note = "Independent integer-work fixture. No Unity geometry, renderer, official simulation or game FPS is measured."
                }));

                var scenarios = new[]
                {
                    new Scenario("stable-half-medium", 256, ConduitAnimationQuality.Half, requireSpeedup: true),
                    new Scenario("stable-quarter-medium", 256, ConduitAnimationQuality.Quarter, requireSpeedup: true),
                    new Scenario("stable-half-large", 4096, ConduitAnimationQuality.Half, requireSpeedup: true),
                    new Scenario("stable-quarter-large", 4096, ConduitAnimationQuality.Quarter, requireSpeedup: true),
                    new Scenario("stable-half-tiny", 0, ConduitAnimationQuality.Half),
                    new Scenario("stable-quarter-tiny", 0, ConduitAnimationQuality.Quarter),
                    new Scenario("changing-highlight", 256, ConduitAnimationQuality.Quarter, Changes.Highlight),
                    new Scenario("contents-overlay", 256, ConduitAnimationQuality.Quarter, Changes.Overlay),
                    new Scenario("audio-every-frame", 256, ConduitAnimationQuality.Quarter, Changes.Audio),
                    new Scenario("simulation-every-frame", 256, ConduitAnimationQuality.Quarter, Changes.SimulationEveryFrame),
                    new Scenario("low-fps", 256, ConduitAnimationQuality.Quarter, Changes.LowFrameRate),
                    new Scenario("simulation-every-third-frame", 256, ConduitAnimationQuality.Quarter,
                        Changes.SimulationEveryThirdFrame)
                };
                var timingFailures = new List<string>();
                foreach (var scenario in scenarios)
                    if (!MeasureScenario(scenario))
                        timingFailures.Add(scenario.Name);
                // Keep all controls and all raw samples even when a timing
                // acceptance criterion fails. Never omit a slower workload.
                if (timingFailures.Count != 0)
                    throw new InvalidOperationException("Conduit median timing acceptance failed: "
                        + string.Join(", ", timingFailures));
            }
            finally
            {
                harmony.UnpatchAll(Owner);
                Configure(ConduitAnimationQuality.Full);
                ConduitVisualRefreshPatch.ResetSession();
                Graphics.ResetForTest();
            }
        }

        private static void InstallOriginals(Harmony harmony)
        {
            var render = AccessTools.DeclaredMethod(typeof(ConduitFlowVisualizer), "Render");
            var mesh = ConduitVisualRefreshPatch.RenderMethod();
            foreach (var target in new MethodBase[] { render, mesh })
            {
                var patches = Harmony.GetPatchInfo(target);
                if (patches != null && patches.Owners.Count != 0)
                    throw new InvalidOperationException("Run the conduit benchmark in a fresh process before other patch tests");
            }
            Reverse(harmony, mesh, nameof(OriginalMesh));
            Reverse(harmony, AccessTools.DeclaredMethod(typeof(ConduitFlow), "Sim200ms"), nameof(OriginalSimulation));
            Reverse(harmony, AccessTools.DeclaredMethod(typeof(ConduitFlowVisualizer), "SetHighlightedCell"),
                nameof(OriginalHighlight));

            // Copying Render alone would still call the subsequently patched
            // RenderMesh. Freeze the redirection to OriginalMesh in its snapshot
            // and remove the temporary patch before production compatibility checks.
            try
            {
                harmony.Patch(render, transpiler: new HarmonyMethod(typeof(ConduitVisualBenchmark),
                    nameof(RedirectOriginalMesh)));
                harmony.CreateReversePatcher(render, new HarmonyMethod(typeof(ConduitVisualBenchmark),
                    nameof(OriginalRender))).Patch(HarmonyReversePatchType.Snapshot);
            }
            finally
            {
                harmony.UnpatchAll(Owner);
            }
        }

        private static void Reverse(Harmony harmony, MethodBase original, string standin)
        {
            harmony.CreateReversePatcher(original, new HarmonyMethod(typeof(ConduitVisualBenchmark),
                standin)).Patch(HarmonyReversePatchType.Original);
        }

        private static IEnumerable<CodeInstruction> RedirectOriginalMesh(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var target = ConduitVisualRefreshPatch.RenderMethod();
            var replacement = AccessTools.DeclaredMethod(typeof(ConduitVisualBenchmark), nameof(OriginalMesh));
            var matches = 0;
            foreach (var instruction in code)
            {
                if (!instruction.Calls(target))
                    continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException("Expected exactly one original Render to RenderMesh call");
            return code;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OriginalMesh(ConduitFlowVisualizer instance, float z, int render_layer,
            float lerp_percent, bool trigger_audio)
        { throw new NotSupportedException("Original mesh reverse patch was not installed"); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OriginalRender(ConduitFlowVisualizer instance, float z, int render_layer,
            float lerp_percent, bool trigger_audio)
        { throw new NotSupportedException("Original render reverse patch was not installed"); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OriginalSimulation(ConduitFlow instance, float dt)
        { throw new NotSupportedException("Original simulation reverse patch was not installed"); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void OriginalHighlight(ConduitFlowVisualizer instance, int cell)
        { throw new NotSupportedException("Original highlight reverse patch was not installed"); }

        private static bool MeasureScenario(Scenario scenario)
        {
            Configure(scenario.Quality);
            for (var warmup = 0; warmup < Warmups; warmup++)
                MeasurePair(scenario, warmup, "warmup", out _, out _);
            var baselineTimes = new double[Pairs];
            var candidateTimes = new double[Pairs];
            var ratios = new double[Pairs];
            for (var pair = 0; pair < Pairs; pair++)
            {
                MeasurePair(scenario, pair, "measurement", out var baseline, out var candidate);
                baselineTimes[pair] = baseline.Milliseconds;
                candidateTimes[pair] = candidate.Milliseconds;
                ratios[pair] = baseline.Milliseconds / candidate.Milliseconds;
            }
            Array.Sort(baselineTimes);
            Array.Sort(candidateTimes);
            Array.Sort(ratios);
            var improved = candidateTimes[Pairs / 2] < baselineTimes[Pairs / 2];
            var accepted = !scenario.RequireSpeedup || improved;
            Console.WriteLine("CONDUIT_VISUAL_BENCH_SUMMARY " + JsonSerializer.Serialize(new
            {
                scenario = scenario.Name,
                baseline_ms_min = baselineTimes[0], baseline_ms_median = baselineTimes[Pairs / 2],
                baseline_ms_max = baselineTimes[Pairs - 1], candidate_ms_min = candidateTimes[0],
                candidate_ms_median = candidateTimes[Pairs / 2], candidate_ms_max = candidateTimes[Pairs - 1],
                paired_speedup_min = ratios[0], paired_speedup_median = ratios[Pairs / 2],
                paired_speedup_max = ratios[Pairs - 1], median_total_time_improved = improved,
                timing_gate_required = scenario.RequireSpeedup, timing_gate_passed = accepted,
                interpretation = "A speedup below 1 is a retained regression in this synthetic workload, not game FPS."
            }));
            return accepted;
        }

        private static void MeasurePair(Scenario scenario, int pair, string phase,
            out Measurement baseline, out Measurement candidate)
        {
            if ((pair & 1) == 0)
            {
                baseline = Measure(scenario, false);
                candidate = Measure(scenario, true);
            }
            else
            {
                candidate = Measure(scenario, true);
                baseline = Measure(scenario, false);
            }
            Console.WriteLine("CONDUIT_VISUAL_BENCH_SAMPLE " + JsonSerializer.Serialize(new
            {
                scenario = scenario.Name, phase = phase, pair = pair + 1,
                order = (pair & 1) == 0 ? "baseline,candidate" : "candidate,baseline",
                quality = scenario.Quality.ToString(), synthetic_work_iterations = scenario.Work,
                frames = Frames, seconds_per_frame = scenario.Step, simulation_interval = scenario.SimulationInterval,
                baseline = Details(baseline), candidate = Details(candidate),
                speedup = baseline.Milliseconds / candidate.Milliseconds
            }));
            Validate(scenario, baseline, false, phase == "measurement");
            Validate(scenario, candidate, true, phase == "measurement");
            if (baseline.MovingSum != candidate.MovingSum || baseline.StaticSum != candidate.StaticSum
                || baseline.OrderedChecksum != candidate.OrderedChecksum
                || baseline.AnimationDelta != candidate.AnimationDelta)
                throw new InvalidOperationException("Conduit benchmark paired outputs differ: " + scenario.Name);
        }

        private static Measurement Measure(Scenario scenario, bool candidate)
        {
            ConduitVisualRefreshPatch.ResetSession();
            Graphics.ResetForTest();
            GridVisibleArea.BeforeRead = null;
            GridVisibleArea.Area = new GridArea(new Vector2I(0, 0), new Vector2I(31, 31));
            Grid.WidthInCells = Grid.HeightInCells = 256;
            ClusterManager.Instance = new ClusterManager { activeWorldId = 0 };
            var flow = new ConduitFlow { Content = InitialContent };
            var visual = new ConduitFlowVisualizer(flow) { WorkIterations = scenario.Work };
            var overlay = scenario.Change == Changes.Overlay;
            var audio = scenario.Change == Changes.Audio;
            visual.ColourizePipeContents(overlay, overlay);
            Time.frameCount = -1;
            Time.unscaledTimeAsDouble = -1d;
            Time.time = -1f;
            Time.deltaTime = (float)scenario.Step;
            // Allocate the production state before timing, while the negative
            // frame deliberately prevents this prime from authorizing reuse.
            if (candidate) visual.Render(3f, 23, 0f, audio);
            else OriginalRender(visual, 3f, 23, 0f, audio);
            var startedAnimation = visual.AnimationTime;
            visual.BuildCalls = visual.EmptyBuildCalls = 0;
            visual.AudioResets = visual.AudioCalls = 0;
            visual.MovingMesh.Uploads = visual.StaticMesh.Uploads = 0;
            Graphics.ResetForTest();
            GridVisibleArea.Reads = 0;

            long movingSum = 0, staticSum = 0, orderedChecksum = 0;
            int draws = 0, invalidFrames = 0;
            var expectedSimulation = 0;
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var frame = 0; frame < Frames; frame++)
            {
                Time.frameCount = frame;
                Time.unscaledTimeAsDouble = frame * scenario.Step;
                Time.time = (float)Time.unscaledTimeAsDouble;
                if (scenario.SimulationInterval > 0 && frame % scenario.SimulationInterval == 0)
                {
                    if (candidate) flow.Sim200ms(0.2f);
                    else OriginalSimulation(flow, 0.2f);
                    expectedSimulation++;
                }
                if (scenario.Change == Changes.Highlight)
                {
                    if (candidate) visual.SetHighlightedCell(frame);
                    else OriginalHighlight(visual, frame);
                }
                var z = 3f + (frame % 7) * 0.25f;
                if (candidate) visual.Render(z, 23, 0.5f, audio);
                else OriginalRender(visual, z, 23, 0.5f, audio);
                draws += Graphics.Draws.Count;
                if (Graphics.Draws.Count != 2)
                    invalidFrames++;
                else
                {
                    var moving = Graphics.Draws[0];
                    var stationary = Graphics.Draws[1];
                    var payload = scenario.Payloads[frame];
                    if (moving.Content != payload || stationary.Content != ~payload
                        || !ReferenceEquals(moving.Mesh, visual.MovingMesh)
                        || !ReferenceEquals(stationary.Mesh, visual.StaticMesh)
                        || !ValidDraw(moving, z, overlay ? 7 : 0)
                        || !ValidDraw(stationary, z, overlay ? 7 : 0)
                        || (audio && visual.AudioObservedVersion != expectedSimulation))
                        invalidFrames++;
                    movingSum += moving.Content;
                    staticSum += stationary.Content;
                    orderedChecksum = AppendChecksum(orderedChecksum, moving.Content, stationary.Content);
                }
                // Keep allocation and list traversal identical for both paths;
                // accumulating all frames would measure fixture list growth.
                Graphics.Draws.Clear();
            }
            var ticks = Stopwatch.GetTimestamp() - start;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            return new Measurement
            {
                Ticks = ticks, Allocated = allocated, MovingSum = movingSum, StaticSum = staticSum,
                OrderedChecksum = orderedChecksum, Builds = visual.BuildCalls, EmptyBuilds = visual.EmptyBuildCalls,
                MovingUploads = visual.MovingMesh.Uploads, StaticUploads = visual.StaticMesh.Uploads,
                Draws = draws, SimCalls = flow.SimCalls, SimulationVersion = flow.SimulationVersion,
                AudioResets = visual.AudioResets, AudioCalls = visual.AudioCalls, InvalidFrames = invalidFrames,
                AnimationDelta = visual.AnimationTime - startedAnimation, AreaReads = GridVisibleArea.Reads
            };
        }

        private static bool ValidDraw(Graphics.DrawCall draw, float z, int layer)
        {
            return draw.Position.x == 0.5f && draw.Position.y == 0.5f && draw.Position.z == z - 0.1f
                && draw.Layer == layer && draw.Rotation.x == 0f && draw.Rotation.y == 0f
                && draw.Rotation.z == 0f && draw.Rotation.w == 1f;
        }

        private static long AppendChecksum(long checksum, int moving, int stationary)
        {
            // Moving + stationary alone is always -1 because the fixture stores
            // a bitwise complement. Retain separate sums and an ordered hash.
            return unchecked((checksum * 31 + moving) * 31 + stationary);
        }

        private static void Validate(Scenario scenario, Measurement value, bool candidate, bool steadyState)
        {
            var builds = scenario.ExpectedBuilds(candidate);
            var simulations = scenario.SimulationInterval == 0 ? 0
                : (Frames + scenario.SimulationInterval - 1) / scenario.SimulationInterval;
            var audio = scenario.Change == Changes.Audio ? Frames : 0;
            if (value.Ticks <= 0 || (steadyState && value.Allocated != 0) || value.InvalidFrames != 0
                || value.Builds != builds || value.EmptyBuilds != 0
                || value.MovingUploads != builds || value.StaticUploads != builds
                || value.Draws != Frames * 2 || value.SimCalls != simulations
                || value.SimulationVersion != simulations || value.AudioResets != audio || value.AudioCalls != audio
                || value.MovingSum != scenario.MovingSum || value.StaticSum != scenario.StaticSum
                || value.OrderedChecksum != scenario.OrderedChecksum
                || value.AnimationDelta != Frames * (double)(float)scenario.Step)
                throw new InvalidOperationException("Conduit benchmark behavior/allocation contract failed: "
                    + scenario.Name + " " + (candidate ? "candidate" : "baseline")
                    + "; inspect the preceding raw sample");
        }

        private static object Details(Measurement value)
        {
            return new
            {
                ticks = value.Ticks, elapsed_ms = value.Milliseconds, allocated_bytes = value.Allocated,
                builds = value.Builds, empty_builds = value.EmptyBuilds,
                moving_uploads = value.MovingUploads, static_uploads = value.StaticUploads,
                draws = value.Draws, simulation_calls = value.SimCalls, simulation_version = value.SimulationVersion,
                audio_resets = value.AudioResets, audio_calls = value.AudioCalls, invalid_frames = value.InvalidFrames,
                moving_payload_sum = value.MovingSum, static_payload_sum = value.StaticSum,
                ordered_checksum = value.OrderedChecksum, animation_delta = value.AnimationDelta,
                visible_area_reads = value.AreaReads
            };
        }

        private static void Configure(ConduitAnimationQuality quality)
        {
            CycleTrimPatchOptions.Configure(false, false, false, false, false, false, conduitAnimation: quality);
        }
    }
}
