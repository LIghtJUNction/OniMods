#!/usr/bin/env python3
"""Validate CycleTrim opt-in performance probe reports in an ONI log."""

import argparse
import json
import math
import sys
from pathlib import Path

MARKER = "[CycleTrim][PerfProbe] {"
DEFAULT_REQUIRED = (
    "AsyncPathProber.Manager.TickFrame",
    "AsyncPathProber.WorkOrder.Execute",
    "FetchManager.FetchablesByPrefabId.UpdatePickups",
    "ChoreConsumer.FindNextChore",
    "BrainScheduler.RenderEveryTick",
    "RoomProber.Sim1000ms",
)


def reports(text: str) -> list[dict]:
    parsed = []
    for line in text.splitlines():
        marker = line.find(MARKER)
        if marker < 0:
            continue
        payload = line[marker + len("[CycleTrim][PerfProbe] ") :].strip()
        try:
            parsed.append(json.loads(payload))
        except json.JSONDecodeError as error:
            raise ValueError(f"invalid probe JSON: {error}") from error
    if not parsed:
        raise ValueError("no CycleTrim performance probe JSON report found")
    return parsed


def latest_report(text: str) -> dict:
    return reports(text)[-1]


def _targets_by_name(report: dict) -> tuple[dict[str, dict], list[str]]:
    failures = []
    targets = report.get("targets")
    if not isinstance(targets, list):
        return {}, ["targets array is missing"]

    by_name = {}
    for target in targets:
        if not isinstance(target, dict) or not isinstance(target.get("name"), str):
            failures.append("every target must have a string name")
            continue
        name = target["name"]
        if name in by_name:
            failures.append(f"duplicate target: {name}")
        by_name[name] = target
    return by_name, failures


def _has_interval_schema(report: dict) -> bool:
    return "reportSequence" in report or "intervalDurationTicks" in report


def validate(
    report: dict,
    required: tuple[str, ...],
    require_intervals: bool = False,
    require_calls: bool = True,
    reject_fasttrack: bool = False,
) -> list[str]:
    failures = []
    if not isinstance(report.get("stopwatchFrequency"), int) or report["stopwatchFrequency"] <= 0:
        failures.append("stopwatchFrequency must be a positive integer")

    interval_schema = _has_interval_schema(report)
    if require_intervals or interval_schema:
        sequence = report.get("reportSequence")
        if not isinstance(sequence, int) or sequence <= 0:
            failures.append("reportSequence must be a positive integer")
        duration = report.get("intervalDurationTicks")
        if not isinstance(duration, int) or duration < 0:
            failures.append("intervalDurationTicks must be a non-negative integer")

    gc = report.get("gc")
    if not isinstance(gc, dict):
        failures.append("gc object is missing")
    else:
        for name in ("gen0Delta", "gen1Delta", "gen2Delta", "heapBytes", "heapDeltaBytes"):
            if not isinstance(gc.get(name), int):
                failures.append(f"gc.{name} must be an integer")

    by_name, target_failures = _targets_by_name(report)
    failures.extend(target_failures)
    if target_failures and not by_name:
        return failures

    for name in required:
        target = by_name.get(name)
        if target is None:
            failures.append(f"required target missing: {name}")
            continue
        if target.get("resolved") is not True:
            failures.append(f"required target did not resolve: {name}")
        fasttrack_patched = target.get("fastTrackPatched")
        if not isinstance(fasttrack_patched, bool):
            failures.append(f"{name} fastTrackPatched must be a boolean")
        elif reject_fasttrack and fasttrack_patched:
            failures.append(f"{name} is patched by FastTrack")
        calls = target.get("calls")
        if not isinstance(calls, int) or calls < 0:
            failures.append(f"required target has invalid calls: {name}")
        elif require_calls and calls <= 0:
            failures.append(f"required target has no calls: {name}")
        for metric in ("totalTicks", "maxTicks"):
            if not isinstance(target.get(metric), int) or target[metric] < 0:
                failures.append(f"{name} {metric} must be a non-negative integer")
        mean_ticks = target.get("meanTicks")
        if not isinstance(mean_ticks, (int, float)) or mean_ticks < 0:
            failures.append(f"{name} meanTicks must be non-negative")

        if require_intervals or interval_schema:
            interval_calls = target.get("intervalCalls")
            interval_total = target.get("intervalTotalTicks")
            interval_mean = target.get("intervalMeanTicks")
            if not isinstance(interval_calls, int) or interval_calls < 0:
                failures.append(f"{name} intervalCalls must be a non-negative integer")
            elif isinstance(calls, int) and interval_calls > calls:
                failures.append(f"{name} intervalCalls exceeds cumulative calls")
            if not isinstance(interval_total, int) or interval_total < 0:
                failures.append(f"{name} intervalTotalTicks must be a non-negative integer")
            elif isinstance(target.get("totalTicks"), int) and interval_total > target["totalTicks"]:
                failures.append(f"{name} intervalTotalTicks exceeds cumulative totalTicks")
            if not isinstance(interval_mean, (int, float)) or interval_mean < 0:
                failures.append(f"{name} intervalMeanTicks must be non-negative")
            elif isinstance(interval_calls, int) and isinstance(interval_total, int):
                expected_mean = 0.0 if interval_calls == 0 else interval_total / interval_calls
                if not math.isclose(float(interval_mean), expected_mean, abs_tol=0.001):
                    failures.append(f"{name} intervalMeanTicks does not match interval totals")

    return failures


def validate_series(
    report_items: list[dict],
    required: tuple[str, ...],
    reject_fasttrack: bool = False,
    require_fresh_calls: bool = False,
) -> list[str]:
    failures = []
    if len(report_items) < 2:
        return ["series validation requires at least two probe reports"]

    last_index = len(report_items) - 1
    for index, report in enumerate(report_items):
        for failure in validate(
            report,
            required,
            require_intervals=True,
            require_calls=index == last_index,
            reject_fasttrack=reject_fasttrack,
        ):
            failures.append(f"report[{index}]: {failure}")

    for index in range(1, len(report_items)):
        previous = report_items[index - 1]
        current = report_items[index]
        previous_sequence = previous.get("reportSequence")
        current_sequence = current.get("reportSequence")
        if isinstance(previous_sequence, int) and isinstance(current_sequence, int):
            if current_sequence != previous_sequence + 1:
                failures.append(
                    f"report[{index}]: reportSequence must follow {previous_sequence}, got {current_sequence}"
                )
        if current.get("stopwatchFrequency") != previous.get("stopwatchFrequency"):
            failures.append(f"report[{index}]: stopwatchFrequency changed within one capture")

        previous_targets, _ = _targets_by_name(previous)
        current_targets, _ = _targets_by_name(current)
        for name in required:
            before = previous_targets.get(name)
            after = current_targets.get(name)
            if before is None or after is None:
                continue
            before_fasttrack = before.get("fastTrackPatched")
            after_fasttrack = after.get("fastTrackPatched")
            if (
                isinstance(before_fasttrack, bool)
                and isinstance(after_fasttrack, bool)
                and before_fasttrack != after_fasttrack
            ):
                failures.append(
                    f"report[{index}]: {name} fastTrackPatched changed within one capture"
                )
            if not isinstance(before.get("calls"), int) or not isinstance(after.get("calls"), int):
                continue
            if not isinstance(before.get("totalTicks"), int) or not isinstance(after.get("totalTicks"), int):
                continue

            calls_delta = after["calls"] - before["calls"]
            total_delta = after["totalTicks"] - before["totalTicks"]
            if calls_delta < 0:
                failures.append(f"report[{index}]: {name} cumulative calls decreased")
                continue
            if total_delta < 0:
                failures.append(f"report[{index}]: {name} cumulative totalTicks decreased")
                continue
            if after.get("intervalCalls") != calls_delta:
                failures.append(
                    f"report[{index}]: {name} intervalCalls does not match cumulative delta"
                )
            if after.get("intervalTotalTicks") != total_delta:
                failures.append(
                    f"report[{index}]: {name} intervalTotalTicks does not match cumulative delta"
                )

    first = report_items[0]
    if first.get("reportSequence") == 1:
        first_targets, _ = _targets_by_name(first)
        for name in required:
            target = first_targets.get(name)
            if target is None:
                continue
            if target.get("intervalCalls") != target.get("calls"):
                failures.append(f"report[0]: {name} first intervalCalls must equal cumulative calls")
            if target.get("intervalTotalTicks") != target.get("totalTicks"):
                failures.append(
                    f"report[0]: {name} first intervalTotalTicks must equal cumulative totalTicks"
                )

    if require_fresh_calls:
        first_targets, _ = _targets_by_name(report_items[0])
        last_targets, _ = _targets_by_name(report_items[-1])
        for name in required:
            before = first_targets.get(name)
            after = last_targets.get(name)
            if before is None or after is None:
                continue
            before_calls = before.get("calls")
            after_calls = after.get("calls")
            if not isinstance(before_calls, int) or not isinstance(after_calls, int):
                continue
            if after_calls >= before_calls and after_calls - before_calls == 0:
                failures.append(f"{name} has no fresh calls after the selected baseline")

    return failures


def select_series_after_sequence(report_items: list[dict], after_sequence: int) -> list[dict]:
    """Select a capture anchored at a known report sequence and require a fresh report."""
    if after_sequence <= 0:
        raise ValueError("after_sequence must be a positive reportSequence")

    baseline_index = None
    for index in range(len(report_items) - 1, -1, -1):
        if report_items[index].get("reportSequence") == after_sequence:
            baseline_index = index
            break
    if baseline_index is None:
        raise ValueError(f"baseline reportSequence {after_sequence} was not found in the log")

    selected = report_items[baseline_index:]
    if len(selected) < 2:
        raise ValueError(
            f"no fresh probe report after reportSequence {after_sequence}; "
            "the deferred GameScheduler report may not have flushed"
        )
    return selected


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", type=Path, help="Player.log or another ONI log containing probe output")
    parser.add_argument(
        "--require",
        action="append",
        dest="required",
        help=(
            "target name that must resolve and have calls > 0; with --after-sequence, "
            "it must also receive fresh calls after the selected baseline; repeatable"
        ),
    )
    parser.add_argument(
        "--series",
        action="store_true",
        help="validate all reports plus interval/cumulative arithmetic for an in-run capture",
    )
    parser.add_argument(
        "--after-sequence",
        type=int,
        help=(
            "with --series, anchor validation at this known reportSequence and require "
            "at least one newer report plus fresh calls for every required target"
        ),
    )
    parser.add_argument(
        "--reject-fasttrack",
        action="store_true",
        help=(
            "fail if FastTrack patches any required target; use this for vanilla/CycleTrim "
            "baseline captures where external replacement would invalidate attribution"
        ),
    )
    args = parser.parse_args()

    if not args.log.is_file():
        parser.error(f"log not found: {args.log}")
    if args.after_sequence is not None and not args.series:
        parser.error("--after-sequence requires --series")

    try:
        report_items = reports(args.log.read_text(encoding="utf-8", errors="replace"))
        if args.after_sequence is not None:
            report_items = select_series_after_sequence(report_items, args.after_sequence)
    except ValueError as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    required = tuple(args.required) if args.required else DEFAULT_REQUIRED
    if args.series:
        failures = validate_series(
            report_items,
            required,
            reject_fasttrack=args.reject_fasttrack,
            require_fresh_calls=args.after_sequence is not None,
        )
        report = report_items[-1]
    else:
        report = report_items[-1]
        failures = validate(
            report,
            required,
            reject_fasttrack=args.reject_fasttrack,
        )
    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    for target in report["targets"]:
        if target.get("fastTrackPatched"):
            print(f"INFO: FastTrack also patches {target['name']}")
    if args.series:
        print(
            "PASS CycleTrim performance probe series is structurally valid "
            f"across {len(report_items)} reports"
        )
    else:
        print("PASS CycleTrim performance probe capture is structurally valid")
    return 0


if __name__ == "__main__":
    sys.exit(main())
