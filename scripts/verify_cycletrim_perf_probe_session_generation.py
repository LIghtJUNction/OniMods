#!/usr/bin/env python3
"""Verify that CycleTrim performance-probe counters are isolated per game capture."""

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "mods/CycleTrim/Patches/PerformanceProbePatch.cs"

COUNTERS = (
    "asyncTickCounter",
    "asyncWorkCounter",
    "navigatorProbeCounter",
    "fetchCounter",
    "choreCounter",
    "brainSchedulerCounter",
    "roomProberCounter",
)
MAIN_COUNTERS = tuple(name for name in COUNTERS if name != "asyncWorkCounter")


def main() -> int:
    patch = PATCH.read_text(encoding="utf-8")
    failures: list[str] = []

    for name in COUNTERS:
        if f"private static PerformanceProbeGenerationCounter {name};" not in patch:
            failures.append(f"{name} is not generation-owned")
        if f"{name}.AdvanceGeneration();" not in patch:
            failures.append(f"{name} is not rotated at a game boundary")
        if f"{name}.SnapshotCurrent()" not in patch:
            failures.append(f"{name} reporting is not scoped to the current generation")

    for name in MAIN_COUNTERS:
        if f"BeginMainTiming({name})" not in patch:
            failures.append(f"{name} Prefix does not capture current generation ownership")

    if "startedNewCapture ? default(PerformanceProbeSnapshot) : lastAsyncTickSnapshot" not in patch:
        failures.append("first report discards fresh current-generation main-thread calls")

    for failure in failures:
        print(f"FAIL: {failure}", file=sys.stderr)
    if failures:
        return 1

    print("PASS CycleTrim performance probe game-session generation wiring")
    return 0


if __name__ == "__main__":
    sys.exit(main())
