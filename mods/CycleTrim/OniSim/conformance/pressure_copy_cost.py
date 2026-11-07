#!/usr/bin/env python3
"""Measure the retained pressure-copy probe; this is not a game benchmark."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import resource
import statistics
import subprocess

from benchmark_frames import parse_cpus, require_no_game


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", required=True, type=Path, help="Release unit executable with pressure-copy-cost-probe.patch applied")
    parser.add_argument("--source", required=True, type=Path, help="The exact native crate used for that executable")
    parser.add_argument("--output", required=True, type=Path, help="New private output directory")
    parser.add_argument("--cpus", required=True, type=parse_cpus)
    parser.add_argument("--samples", type=int, default=6)
    args = parser.parse_args()
    if args.samples < 6 or len(args.cpus) < 4 or not set(args.cpus) <= os.sched_getaffinity(0):
        parser.error("Use at least six samples and four available CPUs")
    if not args.binary.is_file() or not os.access(args.binary, os.X_OK):
        parser.error("Supply the compiled release unit executable")
    files = {"probe_executable": args.binary, "driver": Path(__file__),
             "game_guard": Path(__file__).with_name("benchmark_frames.py")}
    files.update({str(p.relative_to(args.source)): p for p in sorted((args.source / "src").glob("*.rs"))})
    for name in ("Cargo.toml", "Cargo.lock"):
        files[name] = args.source / name
    def fingerprints():
        return {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in files.items()}
    identity = fingerprints()
    args.output.mkdir(parents=True, exist_ok=False)
    (args.output / "inputs.json").write_text(json.dumps(identity, indent=2) + "\n")
    rows = []
    for sample in range(args.samples):
        order = ("serial", "copy_only", "parallel") if sample % 2 == 0 else ("parallel", "copy_only", "serial")
        for mode in order:
            require_no_game()
            output = args.output / f"{sample}-{mode}.json"
            environment = dict(os.environ, CYCLETRIM_COST_MODE=mode, CYCLETRIM_COST_OUTPUT=str(output.resolve()))
            before = resource.getrusage(resource.RUSAGE_CHILDREN)
            run = subprocess.run(["taskset", "-c", ",".join(map(str, args.cpus)), str(args.binary.resolve()),
                "--exact", "simulation_step_fluid::tiles::tests::pressure_copy_cost_probe", "--ignored", "--nocapture"],
                env=environment, capture_output=True, text=True, timeout=30)
            after = resource.getrusage(resource.RUSAGE_CHILDREN)
            (args.output / f"{sample}-{mode}.log").write_text(run.stdout + run.stderr)
            run.check_returncode()
            require_no_game()
            row = json.loads(output.read_text())
            if row.get("unchanged_state_verified") is not True or row.get("mode") != mode or row.get("iterations") != 64:
                raise RuntimeError("Wrong, missing or failed pressure probe result")
            row.update(sample=sample, process_cpu_seconds_including_setup=
                       after.ru_utime + after.ru_stime - before.ru_utime - before.ru_stime)
            rows.append(row)
            print(sample, mode, row["wall_seconds"], row["process_cpu_seconds_including_setup"], flush=True)
    if fingerprints() != identity:
        raise RuntimeError("Probe inputs changed during measurement")
    summary = {mode: {field: dict(median=statistics.median(values:=[r[field] for r in rows if r["mode"] == mode]),
                                minimum=min(values), maximum=max(values))
                     for field in ("wall_seconds", "process_cpu_seconds_including_setup")}
               for mode in ("serial", "copy_only", "parallel")}
    result = dict(scope="Authored uniform 256x128 pressure-only premise check, not frames or game FPS",
                  cpu_affinity=args.cpus, workers=4, warmup_phases=4, timed_phases=64,
                  samples_per_mode=args.samples, inputs_sha256=identity, samples=rows, summary=summary,
                  process_cpu_scope="Whole child including fixture/pool setup, warmup and validation; phase wall timer excludes those")
    (args.output / "summary.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
