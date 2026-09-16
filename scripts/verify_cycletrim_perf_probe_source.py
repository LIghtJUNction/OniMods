#!/usr/bin/env python3
"""Guard CycleTrim's opt-in performance probe against accidental hot-path regressions."""

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/PerformanceProbePatch.cs"
CORE = ROOT / "mods/CycleTrim/Core/PerformanceProbeCounter.cs"
ANALYZER = ROOT / "scripts/analyze_cycletrim_perf_probe.py"


def require(text: str, needle: str, message: str, failures: list[str]) -> None:
    if needle not in text:
        failures.append(message)


def main() -> int:
    patch = PATCH.read_text(encoding="utf-8")
    core = CORE.read_text(encoding="utf-8")
    analyzer = ANALYZER.read_text(encoding="utf-8")
    failures = []

    require(patch, '"CYCLETRIM_PERF_PROBE"', "opt-in environment switch missing", failures)
    if patch.count("if (!IsRequested())") != 6:
        failures.append("all six Harmony probe targets must fail closed when the probe is disabled")
    require(patch, "Stopwatch.GetTimestamp()", "wall-clock Stopwatch timing missing", failures)
    require(patch, "RecordWorker(asyncWorkCounter", "worker path is not separated from main-thread timing", failures)
    require(patch, "RecordMain(brainSchedulerCounter", "brain scheduler timing is not recorded on the main-thread path", failures)
    require(patch, "RecordMain(roomProberCounter", "room prober timing is not recorded on the main-thread path", failures)
    require(patch, '"BrainScheduler.RenderEveryTick"', "brain scheduler target is missing from reports", failures)
    require(patch, '"RoomProber.Sim1000ms"', "room prober target is missing from reports", failures)
    require(patch, '"worker"', "worker thread context is missing from reports", failures)
    require(patch, "GC.CollectionCount(2)", "Gen2 observational telemetry missing", failures)
    require(patch, "GC.GetTotalMemory(false)", "heap snapshot telemetry missing", failures)
    require(patch, "Harmony.GetPatchInfo(target)", "Harmony ownership inspection missing", failures)
    require(patch, "FastTrackNamespacePrefix", "FastTrack attribution missing", failures)
    require(patch, "GameScheduler.Instance", "deferred reporting path missing", failures)
    require(patch, "intervalDurationTicks", "report interval duration is missing", failures)
    require(patch, "intervalCalls", "per-report call deltas are missing", failures)
    require(patch, "intervalTotalTicks", "per-report timing deltas are missing", failures)
    require(patch, "DeltaSince(previousSnapshot)", "reporting does not derive interval metrics from cumulative snapshots", failures)
    if "GC.Collect(" in patch:
        failures.append("performance probe must never trigger GC")
    if patch.count("UnityEngine.Debug.Log") != 3:
        failures.append("logging must remain limited to target resolution and deferred reporting")

    for needle, message in (
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
        ("calls <= 0", "analyzer does not fail zero-call captures"),
        ('target.get("resolved") is not True', "analyzer does not fail unresolved targets"),
        ("fastTrackPatched", "analyzer does not surface FastTrack ownership"),
        ('"BrainScheduler.RenderEveryTick"', "analyzer does not require brain scheduler evidence"),
        ('"RoomProber.Sim1000ms"', "analyzer does not require room prober evidence"),
        ('"--series"', "analyzer cannot validate an in-run report series"),
        ('"--reject-fasttrack"', "analyzer cannot fail closed on FastTrack-owned baseline captures"),
        ("fastTrackPatched changed within one capture", "analyzer does not reject mid-capture FastTrack ownership drift"),
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
