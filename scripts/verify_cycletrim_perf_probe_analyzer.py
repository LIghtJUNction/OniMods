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

    legacy = copy.deepcopy(second)
    legacy.pop("reportSequence")
    legacy.pop("intervalDurationTicks")
    for target in legacy["targets"]:
        target.pop("intervalCalls")
        target.pop("intervalTotalTicks")
        target.pop("intervalMeanTicks")
    failures = analyzer.validate(legacy, required)
    require(not failures, "legacy latest-report validation should remain compatible")

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

    print("PASS CycleTrim performance probe analyzer regressions")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
