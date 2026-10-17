#!/usr/bin/env python3
"""Regression-test CycleTrim performance-probe interval analysis without ONI."""

import copy
import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ANALYZER = ROOT / "scripts/analyze_cycletrim_perf_probe.py"


def load_analyzer():
    spec = importlib.util.spec_from_file_location("cycletrim_perf_probe_analyzer", ANALYZER)
    if spec is None or spec.loader is None:
        raise RuntimeError("unable to load CycleTrim performance probe analyzer")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def make_target(name: str, calls: int, total: int, interval_calls: int, interval_total: int) -> dict:
    return {
        "name": name,
        "thread": "worker" if "WorkOrder" in name else "main",
        "resolved": True,
        "fastTrackPatched": False,
        "calls": calls,
        "totalTicks": total,
        "meanTicks": 0.0 if calls == 0 else total / calls,
        "maxTicks": total,
        "intervalCalls": interval_calls,
        "intervalTotalTicks": interval_total,
        "intervalMeanTicks": 0.0 if interval_calls == 0 else interval_total / interval_calls,
    }


def make_report(analyzer, sequence: int, calls: int, total: int, interval_calls: int, interval_total: int) -> dict:
    return {
        "stopwatchFrequency": 10_000_000,
        "captureGeneration": 1,
        "reportSequence": sequence,
        "intervalDurationTicks": 2_000_000,
        "gc": {
            "gen0Delta": 0,
            "gen1Delta": 0,
            "gen2Delta": 0,
            "heapBytes": 123456,
            "heapDeltaBytes": 0,
        },
        "targets": [
            make_target(name, calls, total, interval_calls, interval_total)
            for name in analyzer.DEFAULT_REQUIRED
        ],
    }


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def require_value_error(callback, expected: str) -> None:
    try:
        callback()
    except ValueError as error:
        require(expected in str(error), f"unexpected ValueError: {error}")
        return
    raise RuntimeError("expected ValueError was not raised")


def main() -> int:
    analyzer = load_analyzer()
    required = analyzer.DEFAULT_REQUIRED

    first = make_report(analyzer, 1, 0, 0, 0, 0)
    second = make_report(analyzer, 2, 3, 45, 3, 45)
    failures = analyzer.validate_series([first, second], required)
    require(
        not failures,
        "valid interval series with early zero-call targets rejected: " + "; ".join(failures),
    )

    boundary_first = make_report(analyzer, 1, 3, 45, 0, 0)
    boundary_second = make_report(analyzer, 2, 5, 85, 2, 40)
    failures = analyzer.validate_series([boundary_first, boundary_second], required)
    require(
        not failures,
        "generation-aware first report must be usable as a rebased session boundary: "
        + "; ".join(failures),
    )

    legacy = copy.deepcopy(second)
    legacy.pop("captureGeneration")
    legacy.pop("reportSequence")
    legacy.pop("intervalDurationTicks")
    for target in legacy["targets"]:
        target.pop("intervalCalls")
        target.pop("intervalTotalTicks")
        target.pop("intervalMeanTicks")
    failures = analyzer.validate(legacy, required)
    require(not failures, "legacy latest-report validation should remain compatible")

    fasttrack = copy.deepcopy(second)
    fasttrack["targets"][-1]["fastTrackPatched"] = True
    failures = analyzer.validate(fasttrack, required)
    require(
        not failures,
        "default validation should keep FastTrack ownership informational",
    )
    failures = analyzer.validate(fasttrack, required, reject_fasttrack=True)
    require(
        any("is patched by FastTrack" in failure for failure in failures),
        "vanilla-baseline validation did not reject a FastTrack-owned target",
    )

    malformed_ownership = copy.deepcopy(second)
    malformed_ownership["targets"][0]["fastTrackPatched"] = "yes"
    failures = analyzer.validate(malformed_ownership, required)
    require(
        any("fastTrackPatched must be a boolean" in failure for failure in failures),
        "analyzer accepted malformed FastTrack ownership state",
    )

    ownership_drift = copy.deepcopy(second)
    ownership_drift["targets"][0]["fastTrackPatched"] = True
    failures = analyzer.validate_series([first, ownership_drift], required)
    require(
        any("fastTrackPatched changed within one capture" in failure for failure in failures),
        "series validator did not reject mid-capture FastTrack ownership drift",
    )

    cross_generation = copy.deepcopy(second)
    cross_generation["captureGeneration"] = 2
    failures = analyzer.validate_series([first, cross_generation], required)
    require(
        any("captureGeneration changed within one capture" in failure for failure in failures),
        "series validator accepted reports from different game-session generations",
    )

    broken_delta = copy.deepcopy(second)
    broken_delta["targets"][0]["intervalCalls"] = 2
    failures = analyzer.validate_series([first, broken_delta], required)
    require(
        any("intervalCalls does not match cumulative delta" in failure for failure in failures),
        "series validator did not reject mismatched cumulative/interval call arithmetic",
    )

    broken_sequence = copy.deepcopy(second)
    broken_sequence["reportSequence"] = 4
    failures = analyzer.validate_series([first, broken_sequence], required)
    require(
        any("reportSequence must follow" in failure for failure in failures),
        "series validator did not reject a missing report in the sequence",
    )

    failures = analyzer.validate_series([first], required)
    require(
        failures == ["series validation requires at least two probe reports"],
        "series validator must fail closed on a single report",
    )

    selected = analyzer.select_series_after_sequence([first, second], 1)
    require(
        selected == [first, second],
        "freshness selector did not retain the baseline plus fresh report",
    )
    failures = analyzer.validate_series(selected, required, require_fresh_calls=True)
    require(not failures, "freshness-selected series should require and accept fresh target calls")

    stagnant = make_report(analyzer, 3, 3, 45, 0, 0)
    failures = analyzer.validate_series([second, stagnant], required)
    require(
        not failures,
        "structurally valid idle series should remain valid without a workload freshness gate",
    )
    failures = analyzer.validate_series(
        [second, stagnant],
        required,
        require_fresh_calls=True,
    )
    require(
        any("has no fresh calls after the selected baseline" in failure for failure in failures),
        "workload-anchored series accepted cumulative-only calls from before the baseline",
    )

    third = make_report(analyzer, 3, 5, 85, 2, 40)
    selected = analyzer.select_series_after_sequence([first, second, third], 2)
    require(
        selected == [second, third],
        "freshness selector did not anchor at the requested reportSequence",
    )
    failures = analyzer.validate_series(selected, required, require_fresh_calls=True)
    require(not failures, "anchored freshness series should validate fresh target calls")

    one_target_stale = copy.deepcopy(third)
    one_target_stale["targets"][0] = make_target(required[0], 3, 45, 0, 0)
    failures = analyzer.validate_series(
        [second, one_target_stale],
        required,
        require_fresh_calls=True,
    )
    require(
        any(required[0] in failure and "has no fresh calls" in failure for failure in failures),
        "workload freshness gate did not identify the required target that stayed stale",
    )

    require_value_error(
        lambda: analyzer.select_series_after_sequence([first], 1),
        "no fresh probe report after reportSequence 1",
    )
    require_value_error(
        lambda: analyzer.select_series_after_sequence([second], 1),
        "baseline reportSequence 1 was not found",
    )
    require_value_error(
        lambda: analyzer.select_series_after_sequence([first, second], 0),
        "after_sequence must be a positive reportSequence",
    )

    restarted_first = copy.deepcopy(first)
    require_value_error(
        lambda: analyzer.select_series_after_sequence([first, second, restarted_first], 1),
        "no fresh probe report after reportSequence 1",
    )

    print("PASS CycleTrim performance probe analyzer regressions")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
