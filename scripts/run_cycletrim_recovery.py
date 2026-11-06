#!/usr/bin/env python3
"""Run adjacent old/new host processes; keep timing evidence separate from correctness."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess

PAIRS = 7
SCENARIOS = {
    "power": {"repeated-circuits", "all-distinct-circuits"},
    "conveyor": {"long-misses", "shared-rails", "early-hits", "single-query", "short-paths", "contained-path"},
    "animation": {name + "/" + mode for name in (
        "repeat-single", "change-single", "repeat-range4", "repeat-range256",
        "change-range4", "change-range256", "change-last-range256") for mode in ("setter", "consumer")},
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def dist(values):
    return {"median": statistics.median(values), "min": min(values), "max": max(values)}


def parse_samples(text, family):
    groups = {}
    if family == "animation":
        records = {}
        for line in text.splitlines():
            if not line.startswith("BENCH animation-symbols "):
                continue
            row = dict(part.split("=", 1) for part in line.split()[2:])
            key = row["scenario"] + "/" + row["mode"]
            pair = int(row["sample"])
            variant = row["variant"]
            require(0 <= pair < PAIRS and variant in ("baseline", "candidate"), "invalid animation sample")
            bucket = records.setdefault((key, pair), {})
            require(variant not in bucket, "duplicate animation sample")
            bucket[variant] = row
        for (key, pair), variants in records.items():
            require(set(variants) == {"baseline", "candidate"}, "missing animation variant")
            before, after = variants["baseline"], variants["candidate"]
            require(before["checksum"] == after["checksum"], "animation payload mismatch")
            row = {"pair": pair + 1, "checksum": int(before["checksum"])}
            for variant, sample in variants.items():
                for field in ("symbols", "range", "updates", "settersPerUpdate", "frequency"):
                    require(before[field] == after[field], "animation workload/timer mismatch")
                require(int(sample["frequency"]) > 0, "invalid timer frequency")
                row[variant + "_ms"] = int(sample["ticks"]) * 1000 / int(sample["frequency"])
                row[variant + "_allocated_bytes"] = int(sample["allocated"])
                row[variant + "_copies"] = int(sample["copies"])
                row[variant + "_copied_bytes"] = int(sample["copiedBytes"])
                expected = 30000 if (sample["mode"] == "consumer" and
                    (variant == "baseline" or sample["scenario"].startswith("change"))) else 0
                require(int(sample["copies"]) == expected and int(sample["copiedBytes"]) == expected * 12288,
                        "animation copy count mismatch")
            groups.setdefault(key, []).append(row)
    else:
        prefix = family.upper() + "_BENCH_SAMPLE "
        for line in text.splitlines():
            if line.startswith(prefix):
                row = json.loads(line[len(prefix):])
                groups.setdefault(row["scenario"], []).append(row)
    require(set(groups) == SCENARIOS[family], "missing or unexpected " + family + " scenarios")
    result = {}
    for name, rows in groups.items():
        rows.sort(key=lambda row: row["pair"])
        require([row["pair"] for row in rows] == list(range(1, PAIRS + 1)), "missing/duplicate pairs")
        require(len({row["checksum"] for row in rows}) == 1, "unstable deterministic output")
        for row in rows:
            for variant in ("baseline", "candidate"):
                value = row[variant + "_ms"]
                require(math.isfinite(value) and value > 0, "invalid timing")
                require(row[variant + "_allocated_bytes"] >= 0, "invalid allocation")
            if family == "power":
                calls = 3200 if name == "repeated-circuits" else 51200
                require(row["baseline_query_calls"] == 51200 and row["candidate_query_calls"] == calls,
                        "power query count mismatch")
            if family == "conveyor":
                require(row["baseline_direction_reads"] == row["candidate_direction_reads"],
                        "conveyor callback mismatch")
        signature_keys = {"checksum"} | {key for key in rows[0] if key.endswith(
            ("_query_calls", "_direction_reads", "_copies", "_copied_bytes"))}
        result[name] = {
            "patched_ms": statistics.median(row["candidate_ms"] for row in rows),
            "patched_allocated_bytes": statistics.median(row["candidate_allocated_bytes"] for row in rows),
            "original_over_patched": statistics.median(row["baseline_ms"] / row["candidate_ms"] for row in rows),
            "signature": {key: rows[0][key] for key in sorted(signature_keys)},
        }
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--baseline-sha", required=True)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    binaries = {"baseline": args.baseline.resolve(), "candidate": args.candidate.resolve()}
    require(all(path.is_file() for path in binaries.values()), "missing host binary")
    affinity = sorted(os.sched_getaffinity(0))
    os.sched_setaffinity(0, {affinity[0]})
    metadata = {
        "baseline_sha": args.baseline_sha, "candidate_sha": args.candidate_sha,
        "platform": platform.platform(), "allowed_cpus": affinity, "pinned_cpu": affinity[0],
        "dotnet_info": subprocess.check_output(["dotnet", "--info"], text=True),
        "binary_sha256": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in binaries.items()},
        "outer_pairs": PAIRS,
        "note": "Seven adjacent alternating old/new process pairs. Each process retains all seven built-in pairs. "
                "Each process contributes one median, not seven independent cross-version observations. "
                "Shared CI host, not exclusive CPU; not Unity/Mono or game FPS.",
    }
    (args.output / "environment.json").write_text(json.dumps(metadata, indent=2) + "\n")
    measurements = {}
    for mode, families in (("controlled", ("power", "conveyor", "animation")), ("default", ("power",))):
        env = os.environ.copy()
        env.pop("DOTNET_TieredCompilation", None)
        env.pop("COMPlus_TieredCompilation", None)
        if mode == "controlled":
            env["DOTNET_TieredCompilation"] = "0"
        for family in families:
            for pair in range(PAIRS):
                order = ("baseline", "candidate") if pair % 2 == 0 else ("candidate", "baseline")
                for variant in order:
                    name = f"{mode}-{family}-{pair:02d}-{variant}.log"
                    completed = subprocess.run(["dotnet", str(binaries[variant]), "--benchmark-" + family],
                        text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env, timeout=180)
                    (args.output / name).write_text(completed.stdout)
                    require(completed.returncode == 0, f"{name}: benchmark exit {completed.returncode}")
                    parsed = parse_samples(completed.stdout, family)
                    for scenario, row in parsed.items():
                        key = mode + "/" + family + "/" + scenario
                        measurements.setdefault(key, {}).setdefault(pair, {})[variant] = row
                    print(f"RECOVERY_PROCESS {name} validated", flush=True)
    summary = {"environment": metadata, "cases": {}}
    for case, pairs in measurements.items():
        ratios, originals, before_alloc, after_alloc = [], [], [], []
        for pair, variants in sorted(pairs.items()):
            before, after = variants["baseline"], variants["candidate"]
            require(before["signature"] == after["signature"], case + ": cross-version semantic mismatch")
            ratios.append(before["patched_ms"] / after["patched_ms"])
            originals.append(after["original_over_patched"])
            before_alloc.append(before["patched_allocated_bytes"])
            after_alloc.append(after["patched_allocated_bytes"])
        require(len(ratios) == PAIRS, "incomplete outer comparison")
        entry = {"old_over_new": dist(ratios), "original_over_new": dist(originals),
                 "improved_pairs": sum(value > 1 for value in ratios), "pairs": PAIRS,
                 "old_allocated_bytes": dist(before_alloc), "new_allocated_bytes": dist(after_alloc)}
        summary["cases"][case] = entry
        print("RECOVERY_RESULT " + json.dumps({"case": case, **entry}), flush=True)
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2, allow_nan=False) + "\n")
    (args.output / "process-medians.json").write_text(json.dumps(measurements, indent=2, allow_nan=False) + "\n")


if __name__ == "__main__":
    main()
