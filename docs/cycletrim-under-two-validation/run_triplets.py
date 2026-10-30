#!/usr/bin/env python3
"""Compare fixed 620, 09 and candidate assemblies in adjacent rotated triplets."""

import argparse
from collections import defaultdict
from datetime import datetime, timezone
import hashlib
import importlib.util
from itertools import permutations
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess


HERE = Path(__file__).resolve().parent
PRIOR_RUNNER = HERE.parent / "cycletrim-regression-recovery-validation/integrated/run_benchmarks.py"
SPEC = importlib.util.spec_from_file_location("retained_pair_protocol", PRIOR_RUNNER)
PROTOCOL = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROTOCOL)
SCENARIOS = PROTOCOL.SCENARIOS
REVISIONS = ("anchor", "prior", "candidate")
ORDERS = tuple(permutations(REVISIONS))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def distribution(values):
    return {"min": min(values), "median": statistics.median(values), "max": max(values)}


def ratio_distribution(values):
    return {**distribution(values), "above_one": sum(x > 1 for x in values),
            "at_least_two": sum(x >= 2 for x in values)}


def validate_triplet(values):
    if set(values) != set(REVISIONS):
        raise ValueError("missing or unexpected revision in triplet")
    for variant in ("original", "patched"):
        for revision in REVISIONS:
            duration = values[revision][variant]["milliseconds"]
            if not math.isfinite(duration) or duration <= 0:
                raise ValueError("nonpositive or nonfinite timing")
        expected = {k: v for k, v in values["anchor"][variant].items()
                    if k not in {"milliseconds", "allocated_bytes"}}
        for revision in ("prior", "candidate"):
            observed = {k: v for k, v in values[revision][variant].items()
                        if k not in {"milliseconds", "allocated_bytes"}}
            if expected != observed:
                raise ValueError("cross-revision semantic mismatch: " + revision + "/" + variant)


def summarize(directory):
    directory = Path(directory)
    metadata = json.loads((directory / "run.json").read_text())
    expected = {(family, scenario, mode) for family in metadata["families"]
                for scenario, mode in SCENARIOS[family]}
    grouped = defaultdict(lambda: defaultdict(dict))
    expected_sequence = [(family, scenario, mode, sample, position, revision)
                         for family in metadata["families"]
                         for scenario, mode in SCENARIOS[family]
                         for sample in range(metadata["sets"])
                         for position, revision in enumerate(ORDERS[sample % len(ORDERS)])]
    lines = (directory / "samples.jsonl").read_text().splitlines()
    if len(lines) != len(expected_sequence):
        raise ValueError("missing or extra process records")
    for sequence, line in enumerate(lines):
        value = json.loads(line)
        key = (value["family"], value["scenario"], value["mode"])
        sample = value["set"]
        revision = value["revision"]
        position = value["process_position"]
        if type(position) is not int or not 0 <= position < len(REVISIONS):
            raise ValueError("invalid process position")
        if (*key, sample, position, revision) != expected_sequence[sequence]:
            raise ValueError("process record sequence differs from protocol")
        if revision in grouped[key][sample]:
            raise ValueError("duplicate revision/set")
        order = ORDERS[sample % len(ORDERS)]
        if revision not in REVISIONS or order[value["process_position"]] != revision:
            raise ValueError("process order differs from predeclared rotation")
        grouped[key][sample][revision] = value
    if set(grouped) != expected:
        raise ValueError("missing or unexpected scenario/mode")
    result = []
    for (family, scenario, mode), sets in grouped.items():
        if set(sets) != set(range(metadata["sets"])):
            raise ValueError("missing or unexpected triplet indices")
        ordered = [sets[index] for index in range(metadata["sets"])]
        for values in ordered:
            validate_triplet(values)
        row = {"family": family, "scenario": scenario, "mode": mode, "sets": len(ordered)}
        for revision in REVISIONS:
            for variant in ("original", "patched"):
                values = [item[revision][variant] for item in ordered]
                row[revision + "_" + variant + "_ms"] = distribution([v["milliseconds"] for v in values])
                row[revision + "_" + variant + "_allocated_bytes"] = distribution([v["allocated_bytes"] for v in values])
            row[revision + "_original_to_patched_ratio"] = ratio_distribution([
                item[revision]["original"]["milliseconds"] / item[revision]["patched"]["milliseconds"]
                for item in ordered])
        for baseline in ("anchor", "prior"):
            row[baseline + "_to_candidate_ratio"] = ratio_distribution([
                item[baseline]["patched"]["milliseconds"] / item["candidate"]["patched"]["milliseconds"]
                for item in ordered])
        result.append(row)
    (directory / "summary.json").write_text(json.dumps(result, separators=(",", ":")) + "\n")
    return result


def run(args):
    directory = args.output.resolve()
    directory.mkdir(parents=True, exist_ok=False)
    assemblies = {name: getattr(args, name).resolve() for name in REVISIONS}
    hashes = {name: digest(path) for name, path in assemblies.items()}
    families = args.family or list(SCENARIOS)
    if len(set(families)) != len(families):
        raise ValueError("duplicate family")
    environment = os.environ.copy()
    environment.pop("COMPlus_TieredCompilation", None)
    environment["DOTNET_TieredCompilation"] = "0"
    metadata = {
        "label": args.label, "started_utc": datetime.now(timezone.utc).isoformat(),
        "platform": platform.platform(), "cpu": args.cpu,
        "allowed_cpu_affinity": sorted(os.sched_getaffinity(0)), "sets": args.sets,
        "families": families, "assembly_sha256": hashes, "orders": ORDERS,
        "revision_commits": {"anchor": "62067913a5aea4daa4a8991689c86aad261b1269",
                             "prior": "09c496c46b4879cbdce4a8a9056b3f85f9c5c0d3"},
        "driver_sha256": digest(args.driver),
        "driver_source_sha256": digest(HERE / "BenchmarkDriver/Program.cs"),
        "runner_source_sha256": digest(__file__), "retained_parser_sha256": digest(PRIOR_RUNNER),
        "runtime_environment": {key: environment.get(key, "runtime-default") for key in (
            "DOTNET_TieredCompilation", "COMPlus_TieredCompilation", "DOTNET_TieredPGO", "COMPlus_TieredPGO")},
        "protocol": "one original/patched pair per process; three adjacent revisions with all six order permutations",
    }
    if args.cpu not in metadata["allowed_cpu_affinity"]:
        raise ValueError("requested CPU unavailable")
    (directory / "run.json").write_text(json.dumps(metadata, indent=2) + "\n")
    with (directory / "samples.jsonl").open("w", buffering=1) as samples:
        for family in families:
            for scenario, mode in SCENARIOS[family]:
                for sample in range(args.sets):
                    values = {}
                    for position, revision in enumerate(ORDERS[sample % len(ORDERS)]):
                        command = ["taskset", "-c", str(args.cpu), args.dotnet, str(args.driver.resolve()),
                                   str(assemblies[revision]), family, scenario, mode, str(sample)]
                        process = subprocess.run(command, env=environment, text=True,
                                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
                        if process.returncode:
                            (directory / "failed-process.log").write_text(process.stdout)
                            raise RuntimeError(f"{revision}/{family}/{scenario}/{mode}/{sample} exited {process.returncode}")
                        measured = PROTOCOL.parse_samples(process.stdout, family, scenario, mode, sample, hashes[revision])
                        if family == "animation" and mode == "setter":
                            if any(v["copies"] or v["copied_bytes"] for v in measured.values()):
                                raise ValueError("setter-only animation unexpectedly copied data")
                        values[revision] = measured
                        samples.write(json.dumps({"revision": revision, "family": family,
                            "scenario": scenario, "mode": mode, "set": sample,
                            "process_position": position, **measured}, separators=(",", ":")) + "\n")
                    validate_triplet(values)
                print(f"completed {family}/{scenario}/{mode}", flush=True)
    summarize(directory)
    metadata["completed_utc"] = datetime.now(timezone.utc).isoformat()
    (directory / "run.json").write_text(json.dumps(metadata, indent=2) + "\n")
    print(f"saved complete triplets: {directory}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    execute = commands.add_parser("run")
    for name in ("driver", "anchor", "prior", "candidate", "output"):
        execute.add_argument("--" + name, required=True, type=Path)
    execute.add_argument("--dotnet", default="dotnet")
    execute.add_argument("--cpu", default=0, type=int)
    execute.add_argument("--sets", default=7, type=int, choices=range(1, 8))
    execute.add_argument("--family", action="append", choices=SCENARIOS)
    execute.add_argument("--label", required=True)
    summary = commands.add_parser("summarize")
    summary.add_argument("directory", type=Path)
    args = parser.parse_args()
    if args.command == "run":
        run(args)
    else:
        for row in summarize(args.directory):
            print(row["family"], row["scenario"], row["mode"], row["anchor_to_candidate_ratio"])


if __name__ == "__main__":
    main()
