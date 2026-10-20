#!/usr/bin/env python3
"""Validate paired raw timings and regenerate the full, unfiltered summary."""
import csv
import json
from pathlib import Path
import statistics

ROOT = Path(__file__).resolve().parent / "results"


def summarize():
    with (ROOT / "raw-samples.csv").open() as handle:
        rows = list(csv.DictReader(handle))
    if len(rows) != 640:
        raise ValueError(f"Expected 640 formal timing batches, got {len(rows)}")
    cases = {}
    for row in rows:
        case, pair = row["case"], int(row["pair"])
        variant, position = row["variant"], int(row["position"])
        if variant not in ("baseline", "candidate"):
            raise ValueError("Unexpected variant: " + variant)
        if position != ((variant == "candidate") ^ (pair % 2)):
            raise ValueError("AB/BA sample order differs: " + case)
        if int(row["total_ns"]) < 100_000_000 or int(row["iterations"]) < 3:
            raise ValueError("Timing batch was too short: " + case)
        members = cases.setdefault(case, {}).setdefault(pair, {})
        if variant in members:
            raise ValueError("Duplicate timed variant: " + case)
        members[variant] = int(row["total_ns"]) / int(row["iterations"])
    if len(cases) != 40:
        raise ValueError("Incomplete case matrix")
    summary = []
    for name, pairs in cases.items():
        if set(pairs) != set(range(8)) or any(set(p) != {"baseline", "candidate"} for p in pairs.values()):
            raise ValueError("Incomplete paired case: " + name)
        baseline = [pairs[n]["baseline"] for n in range(8)]
        candidate = [pairs[n]["candidate"] for n in range(8)]
        ratios = [a / b for a, b in zip(baseline, candidate)]
        summary.append({
            "case": name, "pairs": 8,
            "baseline_median_ns": statistics.median(baseline),
            "candidate_median_ns": statistics.median(candidate),
            "paired_speedup_median": statistics.median(ratios),
            "paired_speedup_min": min(ratios), "paired_speedup_max": max(ratios),
            "paired_speedups": ratios,
        })
    report = {"scope": "synthetic host components, not game FPS", "timed_batches": 640,
              "case_count": 40, "minimum_accumulated_batch_ns": 100_000_000, "cases": summary}
    (ROOT / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    fields = [key for key in summary[0] if key != "paired_speedups"]
    with (ROOT / "summary.csv").open("w", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields, lineterminator="\n")
        writer.writeheader()
        writer.writerows({key: row[key] for key in fields} for row in summary)
    for row in summary:
        print(f'{row["case"]}: {row["paired_speedup_median"]:.3f}x '
              f'[{row["paired_speedup_min"]:.3f}, {row["paired_speedup_max"]:.3f}]')


if __name__ == "__main__":
    summarize()
