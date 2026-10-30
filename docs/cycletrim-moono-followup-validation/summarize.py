#!/usr/bin/env python3
"""Recalculate paired summaries from the retained synthetic benchmark logs."""

import json
from pathlib import Path
import statistics


def distribution(values):
    return {"median": statistics.median(values), "min": min(values), "max": max(values)}


def summarize_pairs(pairs):
    if len(pairs) != 7:
        raise ValueError(f"expected seven pairs, found {len(pairs)}")
    return {
        "baseline_ms": distribution([pair["baseline_ms"] for pair in pairs]),
        "candidate_ms": distribution([pair["candidate_ms"] for pair in pairs]),
        "paired_speedup": distribution([
            pair["baseline_ms"] / pair["candidate_ms"] for pair in pairs
        ]),
        "baseline_allocated_bytes": distribution([
            pair["baseline_allocated_bytes"] for pair in pairs
        ]),
        "candidate_allocated_bytes": distribution([
            pair["candidate_allocated_bytes"] for pair in pairs
        ]),
        "pairs": pairs,
    }


def conveyor(path):
    groups = {}
    for line in path.read_text().splitlines():
        if line.startswith("CONVEYOR_BENCH_SAMPLE "):
            record = json.loads(line.split(" ", 1)[1])
            groups.setdefault(record["scenario"], []).append(record)
    if len(groups) != 6:
        raise ValueError(f"expected six conveyor scenarios in {path}")
    result = {}
    for name, pairs in groups.items():
        if sorted(pair["pair"] for pair in pairs) != list(range(1, 8)):
            raise ValueError(f"missing or repeated conveyor pair: {name}")
        result[name] = summarize_pairs(pairs)
    return result


def animation(path):
    groups = {}
    for line in path.read_text().splitlines():
        if not line.startswith("BENCH animation-symbols "):
            continue
        record = dict(word.split("=", 1) for word in line.split()[2:])
        for key in ("sample", "updates", "symbols", "range", "ticks", "frequency",
                    "allocated", "copies", "copiedBytes", "checksum"):
            record[key] = int(record[key])
        name = record["scenario"] + "/" + record["mode"]
        sample = groups.setdefault(name, {}).setdefault(record["sample"], {})
        if record["variant"] in sample:
            raise ValueError(f"repeated animation sample: {name}")
        sample[record["variant"]] = record
    if len(groups) != 8:
        raise ValueError(f"expected eight animation workloads in {path}")
    result = {}
    for name, records in groups.items():
        if sorted(records) != list(range(7)):
            raise ValueError(f"missing or repeated animation pair: {name}")
        pairs = []
        for number, variants in sorted(records.items()):
            before, after = variants["baseline"], variants["candidate"]
            if before["checksum"] != after["checksum"]:
                raise ValueError(f"unequal animation payload: {name}/{number}")
            pair = {"pair": number + 1, "checksum": before["checksum"],
                    "order": "baseline,candidate" if number % 2 == 0 else "candidate,baseline"}
            for variant, record in variants.items():
                pair[variant + "_ms"] = record["ticks"] * 1000 / record["frequency"]
                pair[variant + "_allocated_bytes"] = record["allocated"]
                pair[variant + "_copies"] = record["copies"]
                pair[variant + "_copied_bytes"] = record["copiedBytes"]
            pairs.append(pair)
        result[name] = summarize_pairs(pairs)
    return result


if __name__ == "__main__":
    directory = Path(__file__).resolve().parent
    output = {
        "interpretation": "Synthetic batch timings; paired ratios need not equal the ratio of medians.",
        "conveyor": conveyor(directory / "conveyor-samples.log"),
        "animation": animation(directory / "animation-samples.log"),
        "first_conveyor": conveyor(directory / "first-conveyor-samples.log"),
        "first_animation": animation(directory / "first-animation-samples.log"),
    }
    (directory / "benchmarks.json").write_text(json.dumps(output, indent=2) + "\n")
    print("Wrote benchmarks.json from all retained paired samples.")
