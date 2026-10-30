#!/usr/bin/env python3
"""Capture complete unchanged whole-CLI benchmarks, separately from formal pairs."""

import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("prior", "candidate", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--cpu", type=int, default=0)
    args = parser.parse_args()
    directory = args.output.resolve()
    directory.mkdir(parents=True, exist_ok=False)
    assemblies = {name: getattr(args, name).resolve() for name in ("prior", "candidate")}
    environment = os.environ.copy()
    cleared = [prefix + name for prefix in ("DOTNET_", "COMPlus_")
               for name in ("TieredCompilation", "TieredPGO", "JitDisasm", "JitStdOutFile")]
    for name in cleared:
        environment.pop(name, None)
    metadata = {
        "started_utc": datetime.now(timezone.utc).isoformat(),
        "protocol": "unchanged whole benchmark entry points; separate sessions, not temporal prior/new pairs",
        "cpu": args.cpu,
        "allowed_cpu_affinity": sorted(os.sched_getaffinity(0)),
        "runtime_settings": "default; listed overrides removed from subprocess environment",
        "cleared_environment_variables": cleared,
        "assembly_sha256": {name: digest(path) for name, path in assemblies.items()},
        "runner_source_sha256": digest(__file__),
        "processes": [],
    }
    if args.cpu not in metadata["allowed_cpu_affinity"]:
        raise ValueError("requested CPU unavailable")
    markers = {"conveyor": ("CONVEYOR_BENCH_SAMPLE ", 42),
               "animation": ("BENCH animation-symbols ", 196),
               "power": ("POWER_BENCH_SAMPLE ", 14)}
    for index, family in enumerate(markers):
        order = ("prior", "candidate") if index % 2 == 0 else ("candidate", "prior")
        for revision in order:
            command = ["taskset", "-c", str(args.cpu), args.dotnet,
                       str(assemblies[revision]), "--benchmark-" + family]
            completed = subprocess.run(command, env=environment, stdout=subprocess.PIPE,
                                       stderr=subprocess.STDOUT, text=True, timeout=120)
            log = directory / f"{family}-{revision}.log"
            log.write_text(completed.stdout)
            marker, expected = markers[family]
            count = sum(line.startswith(marker) for line in completed.stdout.splitlines())
            metadata["processes"].append({"family": family, "revision": revision,
                "command": command, "exit_code": completed.returncode,
                "variant_rows_or_pairs": count, "expected": expected,
                "log_sha256": digest(log)})
            (directory / "run.json").write_text(json.dumps(metadata, indent=2) + "\n")
            if completed.returncode or count != expected:
                raise RuntimeError(f"incomplete {family}/{revision}: exit={completed.returncode}, rows={count}")
            print(f"completed {family}/{revision}: {count} rows/pairs", flush=True)
    spec = importlib.util.spec_from_file_location("integrated_default_summary",
                                                Path(__file__).with_name("summarize.py"))
    summarizer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(summarizer)
    summarizer.summarize(directory)
    print(f"saved complete validated default-runtime cross-check to {directory}", flush=True)


if __name__ == "__main__":
    main()
