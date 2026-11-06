#!/usr/bin/env python3
"""Run the fixed lookup benchmark matrix in fresh, CPU-pinned processes."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
from datetime import datetime, timezone


RUNTIME_SWITCHES = tuple(
    prefix + setting
    for prefix in ("DOTNET_", "COMPlus_")
    for setting in ("TieredCompilation", "TieredPGO", "ReadyToRun",
                    "TC_OnStackReplacement", "TC_QuickJit", "TC_QuickJitForLoops")
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True, type=Path)
    parser.add_argument("--assembly", required=True, type=Path)
    parser.add_argument("--benchmark", required=True,
                        choices=("save-scoped", "state-machine"))
    parser.add_argument("--revision", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    dotnet, assembly = args.dotnet.resolve(), args.assembly.resolve()
    if not dotnet.is_file() or not assembly.is_file():
        parser.error("dotnet and the compiled benchmark assembly must exist")
    args.output.mkdir(parents=True, exist_ok=True)
    if any(args.output.iterdir()):
        parser.error("output must be empty so earlier samples are preserved")

    cpu = min(os.sched_getaffinity(0))
    receipt = {
        "revision": args.revision, "cpu": cpu,
        "benchmark": args.benchmark,
        "assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
        "started_utc": datetime.now(timezone.utc).isoformat(),
        "runs": [],
    }
    for mode in ("default", "tiering-off"):
        for trial in range(1, 4):
            environment = os.environ.copy()
            environment.update(DOTNET_ROOT=str(dotnet.parent),
                               DOTNET_CLI_TELEMETRY_OPTOUT="1")
            for key in RUNTIME_SWITCHES:
                environment.pop(key, None)
            if mode == "tiering-off":
                environment["DOTNET_TieredCompilation"] = "0"
            command = ["taskset", "-c", str(cpu), str(dotnet), str(assembly),
                       "--benchmark-" + args.benchmark]
            log = args.output / f"{mode}-{trial}.log"
            timed_out = False
            with log.open("w", encoding="utf-8") as stream:
                try:
                    process = subprocess.run(command, env=environment, stdout=stream,
                                             stderr=subprocess.STDOUT, timeout=300)
                    exit_code = process.returncode
                except subprocess.TimeoutExpired:
                    exit_code, timed_out = 124, True
            receipt["runs"].append({
                "mode": mode, "process": trial, "log": log.name,
                "command": command, "exit_code": exit_code,
                "timed_out": timed_out,
                "environment_overrides": {
                    **{key: environment.get(key) for key in RUNTIME_SWITCHES},
                    "DOTNET_ROOT": str(dotnet.parent),
                    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                },
            })
            (args.output / "receipt.json").write_text(
                json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
            print(f"{mode} process {trial}: exit {exit_code}", flush=True)
            if exit_code:
                raise SystemExit(exit_code)


if __name__ == "__main__":
    main()
