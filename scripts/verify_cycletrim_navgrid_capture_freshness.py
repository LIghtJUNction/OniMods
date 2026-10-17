#!/usr/bin/env python3
"""Verify NavGrid workload captures cannot silently reuse stale deferred reports."""

from pathlib import Path
import sys

from analyze_cycletrim_navgrid_probe import CaptureError, analyze_log


ROOT = Path(__file__).resolve().parents[1]
ANALYZER = ROOT / "scripts/analyze_cycletrim_navgrid_probe.py"
RESOLVED = (
    "[CycleTrim][NavGridProbe] requested; NavGrid.UpdateGraph() resolved. "
    "Valid evidence requires a later 'target reached' message with calls > 0."
)
TARGET = "[CycleTrim][NavGridProbe] target reached; aggregate sampling started."
CAPTURE = "[CycleTrim][NavGridProbeCapture] "


def capture(calls: int) -> str:
    return (
        CAPTURE
        + f"calls={calls}, empty=0, avgDirty=24.00, avgSeedBBox=16.00, "
        + "nonzeroBuckets=1, top=[dirty=24-31,rx=2,ry=4,density=>1/2:"
        + f"{calls}]"
    )


def expect_failure(
    text: str,
    after_calls: int,
    expected: str,
    failures: list[str],
) -> None:
    try:
        analyze_log(text, after_calls=after_calls)
    except CaptureError as error:
        if expected not in str(error):
            failures.append(f"wrong failure for after_calls={after_calls}: {error}")
        return
    failures.append(
        f"stale/invalid capture unexpectedly passed for after_calls={after_calls}"
    )


def main() -> int:
    failures = []
    analyzer = ANALYZER.read_text(encoding="utf-8")
    for fragment in (
        "def select_capture(",
        "def subtract_capture(",
        '"--after-calls"',
        "baseline calls=",
        "no fresh NavGrid capture was emitted",
        "deferred UIScheduler report may not have flushed yet",
        '"baselineCalls": baseline["calls"]',
        '"cumulativeCalls": current["calls"]',
    ):
        if fragment not in analyzer:
            failures.append(f"NavGrid capture freshness contract missing: {fragment}")

    fresh_log = "\n".join((RESOLVED, TARGET, capture(64), capture(256)))
    try:
        parsed = analyze_log(fresh_log, after_calls=64)
        if parsed["calls"] != 192 or parsed["bucketCallTotal"] != 192:
            failures.append("fresh capture selector did not isolate post-baseline calls")
        if parsed["baselineCalls"] != 64 or parsed["cumulativeCalls"] != 256:
            failures.append("fresh capture selector lost cumulative window endpoints")
        if not parsed.get("windowed"):
            failures.append("fresh capture selector did not mark an anchored workload window")
        if parsed["captureLines"] != 2:
            failures.append("fresh capture selector lost capture-line accounting")
    except CaptureError as error:
        failures.append(f"fresh post-baseline capture was rejected: {error}")

    expect_failure(
        "\n".join((RESOLVED, TARGET, capture(64))),
        64,
        "no fresh NavGrid capture",
        failures,
    )
    expect_failure(
        fresh_log,
        63,
        "baseline calls=63 was not found",
        failures,
    )
    expect_failure(
        fresh_log,
        0,
        "positive capture call count",
        failures,
    )

    restarted_log = "\n".join(
        (
            RESOLVED,
            TARGET,
            capture(64),
            RESOLVED,
            TARGET,
            capture(1),
        )
    )
    expect_failure(
        restarted_log,
        64,
        "baseline calls=64 was not found",
        failures,
    )

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    print(
        "PASS CycleTrim NavGrid capture freshness requires a same-run baseline, "
        "a newer deferred report, and an exact post-baseline histogram delta"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
