#!/usr/bin/env python3
"""Validate the latest CycleTrim opt-in performance probe report in an ONI log."""

import argparse
import json
import sys
from pathlib import Path

MARKER = "[CycleTrim][PerfProbe] {"
DEFAULT_REQUIRED = (
    "AsyncPathProber.Manager.TickFrame",
    "AsyncPathProber.WorkOrder.Execute",
    "FetchManager.FetchablesByPrefabId.UpdatePickups",
    "ChoreConsumer.FindNextChore",
    "BrainScheduler.RenderEveryTick",
)


def latest_report(text: str) -> dict:
    reports = []
    for line in text.splitlines():
        marker = line.find(MARKER)
        if marker < 0:
            continue
        payload = line[marker + len("[CycleTrim][PerfProbe] ") :].strip()
        try:
            reports.append(json.loads(payload))
        except json.JSONDecodeError as error:
            raise ValueError(f"invalid probe JSON: {error}") from error
    if not reports:
        raise ValueError("no CycleTrim performance probe JSON report found")
    return reports[-1]


def validate(report: dict, required: tuple[str, ...]) -> list[str]:
    failures = []
    if not isinstance(report.get("stopwatchFrequency"), int) or report["stopwatchFrequency"] <= 0:
        failures.append("stopwatchFrequency must be a positive integer")

    gc = report.get("gc")
    if not isinstance(gc, dict):
        failures.append("gc object is missing")
    else:
        for name in ("gen0Delta", "gen1Delta", "gen2Delta", "heapBytes", "heapDeltaBytes"):
            if not isinstance(gc.get(name), int):
                failures.append(f"gc.{name} must be an integer")

    targets = report.get("targets")
    if not isinstance(targets, list):
        failures.append("targets array is missing")
        return failures

    by_name = {}
    for target in targets:
        if not isinstance(target, dict) or not isinstance(target.get("name"), str):
            failures.append("every target must have a string name")
            continue
        name = target["name"]
        if name in by_name:
            failures.append(f"duplicate target: {name}")
        by_name[name] = target

    for name in required:
        target = by_name.get(name)
        if target is None:
            failures.append(f"required target missing: {name}")
            continue
        if target.get("resolved") is not True:
            failures.append(f"required target did not resolve: {name}")
        calls = target.get("calls")
        if not isinstance(calls, int) or calls <= 0:
            failures.append(f"required target has no calls: {name}")
        for metric in ("totalTicks", "maxTicks"):
            if not isinstance(target.get(metric), int) or target[metric] < 0:
                failures.append(f"{name} {metric} must be a non-negative integer")
        mean_ticks = target.get("meanTicks")
        if not isinstance(mean_ticks, (int, float)) or mean_ticks < 0:
            failures.append(f"{name} meanTicks must be non-negative")

    return failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", type=Path, help="Player.log or another ONI log containing probe output")
    parser.add_argument(
        "--require",
        action="append",
        dest="required",
        help="target name that must resolve and have calls > 0; repeatable",
    )
    args = parser.parse_args()

    if not args.log.is_file():
        parser.error(f"log not found: {args.log}")

    try:
        report = latest_report(args.log.read_text(encoding="utf-8", errors="replace"))
    except ValueError as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    required = tuple(args.required) if args.required else DEFAULT_REQUIRED
    failures = validate(report, required)
    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    for target in report["targets"]:
        if target.get("fastTrackPatched"):
            print(f"INFO: FastTrack also patches {target['name']}")
    print("PASS CycleTrim performance probe capture is structurally valid")
    return 0


if __name__ == "__main__":
    sys.exit(main())
