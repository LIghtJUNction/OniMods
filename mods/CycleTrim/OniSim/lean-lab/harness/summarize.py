#!/usr/bin/env python3
"""Validate raw measurement completeness and report paired component statistics."""

from __future__ import annotations

from collections import Counter, defaultdict
import csv
import json
from pathlib import Path
import statistics


LAB = Path(__file__).resolve().parents[1]
RESULTS = LAB / "results"
INTEGER_FIELDS = {
    "width", "height", "stride", "padded_height", "max_depth", "radius", "emitters",
    "visited_cells", "pair", "iterations", "elapsed_ns", "alloc_calls", "realloc_calls",
    "total_requested_bytes", "peak_live_bytes", "live_bytes_at_end",
}


def load_csv(path: Path) -> list[dict]:
    with path.open(newline="") as source:
        records = list(csv.DictReader(source))
    for record in records:
        for key, value in list(record.items()):
            if key in INTEGER_FIELDS:
                record[key] = int(value)
            elif key == "ns_per_call":
                record[key] = float(value)
    return records


def quantile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    location = fraction * (len(values) - 1)
    low = int(location)
    high = min(low + 1, len(values) - 1)
    return ordered[low] + (ordered[high] - ordered[low]) * (location - low)


def distribution(values: list[float]) -> dict:
    median = statistics.median(values)
    return {
        "n": len(values),
        "median": median,
        "minimum": min(values),
        "maximum": max(values),
        "q25": quantile(values, 0.25),
        "q75": quantile(values, 0.75),
        "median_absolute_deviation": statistics.median(abs(value - median) for value in values),
        "sample_standard_deviation": statistics.stdev(values) if len(values) > 1 else 0.0,
    }


def main() -> None:
    records = load_csv(RESULTS / "raw-samples.csv")
    measured = [record for record in records if record["phase"] == "measured"]
    by_case = defaultdict(list)
    for record in measured:
        if record["elapsed_ns"] < 10_000_000:
            raise ValueError("measured block shorter than the preset minimum")
        by_case[record["case_id"]].append(record)
    if len(by_case) != 60:
        raise ValueError(f"expected 60 cases, found {len(by_case)}")
    summaries = []
    for case_id, rows in by_case.items():
        if len(rows) != 16:
            raise ValueError(f"{case_id}: expected 16 measured blocks")
        first = rows[0]
        pairs = {}
        for pair in range(8):
            pair_rows = [row for row in rows if row["pair"] == pair]
            if len(pair_rows) != 2 or {row["algorithm"] for row in pair_rows} != {"baseline", "candidate"}:
                raise ValueError(f"{case_id}: incomplete pair {pair}")
            expected_order = "AB" if pair % 2 == 0 else "BA"
            if any(row["order"] != expected_order for row in pair_rows):
                raise ValueError(f"{case_id}: wrong paired order")
            pairs[pair] = {row["algorithm"]: row["elapsed_ns"] / row["iterations"] for row in pair_rows}
        old = [pairs[pair]["baseline"] for pair in range(8)]
        new = [pairs[pair]["candidate"] for pair in range(8)]
        ratios = [a / b for a, b in zip(old, new)]
        reductions = [100 * (1 - b / a) for a, b in zip(old, new)]
        summaries.append({
            **{key: first[key] for key in ("case_id", "kind", "width", "height", "stride", "padded_height", "max_depth", "radius", "emitters", "visited_cells")},
            "baseline_ns_per_call": distribution(old),
            "candidate_ns_per_call": distribution(new),
            "paired_speedup_baseline_over_candidate": distribution(ratios),
            "paired_time_reduction_percent": distribution(reductions),
            "ratio_of_medians": statistics.median(old) / statistics.median(new),
            "paired_ratios": ratios,
        })
    common = [row for row in summaries if row["max_depth"] in (1, 4, 16)]
    regressions = [row["case_id"] for row in summaries if row["paired_speedup_baseline_over_candidate"]["median"] < 1]
    failed_common = [row["case_id"] for row in common if row["paired_time_reduction_percent"]["median"] <= 5]
    correctness = json.loads((RESULTS / "correctness.json").read_text())
    environment = json.loads((RESULTS / "environment.json").read_text())
    if environment.get("source_unchanged_during_timing") is not True:
        raise ValueError("source stability was not established for the timing run")
    allocations_path = RESULTS / "allocations.csv"
    allocation_rows = load_csv(allocations_path) if allocations_path.exists() else []
    allocation_lookup = {(row["case_id"], row["algorithm"]): row for row in allocation_rows}
    for summary in summaries:
        if allocation_rows:
            summary["allocations_per_call"] = {
                algorithm: allocation_lookup[(summary["case_id"], algorithm)]
                for algorithm in ("baseline", "candidate")
            }
    report = {
        "scope": "Actual disease emitter Rust component with real disease arithmetic; minimal world adapter; no game FPS claim",
        "timing_unit": "nanoseconds per complete search or update call",
        "paired_ratio_definition": "For each pair: baseline ns/call divided by candidate ns/call; summarize the eight ratios",
        "ratio_of_medians_definition": "Median baseline ns/call divided by median candidate ns/call; distinct from median paired ratio",
        "raw_phase_counts": dict(Counter(row["phase"] for row in records)),
        "measured_blocks": len(measured),
        "case_count": len(summaries),
        "pairs_per_case": 8,
        "gates": {
            "correctness_passed": correctness["status"] == "passed",
            "correctness_cases": correctness["total_cases"],
            "common_depth_cases": len(common),
            "common_depth_median_paired_time_reduction_above_5_percent": not failed_common,
            "failed_common_cases": failed_common,
            "deeper_or_common_regressions": regressions,
            "source_unchanged_during_timing": environment["source_unchanged_during_timing"],
            "candidate_zero_allocations_all_cases": (
                all(row["alloc_calls"] == 0 and row["realloc_calls"] == 0 for row in allocation_rows if row["algorithm"] == "candidate")
                if allocation_rows else None
            ),
        },
        "cases": summaries,
    }
    (RESULTS / "raw-samples.json").write_text(json.dumps(records, indent=2) + "\n")
    (RESULTS / "allocations.json").write_text(json.dumps(allocation_rows, indent=2) + "\n")
    (RESULTS / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    fields = ["case_id", "kind", "width", "height", "max_depth", "radius", "emitters", "visited_cells", "baseline_median_ns", "candidate_median_ns", "baseline_mad_ns", "candidate_mad_ns", "paired_speedup_median", "paired_speedup_q25", "paired_speedup_q75", "paired_speedup_min", "paired_speedup_max", "ratio_of_medians", "paired_time_reduction_median_percent"]
    with (RESULTS / "summary.csv").open("w", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=fields)
        writer.writeheader()
        for row in summaries:
            paired = row["paired_speedup_baseline_over_candidate"]
            writer.writerow({
                **{key: row[key] for key in fields[:8]},
                "baseline_median_ns": row["baseline_ns_per_call"]["median"],
                "candidate_median_ns": row["candidate_ns_per_call"]["median"],
                "baseline_mad_ns": row["baseline_ns_per_call"]["median_absolute_deviation"],
                "candidate_mad_ns": row["candidate_ns_per_call"]["median_absolute_deviation"],
                "paired_speedup_median": paired["median"],
                "paired_speedup_q25": paired["q25"],
                "paired_speedup_q75": paired["q75"],
                "paired_speedup_min": paired["minimum"],
                "paired_speedup_max": paired["maximum"],
                "ratio_of_medians": row["ratio_of_medians"],
                "paired_time_reduction_median_percent": row["paired_time_reduction_percent"]["median"],
            })
    print(json.dumps({key: value for key, value in report.items() if key != "cases"}, indent=2))
    for row in summaries:
        if row["width"] == 1024 and (row["kind"] == "search" or row["emitters"] == 8):
            print(row["case_id"], "K=", row["visited_cells"],
                  "baseline_us=", round(row["baseline_ns_per_call"]["median"] / 1000, 6),
                  "candidate_us=", round(row["candidate_ns_per_call"]["median"] / 1000, 6),
                  "median_paired_speedup=", round(row["paired_speedup_baseline_over_candidate"]["median"], 3),
                  "ratio_of_medians=", round(row["ratio_of_medians"], 3))


if __name__ == "__main__":
    main()
