#!/usr/bin/env python3
"""Verify the developer-only CycleTrim NavGrid workload probe stays observational."""

from pathlib import Path
import re
import sys

from analyze_cycletrim_navgrid_probe import (
    CANDIDATE_MIN_DIRTY_CELLS,
    CANDIDATE_MIN_LONG_RANGE,
    CANDIDATE_MIN_SHORT_RANGE,
    CaptureError,
    analyze_log,
)


ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/NavGridWorkloadProbePatch.cs"
CORE = ROOT / "mods/CycleTrim/Core/NavGridWorkloadProbe.cs"
ANALYZER = ROOT / "scripts/analyze_cycletrim_navgrid_probe.py"
GATE_BENCHMARK = (
    ROOT / "benchmarks/CycleTrim.BrainBenchmarks/NavGridAdaptiveGateBenchmark.cs"
)


def expect_capture_failure(
    text: str,
    expected: str,
    failures: list[str],
    after_calls: int | None = None,
) -> None:
    try:
        analyze_log(text, after_calls=after_calls)
    except CaptureError as error:
        if expected not in str(error):
            failures.append(
                f"capture analyzer rejected fixture for the wrong reason: {error}"
            )
        return
    failures.append(f"capture analyzer accepted invalid fixture: expected {expected!r}")


def read_benchmark_constant(source: str, name: str) -> int:
    match = re.search(rf"private\s+const\s+int\s+{name}\s*=\s*(\d+)\s*;", source)
    if match is None:
        raise ValueError(f"missing NavGrid gate benchmark constant: {name}")
    return int(match.group(1))


def main() -> int:
    patch = PATCH.read_text(encoding="utf-8")
    core = CORE.read_text(encoding="utf-8")
    analyzer = ANALYZER.read_text(encoding="utf-8")
    gate_benchmark = GATE_BENCHMARK.read_text(encoding="utf-8")
    failures = []

    required_patch_fragments = (
        '"CYCLETRIM_NAVGRID_PROBE"',
        '"CYCLETRIM_NAVGRID_PROBE_CAPTURE"',
        '"PeterHan.FastTrack.PathPatches.NavGrid_UpdateGraph_Patch"',
        'AccessTools.Method(typeof(NavGrid), "UpdateGraph", Type.EmptyTypes)',
        "Harmony.GetPatchInfo(targetMethod)",
        "List<int> ___DirtyCells",
        "__instance.updateRangeX",
        "__instance.updateRangeY",
        "UIScheduler.Instance",
        "ScheduleNextFrame(ReportName, ReportCallback)",
        "Probe.FormatSummary(maxBuckets: 12)",
        "Probe.FormatSummary(maxBuckets: HistogramBucketCapacity)",
        '"[CycleTrim][NavGridProbeCapture] "',
    )
    for fragment in required_patch_fragments:
        if fragment not in patch:
            failures.append(f"runtime probe contract missing: {fragment}")
    if "GameScheduler.Instance" in patch:
        failures.append(
            "NavGrid probe reporting must not use the paused GameClock-backed GameScheduler"
        )

    required_analyzer_fragments = (
        "RESOLVED_MARKER",
        "TARGET_MARKER",
        "FASTTRACK_MARKER",
        "CAPTURE_PREFIX",
        '"bucketCallTotal": bucket_calls',
        '"candidateGate": summarize_candidate_gate(buckets, calls)',
        '"eligibleCallsLowerBound": lower_bound',
        '"eligibleCallsUpperBound": upper_bound',
        '"ambiguousCalls": ambiguous',
        "subtract_capture",
        '"baselineCalls": baseline["calls"]',
        '"cumulativeCalls": current["calls"]',
        '"windowed": True',
    )
    for fragment in required_analyzer_fragments:
        if fragment not in analyzer:
            failures.append(f"capture analyzer contract missing: {fragment}")

    try:
        benchmark_gate = (
            read_benchmark_constant(gate_benchmark, "MinDirtyCells"),
            read_benchmark_constant(gate_benchmark, "MinShortRange"),
            read_benchmark_constant(gate_benchmark, "MinLongRange"),
        )
        analyzer_gate = (
            CANDIDATE_MIN_DIRTY_CELLS,
            CANDIDATE_MIN_SHORT_RANGE,
            CANDIDATE_MIN_LONG_RANGE,
        )
        if analyzer_gate != benchmark_gate:
            failures.append(
                "capture analyzer candidate gate drifted from NavGridAdaptiveGateBenchmark: "
                f"analyzer={analyzer_gate}, benchmark={benchmark_gate}"
            )
    except ValueError as error:
        failures.append(str(error))

    if not re.search(
        r"private\s+static\s+void\s+Prefix\s*\(",
        patch,
    ):
        failures.append("NavGrid workload probe Prefix must remain observational void")
    if "ref List<int> ___DirtyCells" in patch or "out List<int> ___DirtyCells" in patch:
        failures.append("NavGrid workload probe must not replace the DirtyCells list")
    if "return false;" not in patch:
        failures.append("probe opt-in Prepare guard is missing")
    if "GetEnvironmentVariable(environmentVariable)" not in patch:
        failures.append("probe no longer has an explicit opt-in environment guard")
    if "UnityEngine" in core or "Harmony" in core or "NavGrid" not in core:
        failures.append("aggregate collector must remain independent of Unity/Harmony runtime APIs")
    record_body = core.split("internal void Record", 1)[1].split(
        "internal long GetBucketCount", 1
    )[0]
    if "new " in record_body:
        failures.append("Record hot path must not allocate objects")

    valid_capture = "\n".join(
        (
            "prefix " +
            "[CycleTrim][NavGridProbe] requested; NavGrid.UpdateGraph() resolved. " +
            "Valid evidence requires a later 'target reached' message with calls > 0.",
            "prefix [CycleTrim][NavGridProbe] target reached; aggregate sampling started.",
            "prefix [CycleTrim][NavGridProbeCapture] " +
            "calls=3, empty=0, avgDirty=8.00, avgSeedBBox=20.00, " +
            "nonzeroBuckets=2, top=[dirty=8-15,rx=2,ry=4,density=<=1/4:2; " +
            "dirty=8-15,rx=4,ry=2,density=>1/2:1]",
        )
    )
    try:
        parsed = analyze_log(valid_capture)
        if parsed["calls"] != 3 or parsed["bucketCallTotal"] != 3:
            failures.append("capture analyzer did not preserve complete bucket totals")
        if len(parsed["buckets"]) != 2:
            failures.append("capture analyzer did not preserve all nonzero buckets")
        gate = parsed["candidateGate"]
        if gate["eligibleCallsLowerBound"] != 0 or gate["eligibleCallsUpperBound"] != 0:
            failures.append("sub-threshold capture must have zero candidate-gate coverage")
    except CaptureError as error:
        failures.append(f"capture analyzer rejected valid fixture: {error}")

    ambiguous_capture = valid_capture.replace(
        "calls=3, empty=0, avgDirty=8.00, avgSeedBBox=20.00, "
        "nonzeroBuckets=2, top=[dirty=8-15,rx=2,ry=4,density=<=1/4:2; "
        "dirty=8-15,rx=4,ry=2,density=>1/2:1]",
        "calls=4, empty=0, avgDirty=21.00, avgSeedBBox=20.00, "
        "nonzeroBuckets=2, top=[dirty=16-23,rx=2,ry=4,density=<=1/4:3; "
        "dirty=24-31,rx=2,ry=4,density=>1/2:1]",
    )
    try:
        parsed = analyze_log(ambiguous_capture)
        gate = parsed["candidateGate"]
        if gate["eligibleCallsLowerBound"] != 1:
            failures.append("candidate-gate lower bound must count only guaranteed buckets")
        if gate["eligibleCallsUpperBound"] != 4:
            failures.append("candidate-gate upper bound must include threshold-crossing buckets")
        if gate["ambiguousCalls"] != 3:
            failures.append("candidate-gate ambiguity must expose coarse 16-23 bucket calls")
    except CaptureError as error:
        failures.append(f"capture analyzer rejected ambiguous valid fixture: {error}")

    window_capture = "\n".join(
        (
            "prefix " +
            "[CycleTrim][NavGridProbe] requested; NavGrid.UpdateGraph() resolved. " +
            "Valid evidence requires a later 'target reached' message with calls > 0.",
            "prefix [CycleTrim][NavGridProbe] target reached; aggregate sampling started.",
            "prefix [CycleTrim][NavGridProbeCapture] " +
            "calls=2, empty=0, nonzeroBuckets=1, " +
            "top=[dirty=8-15,rx=2,ry=4,density=<=1/4:2]",
            "prefix [CycleTrim][NavGridProbeCapture] " +
            "calls=5, empty=0, nonzeroBuckets=2, " +
            "top=[dirty=8-15,rx=2,ry=4,density=<=1/4:3; " +
            "dirty=24-31,rx=2,ry=4,density=>1/2:2]",
        )
    )
    try:
        parsed = analyze_log(window_capture, after_calls=2)
        if not parsed.get("windowed"):
            failures.append("anchored capture did not identify itself as a workload window")
        if parsed["baselineCalls"] != 2 or parsed["cumulativeCalls"] != 5:
            failures.append("anchored capture lost cumulative baseline/final calls")
        if parsed["calls"] != 3 or parsed["bucketCallTotal"] != 3:
            failures.append("anchored capture did not subtract baseline calls/buckets")
        if len(parsed["buckets"]) != 2:
            failures.append("anchored capture should preserve both fresh workload buckets")
        gate = parsed["candidateGate"]
        if gate["eligibleCallsLowerBound"] != 2 or gate["eligibleCallsUpperBound"] != 2:
            failures.append("anchored candidate-gate coverage must use fresh bucket deltas only")
    except CaptureError as error:
        failures.append(f"capture analyzer rejected valid anchored window: {error}")

    regressed_bucket_capture = (
        window_capture
        .replace(
            "dirty=8-15,rx=2,ry=4,density=<=1/4:3; ",
            "dirty=8-15,rx=2,ry=4,density=<=1/4:1; ",
        )
        .replace(
            "dirty=24-31,rx=2,ry=4,density=>1/2:2]",
            "dirty=24-31,rx=2,ry=4,density=>1/2:4]",
        )
    )
    expect_capture_failure(
        regressed_bucket_capture,
        "bucket count moved backwards",
        failures,
        after_calls=2,
    )

    truncated_capture = valid_capture.replace(
        "; dirty=8-15,rx=4,ry=2,density=>1/2:1", ""
    )
    expect_capture_failure(truncated_capture, "truncated", failures)

    fasttrack_capture = valid_capture.replace(
        "prefix [CycleTrim][NavGridProbe] target reached; aggregate sampling started.",
        "prefix [CycleTrim][NavGridProbe] target reached but sampling is disabled: " +
        "FastTrack's NavGrid.UpdateGraph replacement is active.",
    )
    expect_capture_failure(fasttrack_capture, "FastTrack", failures)

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print(
        "PASS CycleTrim NavGrid workload probe remains opt-in, observational, pause-safe, "
        "and anchored complete captures are analyzed as exact histogram deltas"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
