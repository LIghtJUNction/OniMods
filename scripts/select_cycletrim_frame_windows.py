#!/usr/bin/env python3
"""Watch OniHarness status and select complete, continuously observed FrameChain windows.

Read-only: never launches ONI, calls MCP, changes pause holds, or edits a save.
Game.Update entry-to-entry cadence is not presented FPS or GPU duration.
Use a completed log copy, retain rejected trials, and compare trial context
(save bytes, binary receipts, observer configuration and graphics) separately.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import sys
import time

import analyze_cycletrim_frame_chain as chain

IDENTITY_FIELDS = ("pid", "unity", "graphicsDevice", "batchMode", "headlessRequested",
                   "headless", "mode", "renderInterval", "updateRate", "targetFrameRate",
                   "vSyncCount", "runInBackground", "duplicants")
ENVIRONMENT_FIELDS = ("applicationIsFocused", "appHasFocus", "sleepWhenOutOfFocus",
                      "effectiveSleepWhenOutOfFocus", "backgroundThrottleOverrideRequested",
                      "backgroundThrottleOverrideApplied", "vSyncCount", "targetFrameRate")
OBSERVER_KIND = "cycletrim_frame_window_observer"


def stamp(value):
    if not isinstance(value, str):
        raise ValueError("Timestamp must be an ISO-8601 string")
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.tzinfo is None:
        raise ValueError("Timestamp must include a timezone")
    return result


def require(condition, message):
    if not condition:
        raise ValueError(message)


def receipt(path, data=None):
    data = path.read_bytes() if data is None else data
    return {"path": str(path.resolve()), "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest()}


def write_new(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write("\n")


def watch(args):
    require(math.isfinite(args.seconds) and 0 < args.seconds <= 3600,
            "seconds must be finite, > 0 and <= 3600")
    source = args.control / "status.json"
    require(args.output.resolve() != source.resolve(), "Output must not replace runtime status")
    deadline, previous, count = time.monotonic() + args.seconds, None, 0
    with args.output.open("x", encoding="utf-8") as stream:
        observer = {"kind": OBSERVER_KIND, "schema": 1, "script": receipt(Path(__file__)),
                    "python": sys.version, "pollSeconds": 0.1, "statusPath": str(source.resolve())}
        stream.write(json.dumps(observer, ensure_ascii=False) + "\n")
        stream.flush()
        while time.monotonic() < deadline:
            row = {}
            try:
                status = json.loads(source.read_text(encoding="utf-8"))
                age = (datetime.now(timezone.utc) - stamp(status["capturedAt"])).total_seconds()
                signature = (status.get("pid"), status["capturedAt"], not 0 <= age <= 5)
                if signature == previous:
                    time.sleep(0.1)
                    continue
                previous = signature
                row["status"] = status
            except (OSError, ValueError, KeyError, TypeError) as error:
                row["readError"] = str(error)
            row["observedAt"] = datetime.now(timezone.utc).isoformat()
            row["observedMonotonicNs"] = time.monotonic_ns()
            stream.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")
            stream.flush()
            count += 1
            time.sleep(0.1)
    print(json.dumps({"output": str(args.output), "observations": count}))
    return 0


def status_identity(status):
    for name in ("unity", "graphicsDevice", "mode"):
        require(isinstance(status.get(name), str) and status[name].strip(),
                "Missing process/rendering identity: " + name)
    for name in ("batchMode", "headlessRequested", "headless", "runInBackground"):
        require(type(status.get(name)) is bool, "Unverified boolean identity: " + name)
    for name in ("pid", "renderInterval", "updateRate"):
        require(type(status.get(name)) is int and status[name] > 0,
                "Missing positive integer identity: " + name)
    for name in ("vSyncCount", "duplicants"):
        require(type(status.get(name)) is int and status[name] >= 0,
                "Missing nonnegative integer identity: " + name)
    require(type(status.get("targetFrameRate")) is int, "Missing targetFrameRate")
    return {name: status[name] for name in IDENTITY_FIELDS}


def running_segments(records, max_age, max_gap):
    segments, errors, current, observers = [], [], [], []
    identity, previous_observation = None, None

    def finish():
        if len(current) >= 2:
            first, last = current[0], current[-1]
            before, after = first["status"], last["status"]
            game = after["simulationSeconds"] - before["simulationSeconds"]
            wall = (last["observedMonotonicNs"] - first["observedMonotonicNs"]) / 1e9
            segments.append({"firstFrame": before["frameCount"], "endFrame": after["frameCount"],
                             "firstCapturedAt": before["capturedAt"], "endCapturedAt": after["capturedAt"],
                             "observations": len(current), "gameSeconds": game,
                             "observedWallSeconds": wall,
                             "capturedWallSeconds": (stamp(after["capturedAt"]) - stamp(before["capturedAt"])).total_seconds(),
                             "gameSecondsPerObservedWallSecond": game / wall,
                             "positiveGameProgress": game > 0})
            if game <= 0:
                errors.append({"reason": "Unpaused interval has no positive game-clock progress"})
        elif current:
            errors.append({"reason": "Unpaused interval has fewer than two fresh observations"})
        current.clear()

    for number, row in enumerate(records, 1):
        if isinstance(row, dict) and row.get("kind") == OBSERVER_KIND:
            observers.append(row)
            continue
        try:
            require(isinstance(row, dict), "Observation must be an object")
            status = row.get("status")
            require(isinstance(status, dict), row.get("readError", "Missing status object"))
            observed, captured = stamp(row.get("observedAt")), stamp(status.get("capturedAt"))
            mono = row.get("observedMonotonicNs")
            require(type(mono) is int and mono > 0, "Missing positive monotonic observation time")
            require(0 <= (observed - captured).total_seconds() <= max_age,
                    "Telemetry is stale or has a future timestamp")
            previous = previous_observation
            previous_observation = (mono, captured)
            if previous is not None:
                observed_gap = (mono - previous[0]) / 1e9
                captured_gap = (captured - previous[1]).total_seconds()
                require(0 < observed_gap <= max_gap and 0 < captured_gap <= max_gap,
                        "Observation gap or nonmonotonic time: observed="
                        + str(observed_gap) + "s captured=" + str(captured_gap) + "s")
            require(status.get("schema") == 1 and type(status.get("schema")) is int,
                    "Unsupported runtime status schema")
            require(not status.get("error") and not status.get("gameError")
                    and status.get("gameHasCrash") is False,
                    "Runtime reports an error or unverified crash state")
            require(type(status.get("gameLoaded")) is bool, "Unverified gameLoaded state")
            if status["gameLoaded"] is False:
                finish()
                continue
            require(type(status.get("paused")) is bool, "Unverified pause state")
            if status["paused"]:
                finish()
                continue
            require(type(status.get("nativePauseCount")) is int and status["nativePauseCount"] == 0,
                    "Unpaused telemetry has unknown or nonzero native pause holds")
            require(type(status.get("frameCount")) is int and status["frameCount"] >= 0,
                    "Missing or wrapped frame counter")
            clock = status.get("simulationSeconds")
            require(type(clock) in (int, float) and math.isfinite(clock),
                    "Missing or non-finite game-clock counter")
            key = status_identity(status)
            if identity is None:
                identity = key
            require(key == identity, "Process, graphics, rendering policy or duplicant count changed")
            if current:
                previous_status = current[-1]["status"]
                require(status["frameCount"] > previous_status["frameCount"]
                        and clock >= previous_status["simulationSeconds"],
                        "Frame/game clock did not remain monotonic")
            current.append(row)
        except (ValueError, KeyError, TypeError) as error:
            finish()
            errors.append({"observation": number, "reason": str(error)})
    finish()
    return segments, errors, identity, observers


def checked_environment(frame, identity):
    environment = frame.get("environment")
    require(isinstance(environment, dict), "Missing focus/background-throttle telemetry")
    require(environment.get("settingsKnown") is True and environment.get("stable") is True,
            "Unverified or changing focus/throttle conditions")
    for name in ("observedChanges", "focusChanges"):
        require(type(environment.get(name)) is int and environment[name] == 0,
                "Unverified or changing focus/throttle conditions: " + name)
    require(type(environment.get("observations")) is int
            and environment["observations"] >= frame["frames"] + 1,
            "Environment observations do not cover both boundaries of every interval")
    for name in ENVIRONMENT_FIELDS[:6]:
        require(type(environment.get(name)) is bool, "Unverified focus/throttle boolean: " + name)
    require(environment["backgroundThrottleOverrideRequested"]
            and environment["backgroundThrottleOverrideApplied"]
            and not environment["effectiveSleepWhenOutOfFocus"],
            "Background-throttle override is not verified effective")
    require(type(environment.get("vSyncCount")) is int and environment["vSyncCount"] == 0
            and type(environment.get("targetFrameRate")) is int and environment["targetFrameRate"] == -1,
            "Uncapped rendering is not verified")
    require(all(environment[name] == identity[name] for name in ("vSyncCount", "targetFrameRate")),
            "FrameChain rendering policy disagrees with runtime status")
    return {name: environment[name] for name in ENVIRONMENT_FIELDS}


def select_windows(records, log_text, max_age=5.0, max_gap=2.5):
    require(math.isfinite(max_age) and max_age > 0, "max-status-age must be finite and positive")
    require(math.isfinite(max_gap) and max_gap > 0, "max-observation-gap must be finite and positive")
    segments, observation_errors, identity, observers = running_segments(records, max_age, max_gap)
    if observers and any(item != observers[0] for item in observers[1:]):
        observation_errors.append({"reason": "Observer configuration changed within samples"})
    chosen, excluded, window_errors, log_errors = [], [], [], []
    environment, selected_captures = None, set()
    try:
        reports = chain.reports(log_text)
        log_errors = chain.validate_series(reports)
        previous = None
        for frame in reports:
            if chain.validate(frame):
                continue
            if frame["firstFrame"] >= frame["endFrame"]:
                log_errors.append("Wrapped or reversed frame range cannot be bracketed by runtime status")
            if previous is not None and frame["capture"] == previous["capture"]:
                if frame["firstFrame"] < previous["endFrame"]:
                    log_errors.append("FrameChain ranges overlap within a capture")
            previous = frame
        if not log_errors:
            for frame in reports:
                ref = {name: frame[name] for name in ("capture", "sequence", "firstFrame", "endFrame")}
                if not any(segment["positiveGameProgress"]
                           and segment["firstFrame"] < frame["firstFrame"]
                           and frame["endFrame"] < segment["endFrame"] for segment in segments):
                    excluded.append(dict(ref, reason="Not strictly inside a continuously observed unpaused segment"))
                    continue
                try:
                    require(frame["droppedScopes"] == 0, "Selected window has dropped scopes")
                    conditions = checked_environment(frame, identity)
                    require(environment is None or conditions == environment,
                            "Focus/throttle conditions changed between selected windows")
                    environment = conditions
                except ValueError as error:
                    window_errors.append(dict(ref, reason=str(error), environment=frame.get("environment")))
                    continue
                selected_captures.add(frame["capture"])
                chosen.append(frame)
    except ValueError as error:
        log_errors.append(str(error))
    if len(selected_captures) > 1:
        window_errors.append({"reason": "Selected windows span multiple Game-instance captures"})
    result = {
        "schema": 3, "selectedWindows": len(chosen),
        "validWindowSelection": bool(chosen) and not observation_errors and not window_errors and not log_errors,
        "runningSegments": segments, "statusIdentity": identity, "observerRecords": observers,
        "verifiedEnvironment": environment, "excludedWindows": excluded,
        "observationErrors": observation_errors, "windowErrors": window_errors, "logErrors": log_errors,
        "selectionPolicy": {"maxStatusAgeSeconds": max_age, "maxObservationGapSeconds": max_gap,
                            "strictFrameInterior": True},
        "metric": "Elapsed Game.Update entry-to-entry cadence; not presented FPS or GPU duration.",
        "comparisonBoundary": {
            "pairedRunChecked": False,
            "requiredMatches": ["save SHA256", "observer binaries/configuration",
                                "graphics device/renderer/resolution/settings", "verifiedEnvironment",
                                "status rendering policy and duplicant count"],
            "recordTreatmentDifferences": ["installed and loaded binary receipts", "startup arguments", "simulator mode"],
            "requiresTrialAcceptance": "Use bounded-step/exit receipts and alarm/error evidence; retain aborted trials."
        },
        "limits": [
            "Valid window selection alone does not establish an accepted trial or a comparable A/B pair.",
            "Status writes about once per second; a shorter missed pause transition remains possible.",
            "An observation gap invalidates the trial even if other interior windows remain available for diagnosis.",
            "Game-clock/observed-wall rates cover sampled interior segments, not the full requested step or native CPU work.",
            "Do not pool or average window percentiles; use the existing FrameChain analyzer for weighted means and self times.",
            "Status graphicsDevice identifies an API, not the renderer, resolution or all graphics settings."
        ]}
    return chosen, result


def read_samples(text):
    records = []
    for number, line in enumerate(text.splitlines(), 1):
        if not line.strip():
            continue
        try:
            records.append(json.loads(line))
        except ValueError as error:
            records.append({"readError": "Invalid sample JSON at line " + str(number) + ": " + str(error)})
    return records


def select(args):
    require(args.output.resolve() != args.summary.resolve(), "Selection outputs must have distinct paths")
    for path in (args.output, args.summary):
        if path.exists():
            raise FileExistsError("Selection outputs must be new files: " + str(path))
    sample_bytes, log_bytes = args.samples.read_bytes(), args.log.read_bytes()
    selected, result = select_windows(read_samples(sample_bytes.decode("utf-8")),
                                      log_bytes.decode("utf-8", errors="replace"),
                                      args.max_status_age, args.max_observation_gap)
    result["sources"] = {"samples": receipt(args.samples, sample_bytes), "log": receipt(args.log, log_bytes)}
    result["selector"] = receipt(Path(__file__))
    result["analyzer"] = receipt(Path(chain.__file__))
    result["selectedLog"] = str(args.output.resolve())
    result["declaredTrialContext"] = None
    if args.context is not None:
        context_bytes = args.context.read_bytes()
        context = json.loads(context_bytes)
        require(isinstance(context, dict), "Trial context must be a JSON object")
        result["declaredTrialContext"] = context
        result["trialContextSource"] = receipt(args.context, context_bytes)
    context = result["declaredTrialContext"] or {}
    result["missingTrialContext"] = [key for key in ("save", "binaries", "observer", "graphics")
                                     if not isinstance(context.get(key), dict) or not context[key]]
    result["limits"].append("Trial context is an external declaration, not independently verified by this selector.")
    with args.output.open("x", encoding="utf-8") as stream:
        for frame in selected:
            stream.write(chain.MARKER + " " + json.dumps(frame, separators=(",", ":"), allow_nan=False) + "\n")
    write_new(args.summary, result)
    print(json.dumps({"summary": str(args.summary), "selectedWindows": len(selected),
                      "validWindowSelection": result["validWindowSelection"],
                      "missingTrialContext": result["missingTrialContext"]}))
    return 0 if result["validWindowSelection"] else 2


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    capture = commands.add_parser("watch", help="Record read-only status observations as JSONL")
    capture.add_argument("--control", type=Path, required=True)
    capture.add_argument("--seconds", type=float, required=True)
    capture.add_argument("--output", type=Path, required=True)
    selection = commands.add_parser("select", help="Keep strict interior windows; exit 2 for an invalid trial")
    selection.add_argument("--samples", type=Path, required=True)
    selection.add_argument("--log", type=Path, required=True, help="Completed Player.log copy")
    selection.add_argument("--output", type=Path, required=True)
    selection.add_argument("--summary", type=Path, required=True)
    selection.add_argument("--max-status-age", type=float, default=5.0)
    selection.add_argument("--max-observation-gap", type=float, default=2.5)
    selection.add_argument("--context", type=Path, help="External JSON declaration with save/binaries/observer/graphics objects")
    args = parser.parse_args()
    try:
        return watch(args) if args.command == "watch" else select(args)
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
