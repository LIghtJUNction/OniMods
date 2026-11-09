#!/usr/bin/env python3
"""Paired complete native worker/Engine/publication validation, not game FPS."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import statistics
import subprocess
import time


TEST = "native_chain_tests::benchmark_native_chain_sample"
CASES = [(w, h, scene, "0.2") for w, h in [(96, 96), (256, 384)]
         for scene in ["pure", "liquid", "mixed", "thermal", "regions"]]
CASES += [(736, 304, scene, "0.2") for scene in ["pure", "mixed"]]
CASES += [(96, 96, "regions", "1.0")]
CASES += [(96, 96, scene, "0.2") for scene in ["empty", "zero", "command"]]
CASES += [(w, h, "alternating_idle", "0.0") for w, h in [(96, 96), (256, 384), (736, 304)]]


def case_id(case):
    width, height, scene, elapsed = case
    return f"{width}x{height}_{scene}_dt{elapsed}"


def gate_limit(name, scope):
    if name == "736x304_alternating_idle_dt0.0":
        return -10
    if scope == "combined" and name == "736x304_pure_dt0.2":
        return -5
    return 3


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def source_identity(root):
    files = {}
    for package in ("native", "gpu"):
        directory = root / "mods/CycleTrim/OniSim" / package
        for path in sorted((directory / "src").rglob("*")):
            if path.is_file() and path.suffix in (".rs", ".wgsl"):
                files[str(path.relative_to(root))] = digest(path)
        for name in ("Cargo.toml", "Cargo.lock"):
            path = directory / name
            if path.exists():
                files[str(path.relative_to(root))] = digest(path)
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    return dict(head=head, files_sha256=files)


def parse(log):
    snapshots, sample = [], None
    for line in log.splitlines():
        if "NATIVE_CHAIN_SNAPSHOT," in line:
            fields = line[line.index("NATIVE_CHAIN_SNAPSHOT,"):].split(",")
            snapshots.append(dict(label=fields[1], publication_bytes=int(fields[2]),
                                  publication_sha256=fields[3], world_bytes=int(fields[4]),
                                  world_sha256=fields[5]))
        if "NATIVE_CHAIN_SAMPLE," in line:
            fields = line[line.index("NATIVE_CHAIN_SAMPLE,"):].split(",")
            sample = dict(scene=fields[1], width=int(fields[2]), height=int(fields[3]),
                          elapsed=float(fields[4]), warmup_batches=int(fields[5]),
                          measured_batches=int(fields[6]), mean_batch_us=float(fields[7]),
                          process_cpu_ticks=int(fields[8]), timed_major_faults=int(fields[9]),
                          process_peak_rss_kib=int(fields[10].split()[0]),
                          batches_us=[float(value) for value in fields[11].split(";")])
    if sample is None or len(snapshots) != sample["warmup_batches"] + sample["measured_batches"] + 2:
        raise RuntimeError("Incomplete native sample or canonical snapshot transcript")
    sample["snapshots"] = snapshots
    return sample


def run(args, label, case, name, batches):
    width, height, scene, elapsed = case
    executable = getattr(args, label)
    env = dict(os.environ, CYCLETRIM_ONISIM_GPU="0", CYCLETRIM_ONISIM_WORKERS="1",
               ONISIM_CHAIN_SCENE=scene, ONISIM_CHAIN_WIDTH=str(width),
               ONISIM_CHAIN_HEIGHT=str(height), ONISIM_CHAIN_ELAPSED=elapsed,
               ONISIM_CHAIN_WARMUP="2", ONISIM_CHAIN_BATCHES=str(batches))
    env.pop("CYCLETRIM_GPU_PROFILE", None)
    start = time.time()
    log = args.output / (name + ".log")
    try:
        result = subprocess.run([str(executable), "--exact", TEST, "--ignored", "--nocapture", "--test-threads=1"],
                                env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                text=True, timeout=120)
    except subprocess.TimeoutExpired as error:
        partial = error.stdout or ""
        if isinstance(partial, bytes):
            partial = partial.decode("utf-8", errors="replace")
        log.write_text(partial)
        raise
    log.write_text(result.stdout)
    if result.returncode:
        raise RuntimeError(f"{name} failed with {result.returncode}; see {log}")
    row = parse(result.stdout)
    expected = (scene, width, height, float(elapsed), 2, batches)
    actual = tuple(row[key] for key in ("scene", "width", "height", "elapsed",
                                        "warmup_batches", "measured_batches"))
    if actual != expected or len(row["batches_us"]) != batches:
        raise RuntimeError(f"{name} reported a different workload: {actual} != {expected}; see {log}")
    row.update(label=label, case=case_id(case), unix_start=start, log=str(log),
               process_duration_seconds=time.time() - start)
    return row


def compare(expected, actual):
    if expected != actual:
        for first, second in zip(expected, actual):
            if first != second:
                raise RuntimeError(f"Canonical publication/world mismatch: {first} != {second}")
        raise RuntimeError("Snapshot transcript lengths differ")


def describe(values):
    ordered = sorted(values)
    return dict(samples=len(values), median=statistics.median(values),
                p95=ordered[math.ceil(len(values) * .95) - 1], minimum=ordered[0], maximum=ordered[-1],
                standard_deviation=statistics.stdev(values) if len(values) > 1 else 0.0,
                raw=values)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--baseline-source", type=Path, required=True)
    parser.add_argument("--candidate-source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mode", choices=("correctness", "benchmark", "smoke"), required=True)
    parser.add_argument("--candidate-scope", choices=("combined", "retained_idle"), default="combined")
    parser.add_argument("--cpu", type=int, default=0)
    parser.add_argument("--case", action="append", help="Filter smoke/correctness; never accepted as full benchmark")
    args = parser.parse_args()
    for name in ("baseline", "candidate", "baseline_source", "candidate_source", "output"):
        setattr(args, name, getattr(args, name).resolve())
    args.output.mkdir(parents=True, exist_ok=False)
    if args.mode == "benchmark" and args.case:
        raise ValueError("The accepted performance matrix cannot omit fixed cases")
    allowed = os.sched_getaffinity(0)
    if args.cpu not in allowed:
        raise ValueError(f"CPU {args.cpu} outside allowed affinity {sorted(allowed)}")
    os.sched_setaffinity(0, {args.cpu})
    cases = [case for case in CASES if not args.case or case_id(case) in args.case]
    if not cases:
        raise ValueError("No matching cases")
    baseline_identity = source_identity(args.baseline_source)
    candidate_identity = source_identity(args.candidate_source)
    if not baseline_identity["head"].startswith("f40858e"):
        raise ValueError("Baseline must be fixed to f40858e")
    if args.candidate_scope == "retained_idle":
        for filename in ("fluid_pressure.rs", "simulation_step_fluid.rs"):
            path = "mods/CycleTrim/OniSim/native/src/" + filename
            if candidate_identity["files_sha256"][path] != baseline_identity["files_sha256"][path]:
                raise ValueError("retained_idle requires the rejected liquid-scan candidate to be removed: " + path)
    plan = Path(__file__).with_name("plan-native-chain.md")
    inputs = dict(mode=args.mode, candidate_scope=args.candidate_scope,
                  baseline=baseline_identity, candidate=candidate_identity,
                  executables_sha256={label: digest(getattr(args, label)) for label in ("baseline", "candidate")},
                  driver_sha256=digest(Path(__file__)), plan_sha256=digest(plan),
                  cpu=args.cpu, workers=1, cpu_clock_ticks_per_second=os.sysconf("SC_CLK_TCK"),
                  cases=[case_id(case) for case in cases], elapsed_units="microseconds per completed native batch",
                  samples_per_side=34 if args.mode == "benchmark" else 1,
                  wall_boundary="submission + actual worker release/wait + Engine batch + State.finish_prepare stable Grid copy",
                  wall_excludes="worker handle lookup, NewFrame/regions clone/visibility allocation before the clock; stable-address assertions after the clock; fixture, warmup, observers and shutdown",
                  observer_scope="all canonical snapshots and synchronous sha256sum calls are outside wall timing; peak RSS includes snapshot allocation",
                  process_cpu_counter_scope="before/after advance: includes frame/visibility preparation, handle lookup, stable-address assertions and counter-read overhead in addition to wall interval; coarse SC_CLK_TCK units; diagnostic only",
                  p95_estimator="nearest-rank: sorted_values[ceil(0.95 * count) - 1]",
                  gate_limits_percent={case_id(case): gate_limit(case_id(case), args.candidate_scope) for case in cases},
                  thresholds=dict(primary_pure_736x304_reduction_percent=5 if args.candidate_scope == "combined" else None,
                                  primary_alternating_idle_736x304_reduction_percent=10,
                                  maximum_regression_percent=3),
                  runtime=dict(platform=os.uname()._asdict() if hasattr(os.uname(), "_asdict") else list(os.uname()),
                               python=os.sys.version, initial_load_average=os.getloadavg()))
    save(args.output / "inputs.json", inputs)
    rows, references = [], {}
    try:
        if args.mode != "benchmark":
            for case in cases:
                name = case_id(case)
                pair = []
                for label in ("baseline", "candidate"):
                    row = run(args, label, case, f"{name}-{label}", 20 if args.mode == "correctness" else 1)
                    pair.append(row)
                    rows.append(row)
                compare(pair[0]["snapshots"], pair[1]["snapshots"])
                print(f"MATCH {name}: {len(pair[0]['snapshots'])} complete publications and world snapshots", flush=True)
                save(args.output / "samples.json", rows)
        else:
            for block in range(17):
                order = ("baseline", "candidate", "candidate", "baseline") if block % 2 == 0 else (
                    "candidate", "baseline", "baseline", "candidate")
                for case in cases:
                    name = case_id(case)
                    for attempt in range(3):
                        paired = []
                        for position, label in enumerate(order):
                            row = run(args, label, case, f"b{block:02}-{name}-a{attempt}-{position}-{label}", 4)
                            if name not in references:
                                if label != "baseline":
                                    raise RuntimeError("The first accepted trajectory must be a baseline sample")
                                references[name] = row["snapshots"]
                            compare(references[name], row["snapshots"])
                            row.update(block=block, attempt=attempt, position=position)
                            paired.append(row)
                        valid = not any(row["timed_major_faults"] for row in paired)
                        for row in paired:
                            row["accepted_sample"] = valid
                        rows.extend(paired)
                        save(args.output / "samples.json", rows)
                        print(f"BLOCK {block + 1}/17 {name} attempt={attempt} valid={valid}", flush=True)
                        if valid:
                            break
                    else:
                        raise RuntimeError(f"Repeated major page faults invalidate block {block}/{name}")
        if source_identity(args.baseline_source) != baseline_identity or source_identity(args.candidate_source) != candidate_identity:
            raise RuntimeError("A measured source tree changed during validation")
        if any(digest(getattr(args, label)) != inputs["executables_sha256"][label] for label in ("baseline", "candidate")):
            raise RuntimeError("A measured executable changed during validation")
        summary = dict(mode=args.mode, candidate_scope=args.candidate_scope,
                       output_equality="all recorded canonical byte-image SHA-256 values match",
                       performance_accepted=False, cases=[], raw_rows=len(rows))
        if args.mode == "benchmark":
            gates = []
            original_gates, original_controls, alternating_gates = [], [], []
            for case in cases:
                name = case_id(case)
                series = {label: [row["mean_batch_us"] for row in rows if row["case"] == name
                                  and row["label"] == label and row["accepted_sample"]]
                          for label in ("baseline", "candidate")}
                assert all(len(values) == 34 for values in series.values())
                stats = {label: describe(values) for label, values in series.items()}
                change = (stats["candidate"]["median"] / stats["baseline"]["median"] - 1) * 100
                limit = gate_limit(name, args.candidate_scope)
                passed = change <= limit
                gates.append(passed)
                alternating = case[2] == "alternating_idle"
                (alternating_gates if alternating else original_gates).append(passed)
                if not alternating and (args.candidate_scope == "retained_idle" or name != "736x304_pure_dt0.2"):
                    original_controls.append(passed)
                summary["cases"].append(dict(case=name, wall_us=stats, change_percent=change,
                                             maximum_change_percent=limit, gate_passed=passed,
                                             group="alternating_idle" if alternating else "original_16"))
            summary["liquid_absence_accepted"] = all(original_gates) if args.candidate_scope == "combined" else None
            summary["alternating_idle_accepted"] = all(alternating_gates) and all(original_controls)
            summary["performance_accepted"] = all(gates)
        save(args.output / "summary.json", summary)
        print(json.dumps(summary, indent=2), flush=True)
    except BaseException as error:
        save(args.output / "samples.json", rows)
        save(args.output / "failed.json", dict(error_type=type(error).__name__, message=str(error), rows=len(rows)))
        raise


if __name__ == "__main__":
    main()
