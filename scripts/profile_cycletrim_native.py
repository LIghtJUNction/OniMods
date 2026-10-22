#!/usr/bin/env python3
"""Profile a native fixture's Python/FFI host and Linux threads, separately from timing acceptance."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import pstats
import shutil
import signal
import subprocess
import sys
import time

from profile_cycletrim_process import interval, process_snapshot, summarize

ROOT = Path(__file__).resolve().parents[1]
PROFILE_BOOTSTRAP = """import cProfile, runpy, sys, os
profiler = cProfile.Profile()
destination = sys.argv[1]
sys.argv = sys.argv[2:]
sys.path.insert(0, os.path.dirname(sys.argv[0]))
profiler.enable()
try:
    runpy.run_path(sys.argv[0], run_name='__main__')
finally:
    profiler.disable()
    profiler.dump_stats(destination)
"""


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require_no_game():
    for directory in Path("/proc").iterdir():
        if not directory.name.isdigit():
            continue
        try:
            if (directory / "comm").read_text().startswith("OxygenNot"):
                raise RuntimeError("A live ONI game would interfere; leave it untouched and profile later")
        except OSError:
            pass


def python_stats(path):
    stats = pstats.Stats(str(path))
    rows = [{"file": key[0], "line": key[1], "function": key[2], "primitiveCalls": value[0],
             "calls": value[1], "selfWallSeconds": value[2], "inclusiveWallSeconds": value[3]}
            for key, value in stats.stats.items()]
    return {"totalCalls": stats.total_calls,
            "topSelf": sorted(rows, key=lambda r: r["selfWallSeconds"], reverse=True)[:30],
            "topInclusive": sorted(rows, key=lambda r: r["inclusiveWallSeconds"], reverse=True)[:30]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--harness-dir", type=Path, default=ROOT / "mods/CycleTrim/OniSim/conformance")
    parser.add_argument("--driver", choices=("benchmark_frames.py", "benchmark_subsystems.py"), default="benchmark_frames.py")
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--library-sha256", required=True)
    parser.add_argument("--role", choices=("candidate", "reference"), required=True)
    parser.add_argument("--mode", choices=("python", "syscalls"), default="python",
                        help="Separate runs: Python/FFI attribution or strace thread wait/syscall totals")
    parser.add_argument("--scene", required=True)
    parser.add_argument("--size", type=int, default=192)
    parser.add_argument("--frames", type=int)
    parser.add_argument("--warmup", type=int)
    parser.add_argument("--workers", type=int, choices=range(1, 9), default=1)
    parser.add_argument("--cpus", help="comma-separated physical CPU ids; held fixed for all workers")
    parser.add_argument("--timeout", type=float, default=180)
    parser.add_argument("--output", type=Path, required=True, help="new directory; never overwrites evidence")
    args = parser.parse_args()
    if not math.isfinite(args.timeout) or not 1 <= args.timeout <= 600 or (args.frames is not None and args.frames < 1) or (args.warmup is not None and args.warmup < 2):
        parser.error("Use 1..600 timeout seconds, positive frames and at least two warmups")
    if args.driver == "benchmark_frames.py" and (args.frames is not None or args.warmup is not None):
        parser.error("benchmark_frames.py has fixed warmup/frame counts; use benchmark_subsystems.py for configurable counts")
    if args.mode == "syscalls" and shutil.which("strace") is None:
        parser.error("strace is unavailable; no profiler is installed or security setting changed automatically")
    library = args.library.resolve()
    if not library.is_file():
        parser.error("The selected library is absent")
    if sha256(library) != args.library_sha256:
        parser.error("Library SHA256 does not match the explicitly selected candidate")
    driver = args.harness_dir.resolve() / args.driver
    if not driver.is_file():
        parser.error("The requested fixture driver is absent")
    require_no_game()
    args.output.mkdir(parents=True, exist_ok=False)
    # The child's own script still enforces its scene/size/ABI checks. No alternate solver.
    result = args.output / "native-result.json"
    profile = args.output / "python.pstats"
    # cProfile's CLI can swallow SystemExit from the target. Preserve failures
    # explicitly, while still saving partial profiler data in its finally block.
    fixture_args = ["--child", "--library", str(library), "--role", args.role,
                    "--scene" if args.driver == "benchmark_subsystems.py" else "--case", args.scene,
                    "--size", str(args.size), "--output", str(result.resolve())]
    if args.driver == "benchmark_subsystems.py":
        fixture_args += ["--frames", str(args.frames or 40), "--warmup", str(args.warmup or 4)]
    if args.cpus:
        fixture_args += ["--cpus", args.cpus]
    command = [sys.executable, "-c", PROFILE_BOOTSTRAP, str(profile.resolve()), str(driver)] + fixture_args
    if args.mode == "syscalls":
        command = ["strace", "-f", "-w", "-c", "-o", str((args.output / "syscalls.txt").resolve()),
                   sys.executable, str(driver)] + fixture_args
    environment = dict(os.environ, CYCLETRIM_ONISIM_WORKERS=str(args.workers))
    report = {"schema": 1, "capturedAt": datetime.now(timezone.utc).isoformat(), "complete": False,
              "librarySha256": args.library_sha256, "driverSha256": sha256(driver),
              "harnessFiles": {p.name: sha256(p) for p in sorted(driver.parent.glob("*.py"))},
              "command": command, "mode": args.mode, "workers": args.workers, "samples": [], "intervals": [],
              "limits": "Diagnostic whole-child profile includes startup, warmup, measured frames, snapshot/readback, "
              "validation and serialization. cProfile observes the Python thread, not Rust worker stacks. "
              "Its ctypes call durations can include native work or waiting. Sampling/profiling changes timings; "
              "strace -w totals are overlapping syscall wall durations across threads, not CPU time or a complete sleep breakdown. "
              "do not use this run for speedup acceptance. Native progress checks do not prove equality with the official reference."}
    child = None
    try:
        with (args.output / "child.log").open("x") as log:
            child = subprocess.Popen(command, env=environment, stdout=log, stderr=log, start_new_session=True)
            deadline = time.monotonic() + args.timeout
            hz = os.sysconf("SC_CLK_TCK")
            profile_pid = child.pid if args.mode == "python" else None
            while child.poll() is None:
                if time.monotonic() >= deadline:
                    raise TimeoutError("Owned diagnostic child exceeded timeout")
                require_no_game()
                if profile_pid is None:
                    children = Path(f"/proc/{child.pid}/task/{child.pid}/children").read_text().split()
                    if len(children) != 1:
                        time.sleep(.05)
                        continue
                    profile_pid = int(children[0])
                report["profiledPid"] = profile_pid
                try:
                    current = process_snapshot(profile_pid)
                except (FileNotFoundError, ProcessLookupError):
                    if child.wait(timeout=5) != 0:
                        raise RuntimeError("Diagnostic child exited unsuccessfully")
                    break
                if report["samples"]:
                    report["intervals"].append(interval(report["samples"][-1], current, hz))
                report["samples"].append(current)
                time.sleep(.25)
            report["exitCode"] = child.wait(timeout=5)
            if report["exitCode"]:
                raise RuntimeError("Diagnostic child failed; retained child.log and partial samples")
        require_no_game()
        if sha256(library) != args.library_sha256 or sha256(driver) != report["driverSha256"]:
            raise RuntimeError("Selected input changed during profiling")
        for name, expected in report["harnessFiles"].items():
            if sha256(driver.parent / name) != expected:
                raise RuntimeError("Fixture dependency changed during profiling: " + name)
        native = json.loads(result.read_text())
        if native.get("native_sha256") != args.library_sha256 or not native.get("wall_seconds", 0) > 0:
            raise RuntimeError("Child receipt lacks the selected library identity or completed work")
        if args.driver == "benchmark_subsystems.py" and native.get("timed_frames") != (args.frames or 40):
            raise RuntimeError("Child did not complete the requested frame count")
        if args.mode == "python":
            report["python"] = python_stats(profile)
        report["nativeResult"] = native
        report["complete"] = True
    except (OSError, ValueError, RuntimeError, TimeoutError, subprocess.TimeoutExpired, KeyboardInterrupt) as error:
        report["error"] = type(error).__name__ + ": " + str(error)
    finally:
        if child is not None and child.poll() is None:
            # The profiler and its one fixture belong to this newly-created group.
            # Terminate the group so strace cannot detach a timed-out fixture.
            try:
                os.killpg(child.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            try:
                child.wait(timeout=5)
            except subprocess.TimeoutExpired:
                try:
                    os.killpg(child.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                child.wait()
        if report["samples"]:
            report["processSummary"] = summarize(report["samples"], report["intervals"])
        with (args.output / "profile.json").open("x") as stream:
            json.dump(report, stream, indent=2, allow_nan=False)
            stream.write("\n")
    print(json.dumps({"complete": report["complete"], "error": report.get("error"),
                      "report": str(args.output / "profile.json")}))
    return 0 if report["complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
