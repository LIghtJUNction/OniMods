#!/usr/bin/env python3
"""Interleave two unchanged host benchmark assemblies and retain paired evidence."""

import argparse
from collections import defaultdict
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess


SCENARIOS = {
    "conveyor": [(name, "lookup") for name in (
        "long-misses", "shared-rails", "early-hits", "single-query",
        "short-paths", "contained-path")],
    "animation": [(name, mode) for name in (
        "repeat-single", "change-single", "repeat-range4", "repeat-range256")
        for mode in ("setter", "consumer")],
    "power": [(name, "query") for name in (
        "repeated-circuits", "all-distinct-circuits")],
}


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def parse_samples(text, family, scenario, mode, pair, expected_sha):
    configs = [json.loads(line[len("R3_CONFIG "):]) for line in text.splitlines()
               if line.startswith("R3_CONFIG ")]
    if len(configs) != 1 or any(configs[0][key] != value for key, value in (
        ("family", family), ("scenario", scenario), ("mode", mode),
        ("pair", pair), ("assembly_sha256", expected_sha))):
        raise ValueError("driver identity mismatch")
    rows = {}
    order = []
    for line in text.splitlines():
        if line.startswith("R3_SAMPLE "):
            value = json.loads(line[len("R3_SAMPLE "):])
            variant = value.pop("variant")
            if value.pop("pair") != pair:
                raise ValueError("sample pair mismatch")
        elif line.startswith("BENCH animation-symbols "):
            fields = dict(part.split("=", 1) for part in line.split()[2:])
            if fields["scenario"] != scenario or fields["mode"] != mode or int(fields["sample"]) != pair:
                raise ValueError("animation sample identity mismatch")
            variant = "original" if fields["variant"] == "baseline" else "patched"
            value = {
                "milliseconds": int(fields["ticks"]) * 1000 / int(fields["frequency"]),
                "allocated_bytes": int(fields["allocated"]),
                "checksum": int(fields["checksum"]),
                "copies": int(fields["copies"]), "copied_bytes": int(fields["copiedBytes"]),
            }
        else:
            continue
        if variant in rows or value["milliseconds"] <= 0:
            raise ValueError("duplicate or invalid timing sample")
        rows[variant] = value
        order.append(variant)
    expected_order = ["original", "patched"] if pair % 2 == 0 else ["patched", "original"]
    if order != expected_order or rows["original"]["checksum"] != rows["patched"]["checksum"]:
        raise ValueError("sample order or output mismatch")
    if family == "conveyor" and rows["original"]["counter"] != rows["patched"]["counter"]:
        raise ValueError("direction-read mismatch")
    if family == "power":
        expected = 3200 if scenario == "repeated-circuits" else 51200
        if rows["original"]["counter"] != 51200 or rows["patched"]["counter"] != expected:
            raise ValueError("power call-count mismatch")
    if family == "animation" and mode == "consumer":
        expected = 30000 if scenario == "change-single" else 0
        if rows["original"]["copies"] != 30000 or rows["patched"]["copies"] != expected:
            raise ValueError("animation copy-count mismatch")
        if rows["original"]["copied_bytes"] != 30000 * 256 * 48 or rows["patched"]["copied_bytes"] != expected * 256 * 48:
            raise ValueError("animation copied-byte mismatch")
    return rows


def distribution(values):
    return {"min": min(values), "median": statistics.median(values), "max": max(values)}


def validate_outer_pair(pair):
    if set(pair) != {"prior", "candidate"}:
        raise ValueError("incomplete outer pair")
    for variant in ("original", "patched"):
        semantic = {key: value for key, value in pair["prior"][variant].items()
                    if key not in {"milliseconds", "allocated_bytes"}}
        compared = {key: value for key, value in pair["candidate"][variant].items()
                    if key not in {"milliseconds", "allocated_bytes"}}
        if semantic != compared:
            raise ValueError("prior/candidate semantic mismatch: " + variant)


def summarize(directory):
    metadata = json.loads((directory / "run.json").read_text())
    expected = {(family, scenario, mode) for family in metadata["families"]
                for scenario, mode in SCENARIOS[family]}
    grouped = defaultdict(lambda: defaultdict(dict))
    for line in (directory / "samples.jsonl").read_text().splitlines():
        item = json.loads(line)
        key = (item["family"], item["scenario"], item["mode"])
        if item["revision"] in grouped[key][item["pair"]]:
            raise ValueError("duplicate revision/pair")
        grouped[key][item["pair"]][item["revision"]] = item
    if set(grouped) != expected:
        raise ValueError("missing or unexpected scenario/mode")
    result = []
    for (family, scenario, mode), pairs in grouped.items():
        if set(pairs) != set(range(metadata["pairs"])):
            raise ValueError("missing or unexpected pair indices")
        ordered = [pairs[index] for index in sorted(pairs)]
        for item in ordered:
            validate_outer_pair(item)
        row = {"family": family, "scenario": scenario, "mode": mode, "pairs": len(ordered)}
        for revision in ("prior", "candidate"):
            for variant in ("original", "patched"):
                values = [item[revision][variant] for item in ordered]
                row[f"{revision}_{variant}_ms"] = distribution([value["milliseconds"] for value in values])
                row[f"{revision}_{variant}_allocated_bytes"] = distribution([value["allocated_bytes"] for value in values])
            row[f"{revision}_original_to_patched_ratio"] = distribution([
                item[revision]["original"]["milliseconds"] / item[revision]["patched"]["milliseconds"] for item in ordered])
        row["prior_to_candidate_ratio"] = distribution([
            item["prior"]["patched"]["milliseconds"] / item["candidate"]["patched"]["milliseconds"] for item in ordered])
        result.append(row)
    (directory / "summary.json").write_text(json.dumps(result, separators=(",", ":")) + "\n")
    return result


def run(args):
    directory = args.output.resolve()
    directory.mkdir(parents=True, exist_ok=False)
    assemblies = {"prior": args.prior.resolve(), "candidate": args.candidate.resolve()}
    hashes = {name: digest(path) for name, path in assemblies.items()}
    families = args.family or list(SCENARIOS)
    if len(set(families)) != len(families):
        raise ValueError("duplicate family")
    metadata = {
        "label": args.label, "started_utc": datetime.now(timezone.utc).isoformat(),
        "platform": platform.platform(), "cpu": args.cpu,
        "allowed_cpu_affinity": sorted(os.sched_getaffinity(0)), "pairs": args.pairs,
        "families": families, "assembly_sha256": hashes,
        "driver_sha256": digest(args.driver), "driver_source_sha256": digest(Path(__file__).parent / "BenchmarkDriver/Program.cs"),
        "runner_source_sha256": digest(__file__),
        "runtime_environment": {key: os.environ.get(key, "runtime-default") for key in (
            "DOTNET_TieredCompilation", "COMPlus_TieredCompilation", "DOTNET_TieredPGO", "COMPlus_TieredPGO")},
        "protocol": "one original/patched pair per process; alternate prior/candidate processes per outer pair",
    }
    if args.cpu not in metadata["allowed_cpu_affinity"]:
        raise ValueError("requested CPU is not available")
    (directory / "run.json").write_text(json.dumps(metadata, indent=2) + "\n")
    with (directory / "samples.jsonl").open("w", buffering=1) as samples:
        for family in families:
            for scenario, mode in SCENARIOS[family]:
                for pair in range(args.pairs):
                    order = ("prior", "candidate") if pair % 2 == 0 else ("candidate", "prior")
                    paired_values = {}
                    for position, revision in enumerate(order):
                        command = ["taskset", "-c", str(args.cpu), args.dotnet, str(args.driver.resolve()),
                                   str(assemblies[revision]), family, scenario, mode, str(pair)]
                        process = subprocess.run(command, text=True, stdout=subprocess.PIPE,
                                                 stderr=subprocess.STDOUT, timeout=120)
                        if process.returncode:
                            (directory / "failed-process.log").write_text(process.stdout)
                            raise RuntimeError(f"{revision}/{family}/{scenario}/{mode}/{pair} exited {process.returncode}")
                        values = parse_samples(process.stdout, family, scenario, mode, pair, hashes[revision])
                        paired_values[revision] = values
                        samples.write(json.dumps({"revision": revision, "family": family, "scenario": scenario,
                            "mode": mode, "pair": pair, "process_position": position, **values}, separators=(",", ":")) + "\n")
                    validate_outer_pair(paired_values)
                print(f"completed {family}/{scenario}/{mode}", flush=True)
    summarize(directory)
    print(f"saved {directory}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    execute = commands.add_parser("run")
    for name in ("driver", "prior", "candidate", "output"):
        execute.add_argument("--" + name, required=True, type=Path)
    execute.add_argument("--dotnet", default="dotnet")
    execute.add_argument("--cpu", default=0, type=int)
    execute.add_argument("--pairs", default=7, type=int, choices=range(1, 8))
    execute.add_argument("--family", action="append", choices=SCENARIOS)
    execute.add_argument("--label", required=True)
    summary = commands.add_parser("summarize")
    summary.add_argument("directory", type=Path)
    args = parser.parse_args()
    if args.command == "run":
        run(args)
    else:
        for item in summarize(args.directory):
            print(item["family"], item["scenario"], item["mode"], item["prior_to_candidate_ratio"])


if __name__ == "__main__":
    main()
