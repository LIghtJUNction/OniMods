#!/usr/bin/env python3
"""Validate and summarize opt-in FrameChain windows from an ONI Player.log.

Measures elapsed Game.Update entry-to-entry cadence, including synchronous
waits. Nested inclusive times are never added into frame coverage. Window
percentiles cannot establish pooled percentiles, presented FPS or GPU time.
"""

import argparse
import json
import sys
from pathlib import Path

MARKER = "[CycleTrim][FrameChain]"
INT32_MAX = (1 << 31) - 1
INT64_MAX = (1 << 63) - 1
EXPECTED_PHASES = (
    "Game.Update", "Game.LateUpdate", "Game.SimEveryTick", "Game.StepTheSim",
    "Global.Update", "Global.LateUpdate", "Game.UnsafeSim200ms", "Sim.PrepareGameData",
    "JobManager.Run", "StateMachineUpdater.AdvanceOneSimSubTick",
    "StateMachineUpdater.Render", "StateMachineUpdater.RenderEveryTick",
    "PropertyTextures.LateUpdate", "KAnimBatchManager.UpdateDirty",
    "KAnimBatchManager.Render", "Pathfinding.UpdateNavGrids",
    "AsyncPathProber.Manager.TickFrame", "KScreenManager.Update", "CameraController.Update",
    "GroundRenderer.Render", "KFMOD.RenderEveryTick", "App.LateUpdate",
)


def _integer(value, minimum=0, maximum=INT64_MAX):
    return type(value) is int and minimum <= value <= maximum


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def _invalid_constant(value):
    raise ValueError(f"non-finite JSON constant: {value}")


def reports(text: str) -> list[dict]:
    """Ignore ordinary log lines and known resolution notices, reject bad reports."""
    parsed = []
    for line_number, line in enumerate(text.splitlines(), 1):
        position = line.find(MARKER)
        if position < 0:
            continue
        payload = line[position + len(MARKER):].strip()
        if payload.startswith("unresolved target:"):
            continue
        try:
            report = json.loads(payload, object_pairs_hook=_unique_object,
                                parse_constant=_invalid_constant)
        except ValueError as error:
            raise ValueError(f"line {line_number}: invalid FrameChain JSON: {error}") from error
        if not isinstance(report, dict):
            raise ValueError(f"line {line_number}: FrameChain report must be an object")
        parsed.append(report)
    if not parsed:
        raise ValueError("no CycleTrim FrameChain JSON reports found")
    return parsed


def validate(report: dict) -> list[str]:
    """Validate one schema-1 window without inferring any missing measurements."""
    if not isinstance(report, dict):
        return ["report must be an object"]
    failures = []
    if type(report.get("schema")) is not int or report["schema"] != 1:
        failures.append("schema must be integer 1")
    if report.get("frameAnchor") != "Game.Update":
        failures.append("frameAnchor must be Game.Update")
    for name in ("capture", "sequence", "frequency", "totalTicks"):
        if not _integer(report.get(name), 1):
            failures.append(f"{name} must be a positive int64")
    if not _integer(report.get("frames"), 1, INT32_MAX):
        failures.append("frames must be a positive int32")
    for name in ("firstFrame", "endFrame"):
        if not _integer(report.get(name), -INT32_MAX - 1, INT32_MAX):
            failures.append(f"{name} must be an int32")
    for name in ("coveredTicks", "unattributedTicks", "droppedScopes",
                 "p50Ticks", "p95Ticks", "p99Ticks", "maxTicks"):
        if not _integer(report.get(name)):
            failures.append(f"{name} must be a non-negative int64")
    gc = report.get("gcCollectionsDelta")
    if not isinstance(gc, list) or len(gc) != 3 or any(
        not _integer(value, 0, INT32_MAX) for value in gc
    ):
        failures.append("gcCollectionsDelta must contain three non-negative int32 counts")
    phases = report.get("phases")
    if not isinstance(phases, list):
        failures.append("phases must be an array")
    if failures:
        return failures

    if ((report["endFrame"] - report["firstFrame"]) & 0xFFFFFFFF) != report["frames"]:
        failures.append("firstFrame/endFrame interval does not match frames (int32 wrap)")
    if report["coveredTicks"] + report["unattributedTicks"] != report["totalTicks"]:
        failures.append("coveredTicks + unattributedTicks must equal totalTicks")
    quantiles = [report[name] for name in ("p50Ticks", "p95Ticks", "p99Ticks", "maxTicks")]
    if quantiles != sorted(quantiles):
        failures.append("frame percentiles must be ordered p50 <= p95 <= p99 <= maxTicks")
    if not report["maxTicks"] <= report["totalTicks"] <= report["frames"] * report["maxTicks"]:
        failures.append("totalTicks is inconsistent with frames and frame maxTicks")

    names = set()
    self_ticks = 0
    valid_phase_totals = True
    for index, phase in enumerate(phases):
        if not isinstance(phase, dict):
            failures.append(f"phases[{index}] must be an object")
            valid_phase_totals = False
            continue
        name = phase.get("name")
        if not isinstance(name, str) or not name.strip():
            failures.append(f"phases[{index}].name must be a non-empty string")
        elif name in names:
            failures.append(f"duplicate phase: {name}")
        else:
            names.add(name)
        if type(phase.get("resolved")) is not bool:
            failures.append(f"{name}: resolved must be a boolean")
        metrics = ("calls", "inclusiveTicks", "selfTicks", "maxTicks")
        invalid_metrics = [key for key in metrics if not _integer(phase.get(key))]
        if invalid_metrics:
            failures.extend(f"{name}: {key} must be a non-negative int64" for key in invalid_metrics)
            valid_phase_totals = False
            continue
        self_ticks += phase["selfTicks"]
        if phase["selfTicks"] > phase["inclusiveTicks"]:
            failures.append(f"{name}: selfTicks exceeds inclusiveTicks")
        if phase["inclusiveTicks"] > report["coveredTicks"]:
            failures.append(f"{name}: inclusiveTicks exceeds coveredTicks")
        if phase["maxTicks"] > phase["inclusiveTicks"]:
            failures.append(f"{name}: call maxTicks exceeds inclusiveTicks")
        if phase["calls"] == 0 and any(phase[key] for key in metrics[1:]):
            failures.append(f"{name}: zero-call phase has timing data")
        if phase.get("resolved") is False and any(phase[key] for key in metrics):
            failures.append(f"{name}: unresolved phase has calls or timing data")
        if phase["inclusiveTicks"] > phase["calls"] * phase["maxTicks"]:
            failures.append(f"{name}: inclusiveTicks exceeds calls * call maxTicks")
    if valid_phase_totals and self_ticks != report["coveredTicks"]:
        failures.append("sum(selfTicks) must equal coveredTicks; do not sum inclusiveTicks")
    return failures


def validate_series(items: list[dict]) -> list[str]:
    failures = []
    if not items:
        return ["at least one FrameChain window is required"]
    previous = None
    for index, report in enumerate(items):
        errors = validate(report)
        failures.extend(f"window {index + 1}: {error}" for error in errors)
        if errors:
            previous = None
            continue
        if previous is not None:
            if report["capture"] < previous["capture"]:
                failures.append(f"window {index + 1}: capture decreased; separate process logs")
            elif report["capture"] == previous["capture"]:
                if report["sequence"] <= previous["sequence"]:
                    failures.append(f"window {index + 1}: sequence must increase within a capture")
                if report["frequency"] != previous["frequency"]:
                    failures.append(f"window {index + 1}: frequency changed within a capture")
        previous = report
    return failures


def summarize(items: list[dict], expected=EXPECTED_PHASES) -> dict:
    errors = validate_series(items)
    if errors:
        raise ValueError("; ".join(errors))
    frames = sum(item["frames"] for item in items)
    elapsed_ms = sum(item["totalTicks"] * 1000 / item["frequency"] for item in items)
    covered_ms = sum(item["coveredTicks"] * 1000 / item["frequency"] for item in items)
    unattributed_ms = sum(item["unattributedTicks"] * 1000 / item["frequency"] for item in items)
    phase_totals, windows, gaps = {}, [], []
    previous = None
    for item in items:
        scale = 1000 / item["frequency"]
        by_name = {phase["name"]: phase for phase in item["phases"]}
        missing = sorted(name for name in expected if name not in by_name)
        unresolved = sorted(name for name, phase in by_name.items() if not phase["resolved"])
        window = {key: item[key] for key in ("capture", "sequence", "firstFrame", "endFrame",
                                            "frames", "droppedScopes", "gcCollectionsDelta")}
        window.update({
            "elapsedMs": item["totalTicks"] * scale,
            "meanFrameMs": item["totalTicks"] * scale / item["frames"],
            "cadenceHz": item["frames"] * item["frequency"] / item["totalTicks"],
            "p50Ms": item["p50Ticks"] * scale, "p95Ms": item["p95Ticks"] * scale,
            "p99Ms": item["p99Ticks"] * scale, "maxFrameMs": item["maxTicks"] * scale,
            "coveredMs": item["coveredTicks"] * scale,
            "unattributedMs": item["unattributedTicks"] * scale,
            "unattributedPercent": 100 * item["unattributedTicks"] / item["totalTicks"],
            "missingTargets": missing, "unresolvedTargets": unresolved,
            "instrumentationComplete": not (missing or unresolved or item["droppedScopes"]),
        })
        windows.append(window)
        if previous is not None and item["capture"] == previous["capture"] and (
            item["sequence"] != previous["sequence"] + 1
            or item["firstFrame"] != previous["endFrame"]
        ):
            gaps.append({"capture": item["capture"], "afterSequence": previous["sequence"],
                         "beforeSequence": item["sequence"], "previousEndFrame": previous["endFrame"],
                         "nextFirstFrame": item["firstFrame"]})
        previous = item
        for name, phase in by_name.items():
            total = phase_totals.setdefault(name, {"name": name, "calls": 0, "inclusiveMs": 0.0,
                                                  "selfMs": 0.0, "maxCallMs": 0.0,
                                                  "observedWindows": 0, "resolvedWindows": 0})
            total["calls"] += phase["calls"]
            total["inclusiveMs"] += phase["inclusiveTicks"] * scale
            total["selfMs"] += phase["selfTicks"] * scale
            total["maxCallMs"] = max(total["maxCallMs"], phase["maxTicks"] * scale)
            total["observedWindows"] += 1
            total["resolvedWindows"] += int(phase["resolved"])
    for total in phase_totals.values():
        total["selfMsPerFrame"] = total["selfMs"] / frames
        total["inclusiveMsPerFrame"] = total["inclusiveMs"] / frames
        total["selfPercentOfElapsed"] = total["selfMs"] * 100 / elapsed_ms
        total["absentWindows"] = len(items) - total["observedWindows"]
        total["unresolvedWindows"] = total["observedWindows"] - total["resolvedWindows"]
    return {
        "schema": 1, "frameAnchor": "Game.Update", "windowCount": len(items),
        "captures": sorted({item["capture"] for item in items}), "frames": frames,
        "elapsedMs": elapsed_ms, "meanFrameMs": elapsed_ms / frames,
        "cadenceHz": frames * 1000 / elapsed_ms,
        "maxFrameMs": max(window["maxFrameMs"] for window in windows),
        "coveredMs": covered_ms, "coveredMsPerFrame": covered_ms / frames,
        "unattributedMs": unattributed_ms, "unattributedMsPerFrame": unattributed_ms / frames,
        "unattributedPercent": unattributed_ms * 100 / elapsed_ms,
        "droppedScopes": sum(item["droppedScopes"] for item in items),
        "gcCollectionsDelta": [sum(item["gcCollectionsDelta"][i] for item in items) for i in range(3)],
        "instrumentationComplete": all(window["instrumentationComplete"] for window in windows),
        "missingTargets": sorted({name for window in windows for name in window["missingTargets"]}),
        "unresolvedTargets": sorted({name for window in windows for name in window["unresolvedTargets"]}),
        "unknownPhases": sorted(set(phase_totals) - set(expected)), "gaps": gaps,
        "phases": sorted(phase_totals.values(), key=lambda phase: (-phase["selfMsPerFrame"], phase["name"])),
        "windows": windows,
        "interpretation": [
            "Elapsed Game.Update entry-to-entry cadence, not presented FPS or GPU duration.",
            "Coverage sums phase self time; nested inclusive times must not be added.",
            "Phase calls count outermost entries after same-phase recursion is coalesced; inclusive time uses the same coalescing.",
            "Resolved means a method signature was found, not proof that its hook ran.",
            "Instrumentation completeness covers reported field/resolution and arithmetic integrity, not all game work or Unity rendering.",
            "Only logged windows are aggregated; reset or missing intervals are not reconstructed.",
            "Percentiles are per window only; pooled frame or phase percentiles are unavailable.",
            "Max frame interval and maximum single phase call are distinct measurements.",
            "GC collection counts do not measure GC pause duration.",
        ],
    }


def summary_text(result: dict) -> str:
    last = result["windows"][-1]
    lines = [
        f"Game.Update cadence: {result['cadenceHz']:.2f} Hz, mean {result['meanFrameMs']:.3f} ms "
        f"({result['frames']} intervals, {result['windowCount']} windows; not presented FPS).",
        f"Maximum frame interval: {result['maxFrameMs']:.3f} ms; "
        f"unattributed: {result['unattributedMsPerFrame']:.3f} ms/frame "
        f"({result['unattributedPercent']:.1f}%).",
        f"Last window {last['capture']}/{last['sequence']} cadence percentiles: "
        f"P50={last['p50Ms']:.3f}, P95={last['p95Ms']:.3f}, P99={last['p99Ms']:.3f} ms "
        "(not pooled percentiles).",
        f"Instrumentation {'complete for reported fields only' if result['instrumentationComplete'] else 'INCOMPLETE'}; "
        f"dropped scopes={result['droppedScopes']}; window gaps={len(result['gaps'])}; "
        f"GC collections={result['gcCollectionsDelta']} (counts, not pause time).",
        "Phases ranked by self ms/frame (inclusive contains children; calls coalesce same-phase recursion):",
    ]
    for phase in result["phases"][:8]:
        lines.append(f"  {phase['name']}: self={phase['selfMsPerFrame']:.3f}, "
                     f"inclusive={phase['inclusiveMsPerFrame']:.3f}, "
                     f"calls={phase['calls']}, max single call={phase['maxCallMs']:.3f} ms")
    for key, label in (("missingTargets", "Absent targets"), ("unresolvedTargets", "Unresolved targets")):
        if result[key]:
            lines.append(label + ": " + ", ".join(result[key]))
    lines.append("Resolved means a method signature was found, not proof every hook ran or all game work was covered.")
    lines.append("Unattributed time may include unmeasured work or waits; it is not GPU timing.")
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", type=Path, help="ONI Player.log containing schema-1 FrameChain JSON")
    parser.add_argument("--output", type=Path, help="write JSON here instead of stdout; summary goes to stderr")
    args = parser.parse_args(argv)
    try:
        if args.output and args.output.resolve() == args.log.resolve():
            raise ValueError("--output must not overwrite the input log")
        result = summarize(reports(args.log.read_text(encoding="utf-8-sig", errors="replace")))
        result["source"] = str(args.log.resolve())
        encoded = json.dumps(result, indent=2, ensure_ascii=False, allow_nan=False) + "\n"
        if args.output:
            args.output.write_text(encoded, encoding="utf-8")
        else:
            sys.stdout.write(encoded)
        print(summary_text(result), file=sys.stderr)
    except (OSError, ValueError) as error:
        print(f"FrameChain validation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
