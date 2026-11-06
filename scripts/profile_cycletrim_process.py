#!/usr/bin/env python3
"""Read-only Linux process/thread diagnostics. Does not launch or control ONI."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import time


def stat_fields(text):
    # comm may contain spaces and parentheses; the final ')' ends that field.
    fields = text.rsplit(")", 1)[1].split()
    return {"state": fields[0], "startTicks": int(fields[19]), "cpuTicks": int(fields[11]) + int(fields[12]),
            "minorFaults": int(fields[7]), "majorFaults": int(fields[9])}


def key_values(path):
    result = {}
    for line in path.read_text().splitlines():
        key, separator, value = line.partition(":")
        if separator:
            first = value.strip().split()
            if first and first[0].isdigit():
                result[key] = int(first[0])
    return result


def pressure(path):
    try:
        return {row[0]: {k: float(v) for k, v in (item.split("=") for item in row[1:])}
                for row in (line.split() for line in path.read_text().splitlines())}
    except (OSError, ValueError):
        return None


def process_snapshot(pid, proc=Path("/proc")):
    root = proc / str(pid)
    started = time.monotonic()
    result = stat_fields((root / "stat").read_text())
    if result["state"] in ("Z", "X"):
        raise ProcessLookupError("Target process has exited (zombie/dead)")
    result["monotonicSeconds"] = started
    memory = key_values(root / "status")
    result["memoryKiB"] = {k: memory[k] for k in ("VmRSS", "VmSwap", "VmHWM", "RssAnon", "RssFile") if k in memory}
    # Optional kernel interfaces can be denied without invalidating basic CPU data.
    for filename, key in (("smaps_rollup", "rollupKiB"), ("io", "ioCounters")):
        try:
            result[key] = key_values(root / filename)
        except OSError:
            result[key] = None
    threads, lost = {}, 0
    for directory in (root / "task").iterdir():
        try:
            thread = stat_fields((directory / "stat").read_text())
            thread["name"] = (directory / "comm").read_text().strip()
            try:
                scheduler = [int(x) for x in (directory / "schedstat").read_text().split()]
                thread.update(runtimeNs=scheduler[0], runqueueWaitNs=scheduler[1], slices=scheduler[2])
            except (OSError, ValueError, IndexError):
                pass
            # Reject a TID reused while reading its other files.
            if stat_fields((directory / "stat").read_text())["startTicks"] != thread["startTicks"]:
                lost += 1
                continue
            threads[directory.name] = thread
        except (OSError, ValueError, IndexError):
            lost += 1
    result["threads"], result["threadsLostDuringRead"] = threads, lost
    result["host"] = {"loadAverage": list(os.getloadavg()),
                      "memoryKiB": key_values(proc / "meminfo"),
                      "pressure": {name: pressure(proc / "pressure" / name) for name in ("cpu", "memory", "io")}}
    final = stat_fields((root / "stat").read_text())
    if final["state"] in ("Z", "X"):
        raise ProcessLookupError("Target exited during a snapshot")
    if final["startTicks"] != result["startTicks"]:
        raise RuntimeError("Target PID was reused during a snapshot")
    result["readSeconds"] = time.monotonic() - started
    return result


def interval(before, after, hz):
    if before["startTicks"] != after["startTicks"]:
        raise ValueError("Target PID changed identity")
    wall = after["monotonicSeconds"] - before["monotonicSeconds"]
    if wall <= 0:
        raise ValueError("Non-positive sampling interval")
    cpu = (after["cpuTicks"] - before["cpuTicks"]) / hz
    if cpu < 0:
        raise ValueError("CPU counter decreased")
    rows = []
    for tid, current in after["threads"].items():
        prior = before["threads"].get(tid)
        if prior is None or prior["startTicks"] != current["startTicks"]:
            continue
        row = {"tid": int(tid), "name": current["name"],
               "cpuSeconds": (current["cpuTicks"] - prior["cpuTicks"]) / hz}
        for key in ("runtimeNs", "runqueueWaitNs", "slices"):
            if key in current and key in prior:
                row[key] = current[key] - prior[key]
        rows.append(row)
    io_before, io_after = before.get("ioCounters"), after.get("ioCounters")
    return {"wallSeconds": wall, "processCpuSeconds": cpu, "cpuCoresUsed": cpu / wall,
            "majorFaults": after["majorFaults"] - before["majorFaults"],
            "minorFaults": after["minorFaults"] - before["minorFaults"],
            "ioDelta": {k: io_after[k] - v for k, v in io_before.items() if k in io_after}
            if io_before is not None and io_after is not None else None,
            "threads": rows,
            "threadsNotMatched": len(set(before["threads"]) ^ set(after["threads"]))
            + sum(before["threads"][tid]["startTicks"] != after["threads"][tid]["startTicks"]
                  for tid in set(before["threads"]) & set(after["threads"]))}


def loaded_libraries(pid):
    """Record only mapped CycleTrim/Sim libraries, never cmdline or environment."""
    result = []
    paths = set()
    for line in (Path("/proc") / str(pid) / "maps").read_text().splitlines():
        parts = line.split(maxsplit=5)
        if len(parts) == 6 and parts[5].startswith("/"):
            path = Path(parts[5])
            if any(name in path.name.lower() for name in ("cycletrim", "onisim", "oni_sim", "simdll")):
                paths.add(path)
    for path in sorted(paths):
        try:
            result.append({"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        except OSError:
            result.append({"path": str(path), "sha256": None})
    return result


def summarize(samples, intervals):
    totals = {}
    for index, row in enumerate(intervals):
        for thread in row["threads"]:
            # TID plus start time prevents a recycled thread being merged.
            key = (thread["tid"], samples[index + 1]["threads"][str(thread["tid"])]["startTicks"])
            item = totals.setdefault(key, {"tid": thread["tid"], "name": thread["name"],
                                           "cpuSeconds": 0})
            for name in ("cpuSeconds", "runtimeNs", "runqueueWaitNs"):
                if name in thread:
                    item[name] = item.get(name, 0) + thread[name]
    cpu = sum(row["processCpuSeconds"] for row in intervals)
    wall = sum(row["wallSeconds"] for row in intervals)
    return {"wallSeconds": wall, "processCpuSeconds": cpu,
            "cpuCoresUsed": cpu / wall if wall else None,
            "majorFaults": sum(row["majorFaults"] for row in intervals),
            "peakRssKiB": max(s["memoryKiB"].get("VmRSS", 0) for s in samples),
            "peakSwapKiB": max(s["memoryKiB"].get("VmSwap", 0) for s in samples),
            "peakPssKiB": max((s["rollupKiB"].get("Pss", 0) for s in samples if s["rollupKiB"] is not None), default=None),
            "peakSwapPssKiB": max((s["rollupKiB"].get("SwapPss", 0) for s in samples if s["rollupKiB"] is not None), default=None),
            "maxObserverReadSeconds": max(s["readSeconds"] for s in samples),
            "threadCoverageIncomplete": any(r["threadsNotMatched"] for r in intervals)
            or any(s["threadsLostDuringRead"] for s in samples),
            "observedThreads": sorted(totals.values(), key=lambda t: t["cpuSeconds"], reverse=True)}


def collect(pid, seconds, cadence):
    first = process_snapshot(pid)
    report = {"schema": 1, "capturedAt": datetime.now(timezone.utc).isoformat(),
              "pid": pid, "startTicks": first["startTicks"], "clockTicksPerSecond": os.sysconf("SC_CLK_TCK"),
              "logicalCpus": os.cpu_count(), "cpuAffinity": sorted(os.sched_getaffinity(pid)),
              "librariesBefore": loaded_libraries(pid), "samples": [first], "intervals": [], "complete": False,
              "limits": "Read-only diagnostic, not FPS or a speedup. Thread totals omit unmatched/exited threads; "
              "schedstat wait is runnable queue wait, not total sleep. Process CPU includes the host/FFI. "
              "Samples are sequential, not atomic; readSeconds records observer cost. Mapped DLLs may omit managed assemblies; "
              "still verify the in-game loaded assembly receipt. Profile separately from acceptance timings."}
    deadline = first["monotonicSeconds"] + seconds
    try:
        while time.monotonic() < deadline:
            time.sleep(min(cadence, max(0, deadline - time.monotonic())))
            current = process_snapshot(pid)
            report["intervals"].append(interval(report["samples"][-1], current, report["clockTicksPerSecond"]))
            report["samples"].append(current)
        report["librariesAfter"] = loaded_libraries(pid)
        if stat_fields((Path("/proc") / str(pid) / "stat").read_text())["startTicks"] != first["startTicks"]:
            raise RuntimeError("Target PID changed before the final library check")
        if report["librariesAfter"] != report["librariesBefore"]:
            raise RuntimeError("Mapped simulation libraries changed during profiling")
        report["complete"] = True
    except (OSError, RuntimeError, ValueError, KeyboardInterrupt) as error:
        report["error"] = type(error).__name__ + ": " + str(error)
    report["summary"] = summarize(report["samples"], report["intervals"])
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--seconds", type=float, default=30)
    parser.add_argument("--interval", type=float, default=1)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.pid <= 0 or not math.isfinite(args.seconds) or not 1 <= args.seconds <= 600 or not math.isfinite(args.interval) or not .25 <= args.interval <= 10:
        parser.error("Use a positive PID, 1..600 seconds and a .25..10 second interval")
    if args.output.exists():
        parser.error("Choose a new output file; existing diagnostic evidence is preserved")
    report = collect(args.pid, args.seconds, args.interval)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x") as stream:
        json.dump(report, stream, indent=2, allow_nan=False)
        stream.write("\n")
    print(json.dumps({"complete": report["complete"], "output": str(args.output), "summary": report["summary"]}))
    return 0 if report["complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
