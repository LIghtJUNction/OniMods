#!/usr/bin/env python3
"""Validate every fixed-matrix sample and summarize paired elapsed-time ratios."""

import argparse
from collections import defaultdict
import json
from pathlib import Path
from statistics import median


SCENARIOS = {
    "save-scoped": ("repeated", "mixed", "skip-heavy", "empty", "single"),
    "state-machine": ("tail-hit-64", "miss-64", "head-hit-64", "short-hit-4",
                      "mutation-every-query", "cold-owner-every-query", "capacity-17"),
}


def read_samples(path, benchmark):
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if benchmark == "state-machine" and line.startswith("BENCH state-machine-sample "):
            row = json.loads(line.removeprefix("BENCH state-machine-sample "))
            row["allocated"] = row["allocatedBytes"]
            row["operations"] = row["iterations"]
            rows.append(row)
        elif benchmark == "save-scoped" and line.startswith("BENCH save-scoped scenario="):
            row = dict(item.split("=", 1) for item in line.split()[2:])
            row = {key: value if key in ("scenario", "variant") else int(value)
                   for key, value in row.items()}
            row["operations"] = row["saves"]
            rows.append(row)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("matrix", type=Path)
    parser.add_argument("--benchmark", choices=SCENARIOS, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    receipt = json.loads((args.matrix / "receipt.json").read_text(encoding="utf-8"))
    runs = receipt["runs"]
    assert len(runs) == 6
    assert {(run["mode"], run["process"]) for run in runs} == {
        (mode, trial) for mode in ("default", "tiering-off") for trial in (1, 2, 3)}
    results, total_rows, warm_rows = [], 0, 0
    for run in runs:
        assert run["exit_code"] == 0, run
        rows = read_samples(args.matrix / run["log"], args.benchmark)
        assert len(rows) == len(SCENARIOS[args.benchmark]) * 14
        total_rows += len(rows)
        offset = 0
        for scenario in SCENARIOS[args.benchmark]:
            samples = rows[offset:offset + 14]
            offset += 14
            ratios = []
            costs, allocations = defaultdict(list), defaultdict(list)
            for pair in range(7):
                ordered = samples[pair * 2:pair * 2 + 2]
                assert [row["variant"] for row in ordered] == (
                    ["baseline", "candidate"] if pair % 2 == 0 else ["candidate", "baseline"])
                assert all(row["scenario"] == scenario and row["sample"] == pair for row in ordered)
                values = {row["variant"]: row for row in ordered}
                baseline, candidate = values["baseline"], values["candidate"]
                equal = ["checksum", "operations", "frequency"]
                equal += ["components", "bytes"] if args.benchmark == "save-scoped" else ["count", "queries"]
                assert all(baseline[key] == candidate[key] for key in equal), (run, scenario, pair)
                assert baseline["ticks"] > 0 and candidate["ticks"] > 0
                ratios.append(baseline["ticks"] / candidate["ticks"])
                for variant, row in values.items():
                    if scenario != "cold-owner-every-query":
                        assert row["allocated"] == 0, (run, row)
                        warm_rows += 1
                    costs[variant].append(row["ticks"] * 1e9 / row["frequency"] / row["operations"])
                    allocations[variant].append(row["allocated"] / row["operations"])
            result = {
                "mode": run["mode"], "process": run["process"], "scenario": scenario,
                "median_ratio": median(ratios), "pair_ratios": ratios,
                "pair_range": [min(ratios), max(ratios)],
                "improved_pairs": sum(ratio > 1 for ratio in ratios),
            }
            for variant in ("baseline", "candidate"):
                result[variant + "_ns_per_operation"] = median(costs[variant])
                result[variant + "_bytes_per_operation_range"] = [min(allocations[variant]), max(allocations[variant])]
            results.append(result)

    aggregates = []
    for mode in ("default", "tiering-off"):
        for scenario in SCENARIOS[args.benchmark]:
            processes = [row for row in results if row["mode"] == mode and row["scenario"] == scenario]
            assert len(processes) == 3
            ratios = [ratio for row in processes for ratio in row["pair_ratios"]]
            process_medians = [row["median_ratio"] for row in processes]
            aggregate = {
                "mode": mode, "scenario": scenario,
                "median_of_process_medians": median(process_medians),
                "process_medians": process_medians,
                "pair_range": [min(ratios), max(ratios)],
                "improved_pairs": sum(ratio > 1 for ratio in ratios), "pairs": len(ratios),
            }
            for variant in ("baseline", "candidate"):
                aggregate[variant + "_ns_per_operation"] = median(row[variant + "_ns_per_operation"] for row in processes)
                spans = [row[variant + "_bytes_per_operation_range"] for row in processes]
                aggregate[variant + "_bytes_per_operation_range"] = [min(span[0] for span in spans), max(span[1] for span in spans)]
            aggregates.append(aggregate)
    revision = receipt.get("revision", receipt.get("commit"))
    assert revision
    output = {
        "benchmark": args.benchmark, "revision": revision,
        "method": "median within each process of seven paired baseline/candidate time ratios, then median of three process medians",
        "validated": {"processes": len(runs), "sample_windows": total_rows,
                      "paired_windows": total_rows // 2, "zero_allocation_warm_windows": warm_rows,
                      "equal_paired_checksums": True, "alternating_order": True},
        "aggregates": aggregates, "process_results": results,
    }
    args.output.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"validated": output["validated"], "aggregates": aggregates}, indent=2))


if __name__ == "__main__":
    main()
