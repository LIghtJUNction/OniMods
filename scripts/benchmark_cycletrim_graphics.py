#!/usr/bin/env python3
"""Measure rendered frames during guarded ONI steps, retaining raw telemetry."""
import argparse
from datetime import datetime
import json
from pathlib import Path
import re
import statistics
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
STATUS_KEYS = (
    "capturedAt", "pid", "gameLoaded", "paused", "simulationSeconds", "duplicants",
    "frameCount", "renderedFrameCount", "cameraRenderFrames", "targetFrameRate",
    "vSyncCount", "graphicsDevice", "gameError", "gameHasCrash",
)


def host_state():
    counters = dict(line.split() for line in Path("/proc/vmstat").read_text().splitlines())
    compilers = subprocess.run(["ps", "-C", "rustc", "-o", "pid=,rss="],
                               capture_output=True, text=True, check=False)
    return {
        "pswpin": int(counters["pswpin"]), "pswpout": int(counters["pswpout"]),
        "memoryPressure": Path("/proc/pressure/memory").read_text().strip(),
        "rustcProcesses": [{"pid": int(pid), "rssKiB": int(rss)}
                           for pid, rss in (line.split() for line in compilers.stdout.splitlines())],
    }


def active_windows(rows):
    groups = []
    last = -2
    for index, (before, after) in enumerate(zip(rows, rows[1:])):
        if before["paused"] is not False or after["paused"] is not False:
            continue
        elapsed = (datetime.fromisoformat(after["capturedAt"].replace("Z", "+00:00"))
                   - datetime.fromisoformat(before["capturedAt"].replace("Z", "+00:00"))).total_seconds()
        if elapsed <= 0:
            raise ValueError("Telemetry timestamps did not advance")
        if index != last + 1:
            groups.append([])
        groups[-1].append((elapsed, after["cameraRenderFrames"] - before["cameraRenderFrames"],
                          after["simulationSeconds"] - before["simulationSeconds"]))
        last = index
    windows = []
    for group in groups:
        elapsed = sum(row[0] for row in group)
        windows.append({"activeSeconds": elapsed,
                        "cameraFramesPerSecond": sum(row[1] for row in group) / elapsed,
                        "nativeSecondsPerWallSecond": sum(row[2] for row in group) / elapsed})
    return windows


def analyze(raw, steps):
    windows = active_windows(raw["rows"])
    warmups = steps["warmups"]
    measured = windows[warmups:]
    issues = []
    if not isinstance(raw.get("viewBefore"), dict) or "position" not in raw["viewBefore"]:
        issues.append("Camera view was not captured as structured data")
    elif raw["viewBefore"] != raw.get("viewAfter"):
        issues.append("Camera view, viewport or active world changed during measurement")
    if raw["returncode"] or not steps.get("complete"):
        issues.append("Guarded step collection failed")
    if steps.get("headless") is not False:
        issues.append("A graphical game is required")
    if len(windows) != warmups + len(steps["samples"]):
        issues.append("Missing distinct active windows; pause boundaries were not fully observed")
    if any(row["activeSeconds"] < steps["requestedStepSeconds"] - 2 for row in measured):
        issues.append("Too little of a running window was observed")
    if any(row["controlOverheadSeconds"] > 2 for row in steps["samples"]):
        issues.append("Control overhead exceeded two seconds; inspect stalls and host pressure")
    if any(row.get("gameError") or row.get("gameHasCrash") for row in raw["rows"]):
        issues.append("The game reported an error")
    if len({row["pid"] for row in raw["rows"]}) != 1:
        issues.append("Game process changed")
    if len({row["duplicants"] for row in raw["rows"]}) != 1:
        issues.append("Population changed during collection")
    if any(len({row[key] for row in raw["rows"]}) != 1
           for key in ("targetFrameRate", "vSyncCount", "graphicsDevice")):
        issues.append("Render policy changed during collection")
    if any(row["cameraFramesPerSecond"] < 0 or row["nativeSecondsPerWallSecond"] <= 0
           for row in measured):
        issues.append("Counters reset or no native work advanced")
    summary = {}
    if measured:
        for key in ("cameraFramesPerSecond", "nativeSecondsPerWallSecond"):
            values = [row[key] for row in measured]
            summary[key] = {"median": statistics.median(values), "min": min(values), "max": max(values)}
    else:
        issues.append("No measured windows")
    return {"schema": 1, "label": raw["label"], "validForComparison": not issues,
            "issues": issues, "warmupsExcluded": warmups, "windows": windows, "summary": summary,
            "limits": "Camera callback cadence, not display presentation timing. Transition intervals are excluded. "
                      "Simulation throughput is measured at the requested speed cap. Inspect raw steps and host "
                      "pressure even when control timing passes; invalid series are retained, not silently filtered."}


def collect(args):
    raw_path = args.output / ("cadence-" + args.label + ".json")
    if raw_path.exists():
        raise ValueError("Use a new label; raw measurements already exist")
    initial = json.loads((args.output / "status.json").read_text())
    if initial.get("headless") is not False or initial.get("paused") is not True or not initial.get("duplicants", 0):
        raise ValueError("A loaded, paused graphical test save is required")
    sys.path.insert(0, str(ROOT / "tools/OniHarness/scripts"))
    from runtime_mcp import Client
    client = Client()

    def camera_view():
        view = client.read("navigation_control", {"domain": "camera", "action": "get_view",
            "task": "记录性能测试前后的视角和视口，保持对照条件一致。"})
        if not isinstance(view, dict) or "position" not in view:
            raise ValueError("Camera view was not returned as structured data")
        return view

    data = {"schema": 2, "label": args.label, "hostBefore": host_state(),
            "viewBefore": camera_view(), "rows": []}
    python = ROOT / "tools/OniHarness/.venv-runtime/bin/python"
    measure = ROOT / "tools/OniHarness/scripts/measure_steps.py"
    command = [str(python), str(measure), "--output", str(args.output.resolve()), "--label", args.label,
               "--samples", str(args.samples), "--warmup", str(args.warmup),
               "--seconds", str(args.seconds), "--speed", str(args.speed)]
    with (args.output / ("cadence-" + args.label + ".log")).open("w") as log:
        child = subprocess.Popen(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        try:
            while child.poll() is None:
                try:
                    status = json.loads((args.output / "status.json").read_text())
                    row = {key: status.get(key) for key in STATUS_KEYS}
                    if not data["rows"] or row["capturedAt"] != data["rows"][-1]["capturedAt"]:
                        data["rows"].append(row)
                except (OSError, ValueError):
                    pass
                time.sleep(.1)
        finally:
            if child.poll() is None:
                child.terminate()
                try:
                    child.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait()
            data.update(returncode=child.returncode, hostAfter=host_state())
            try:
                data["viewAfter"] = camera_view()
            except (OSError, RuntimeError, ValueError) as error:
                data["viewAfterError"] = str(error)
            raw_path.write_text(json.dumps(data, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="Fresh loaded, paused graphical Harness runtime")
    parser.add_argument("--label", required=True)
    parser.add_argument("--samples", type=int, default=5)
    parser.add_argument("--warmup", type=int, default=1)
    parser.add_argument("--seconds", type=float, default=5)
    parser.add_argument("--speed", type=int, choices=[1, 2, 3], default=3)
    parser.add_argument("--analyze-only", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9_-]+", args.label):
        parser.error("Use an alphanumeric label with hyphens or underscores")
    if not 0 < args.seconds <= 10 or not 1 <= args.samples <= 20 or not 0 <= args.warmup <= 3:
        parser.error("Use 0..10 step seconds, 1..20 samples and 0..3 warmups")
    if not args.analyze_only:
        collect(args)
    raw = json.loads((args.output / ("cadence-" + args.label + ".json")).read_text())
    steps = json.loads((args.output / ("step-benchmark-" + args.label + ".json")).read_text())
    result = analyze(raw, steps)
    (args.output / ("graphics-" + args.label + ".json")).write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))
    return 0 if result["validForComparison"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
