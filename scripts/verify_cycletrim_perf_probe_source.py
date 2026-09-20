#!/usr/bin/env python3
"""Guard CycleTrim's opt-in performance probe against accidental hot-path regressions."""

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/PerformanceProbePatch.cs"
CORE = ROOT / "mods/CycleTrim/Core/PerformanceProbeCounter.cs"
GENERATION_CORE = ROOT / "mods/CycleTrim/Core/PerformanceProbeGenerationCounter.cs"
ANALYZER = ROOT / "scripts/analyze_cycletrim_perf_probe.py"


def require(text: str, needle: str, message: str, failures: list[str]) -> None:
    if needle not in text:
        failures.append(message)


def main() -> int:
    patch = PATCH.read_text(encoding="utf-8")
    core = CORE.read_text(encoding="utf-8")
    generation_core = GENERATION_CORE.read_text(encoding="utf-8")
    analyzer = ANALYZER.read_text(encoding="utf-8")
    failures = []

    require(patch, '"CYCLETRIM_PERF_PROBE"', "opt-in environment switch missing", failures)
    if patch.count("if (!IsRequested())") != 7:
        failures.append("all seven Harmony probe targets must fail closed when the probe is disabled")
    require(patch, "Stopwatch.GetTimestamp()", "wall-clock Stopwatch timing missing", failures)
    require(
        patch,
        "RecordWorker(__state.Counter, __state.StartedAt)",
        "worker path is not separated from main-thread timing",
        failures,
    )
    require(
        patch,
        "asyncWorkCounter = new PerformanceProbeGenerationCounter()",
        "worker counter does not own capture generations",
        failures,
    )
    require(
        patch,
        "asyncWorkCounter.AdvanceGeneration()",
        "game boundary does not rotate the worker timing generation",
        failures,
    )
    require(
        patch,
        "asyncWorkCounter.CaptureCurrent()",
        "worker Prefix does not capture generation ownership at work start",
        failures,
    )
    require(
        patch,
        "asyncWorkCounter.SnapshotCurrent()",
        "reporting does not snapshot the current worker generation",
        failures,
    )
    require(patch, "RecordMain(navigatorProbeCounter", "navigator probe timing is not recorded on the main-thread path", failures)
    require(patch, "RecordMain(brainSchedulerCounter", "brain scheduler timing is not recorded on the main-thread path", failures)
    require(patch, "RecordMain(roomProberCounter", "room prober timing is not recorded on the main-thread path", failures)
    require(patch, '"Navigator.UpdateProbe"', "navigator probe target is missing from reports", failures)
    require(patch, '"BrainScheduler.RenderEveryTick"', "brain scheduler target is missing from reports", failures)
    require(patch, '"RoomProber.Sim1000ms"', "room prober target is missing from reports", failures)
    require(patch, '"worker"', "worker thread context is missing from reports", failures)
    require(patch, "GC.CollectionCount(2)", "Gen2 observational telemetry missing", failures)
    require(patch, "GC.GetTotalMemory(false)", "heap snapshot telemetry missing", failures)
    require(patch, "Harmony.GetPatchInfo(target)", "Harmony ownership inspection missing", failures)
    require(patch, "FastTrackNamespacePrefix", "FastTrack attribution missing", failures)
    require(patch, "UIScheduler.Instance", "deferred reporting must use the unscaled UI scheduler", failures)
    require(
        patch,
        "scheduler.ScheduleNextFrame(ReportName, reportCallback, scheduler)",
        "deferred report does not retain scheduler ownership for stale-callback isolation",
        failures,
    )
    require(
        patch,
        "!ReferenceEquals(reportScheduler.Target, scheduler)",
        "scheduler replacement cannot recover a stranded deferred report",
        failures,
    )
    require(
        patch,
        "!ReferenceEquals(reportScheduler.Target, scheduledOn)",
        "stale scheduler callbacks can mutate a newer report owner",
        failures,
    )
    require(
        patch,
        "captureGame = new WeakReference(null)",
        "probe capture does not track game-session identity without retaining the old game",
        failures,
    )
    require(
        patch,
        "private static long BeginMainTiming()",
        "main-thread boundary-aware timing helper missing",
        failures,
    )
    require(patch, "ObserveGameBoundary();", "main-thread observations do not detect a new game session", failures)
    require(patch, "captureBoundaryPending = true", "new game sessions do not mark a rebased report boundary", failures)
    require(patch, "reportObservationCount = 0", "new game sessions inherit the prior report cadence", failures)
    require(patch, "nextReportAt = 1", "new game sessions are not guaranteed a fresh first report", failures)
    require(
        patch,
        'summary.Append(",\\\"captureGeneration\\\":").Append(captureGeneration)',
        "probe report does not expose its game-session generation",
        failures,
    )
    require(
        patch,
        "startedNewCapture ? asyncTickSnapshot : lastAsyncTickSnapshot",
        "first report in a game session is not rebased away from prior-session timing",
        failures,
    )
    if patch.count("__state = BeginMainTiming();") != 6:
        failures.append("all six main-thread probes must rotate the game boundary before target execution")
    if "private static long BeginMainTiming()" in patch:
        begin_main = patch.split("private static long BeginMainTiming()", 1)[1].split(
            "private static void ObserveGameBoundary()", 1
        )[0]
        if "ObserveGameBoundary();" not in begin_main or "return BeginTiming();" not in begin_main:
            failures.append("boundary-aware timing helper must observe the game boundary then start timing")
        elif begin_main.find("ObserveGameBoundary();") > begin_main.find("return BeginTiming();"):
            failures.append("game boundary must rotate before the main target timing starts")
    record_main = patch.split("private static void RecordMain", 1)[1].split(
        "private static void RecordWorker", 1
    )[0]
    if "ObserveGameBoundary();" in record_main:
        failures.append("game boundary must rotate before main target execution, not from RecordMain")
    if "GameScheduler.Instance" in patch:
        failures.append("deferred performance reporting must not depend on the paused game clock")
    require(patch, "intervalDurationTicks", "report interval duration is missing", failures)
    require(patch, "intervalCalls", "per-report call deltas are missing", failures)
    require(patch, "intervalTotalTicks", "per-report timing deltas are missing", failures)
    require(patch, "DeltaSince(previousSnapshot)", "reporting does not derive interval metrics from cumulative snapshots", failures)
    if "GC.Collect(" in patch:
        failures.append("performance probe must never trigger GC")
    if patch.count("UnityEngine.Debug.Log") != 3:
        failures.append("logging must remain limited to target resolution and deferred reporting")

    for needle, message in (
        ("PerformanceProbeCounter(bool consistentSnapshots = false)", "counter snapshot-coordination mode is missing"),
        ("lock (snapshotLock)", "coordinated counter does not group record/snapshot state"),
        ("Interlocked.Increment(ref callCount)", "counter call count is not atomic"),
        ("Interlocked.Add(ref totalTicks", "counter total is not atomic"),
        ("Interlocked.CompareExchange(", "counter maximum is not atomic"),
        ("DeltaSince(PerformanceProbeSnapshot previous)", "counter snapshots cannot derive reporting intervals"),
    ):
        require(core, needle, message, failures)
    if "new " in core.split("internal void Record(long elapsedTicks)", 1)[1].split(
        "internal PerformanceProbeSnapshot Snapshot()", 1
    )[0]:
        failures.append("recording path must not allocate managed objects")

    for needle, message in (
        ("Volatile.Read(ref current)", "worker generation capture is not an atomic published read"),
        ("Interlocked.Exchange(ref current, CreateCounter())", "worker generation rotation is not atomic"),
        (
            "new PerformanceProbeCounter(consistentSnapshots: true)",
            "worker generation counters lost coordinated snapshots",
        ),
    ):
        require(generation_core, needle, message, failures)
    if generation_core.count("consistentSnapshots: true") != 1:
        failures.append("snapshot coordination must remain isolated to worker generation counter creation")
    capture_current = generation_core.split(
        "internal PerformanceProbeCounter CaptureCurrent()", 1
    )[1].split("internal PerformanceProbeSnapshot SnapshotCurrent()", 1)[0]
    if "new " in capture_current:
        failures.append("worker generation capture must not allocate managed objects")

    for needle, message in (
        ("calls <= 0", "analyzer does not fail zero-call captures"),
        ('target.get("resolved") is not True', "analyzer does not fail unresolved targets"),
        ("fastTrackPatched", "analyzer does not surface FastTrack ownership"),
        ('"Navigator.UpdateProbe"', "analyzer does not require navigator probe evidence"),
        ('"BrainScheduler.RenderEveryTick"', "analyzer does not require brain scheduler evidence"),
        ('"RoomProber.Sim1000ms"', "analyzer does not require room prober evidence"),
        ('"--series"', "analyzer cannot validate an in-run report series"),
        ('"--reject-fasttrack"', "analyzer cannot fail closed on FastTrack-owned baseline captures"),
        ("fastTrackPatched changed within one capture", "analyzer does not reject mid-capture FastTrack ownership drift"),
        ("captureGeneration changed within one capture", "analyzer does not reject cross-session report series"),
        ("captureGeneration schema changed within one capture", "analyzer accepts mixed legacy/session-aware series"),
        ("intervalCalls does not match cumulative delta", "analyzer does not close interval call arithmetic"),
        ("intervalTotalTicks does not match cumulative delta", "analyzer does not close interval timing arithmetic"),
        (
            "has no fresh calls after the selected baseline",
            "workload-anchored analyzer can reuse only pre-baseline cumulative calls",
        ),
        (
            "require_fresh_calls=args.after_sequence is not None",
            "--after-sequence does not enable the fresh-workload call gate",
        ),
    ):
        require(analyzer, needle, message, failures)

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print("PASS CycleTrim opt-in performance probe source contract")
    return 0


if __name__ == "__main__":
    sys.exit(main())
