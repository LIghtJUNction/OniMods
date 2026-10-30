#!/usr/bin/env python3
"""Validate and summarize the unchanged whole-program benchmark cross-checks."""

import argparse
import importlib.util
import json
import math
from pathlib import Path


def animation(path, parser):
    expected = {name + "/" + mode for name in (
        "repeat-single", "change-single", "repeat-range4", "repeat-range256",
        "change-range4", "change-range256", "change-last-range256")
        for mode in ("setter", "consumer")}
    groups = {}
    for line in path.read_text().splitlines():
        if not line.startswith("BENCH animation-symbols "):
            continue
        row = dict(part.split("=", 1) for part in line.split()[2:])
        for key in ("sample", "ticks", "frequency", "allocated", "copies", "copiedBytes", "checksum"):
            row[key] = int(row[key])
        expected_range = 256 if row["scenario"].endswith("range256") else 4 if row["scenario"].endswith("range4") else 1
        if (int(row["updates"]) != 30000 or int(row["symbols"]) != 256
                or int(row["range"]) != expected_range or int(row["settersPerUpdate"]) != 1
                or row["ticks"] <= 0 or row["frequency"] <= 0):
            raise ValueError("animation workload or clock contract changed")
        key = row["scenario"] + "/" + row["mode"]
        variants = groups.setdefault(key, {}).setdefault(row["sample"], {})
        if row["variant"] in variants:
            raise ValueError("duplicate animation variant")
        variants[row["variant"]] = row
    if set(groups) != expected:
        raise ValueError("missing or unexpected integrated animation workload")
    result = {}
    for name, samples in groups.items():
        if set(samples) != set(range(7)):
            raise ValueError("missing or duplicate integrated animation pair")
        pairs = []
        for number in range(7):
            variants = samples[number]
            if set(variants) != {"baseline", "candidate"}:
                raise ValueError("missing animation variant")
            if list(variants) != (["baseline", "candidate"] if number % 2 == 0 else ["candidate", "baseline"]):
                raise ValueError("animation variant order changed")
            before, after = variants["baseline"], variants["candidate"]
            if before["checksum"] != after["checksum"]:
                raise ValueError("animation output mismatch")
            if name.endswith("/consumer"):
                copies = 30000 if name.startswith("change-") else 0
                if before["copies"] != 30000 or after["copies"] != copies:
                    raise ValueError("animation copy count mismatch")
                if before["copiedBytes"] != 30000 * 256 * 48 or after["copiedBytes"] != copies * 256 * 48:
                    raise ValueError("animation copied byte mismatch")
            elif any(row["copies"] or row["copiedBytes"] for row in (before, after)):
                raise ValueError("setter-only animation unexpectedly copied payload")
            pair = {"pair": number + 1, "checksum": before["checksum"]}
            for variant, row in variants.items():
                pair[variant + "_ms"] = row["ticks"] * 1000 / row["frequency"]
                pair[variant + "_allocated_bytes"] = row["allocated"]
                pair[variant + "_copies"] = row["copies"]
                pair[variant + "_copied_bytes"] = row["copiedBytes"]
            pairs.append(pair)
        result[name] = parser.summarize_pairs(pairs)
    return result


def summarize(directory):
    helper = Path(__file__).resolve().parents[2] / "cycletrim-moono-followup-validation/summarize.py"
    spec = importlib.util.spec_from_file_location("prior_evidence_summary", helper)
    parser = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(parser)
    result = {}
    for family in ("conveyor", "animation", "power"):
        revisions = {}
        for revision in ("anchor", "prior", "candidate"):
            path = directory / f"{family}-{revision}.log"
            if family == "conveyor":
                groups = parser.conveyor(path)
                expected = {"long-misses", "shared-rails", "early-hits", "single-query",
                            "short-paths", "contained-path"}
                if set(groups) != expected:
                    raise ValueError("missing or unexpected conveyor workload")
                for group in groups.values():
                    if any(pair["baseline_direction_reads"] != pair["candidate_direction_reads"]
                           for pair in group["pairs"]):
                        raise ValueError("conveyor direction-read mismatch")
            elif family == "animation":
                groups = animation(path, parser)
            else:
                rows = [json.loads(line.split(" ", 1)[1]) for line in path.read_text().splitlines()
                        if line.startswith("POWER_BENCH_SAMPLE ")]
                expected = {"repeated-circuits", "all-distinct-circuits"}
                if {row["scenario"] for row in rows} != expected:
                    raise ValueError("missing power scenario")
                groups = {}
                for name in sorted(expected):
                    pairs = [row for row in rows if row["scenario"] == name]
                    if sorted(row["pair"] for row in pairs) != list(range(1, 8)):
                        raise ValueError("missing or duplicate power pair")
                    expected_calls = 3200 if name == "repeated-circuits" else 51200
                    if any(pair["baseline_query_calls"] != 51200
                           or pair["candidate_query_calls"] != expected_calls for pair in pairs):
                        raise ValueError("power query-count mismatch")
                    groups[name] = parser.summarize_pairs(pairs)
            for group in groups.values():
                for pair in group["pairs"]:
                    for variant in ("baseline", "candidate"):
                        elapsed = pair[variant + "_ms"]
                        if not math.isfinite(elapsed) or elapsed <= 0:
                            raise ValueError("nonpositive or nonfinite elapsed time")
            revisions[revision] = groups
        for revision in ("prior", "candidate"):
            if set(revisions["anchor"]) != set(revisions[revision]):
                raise ValueError("cross-revision workload mismatch")
            for name in revisions["anchor"]:
                before = revisions["anchor"][name]["pairs"]
                after = revisions[revision][name]["pairs"]
                for left, right in zip(before, after):
                    keys = [key for key in left if key == "checksum"
                            or key.endswith(("reads", "calls", "copies", "copied_bytes"))]
                    if any(left[key] != right[key] for key in keys):
                        raise ValueError(f"cross-revision result mismatch: {family}/{name}/{revision}")
        result[family] = {revision: {name: {key: value for key, value in row.items() if key != "pairs"}
                                    for name, row in groups.items()}
                          for revision, groups in revisions.items()}
    (directory / "summary.json").write_text(json.dumps(result, separators=(",", ":")) + "\n")
    return result


if __name__ == "__main__":
    arguments = argparse.ArgumentParser(description=__doc__)
    arguments.add_argument("directory", nargs="?", type=Path, default=Path(__file__).resolve().parent)
    args = arguments.parse_args()
    summarize(args.directory)
    print("Validated complete default-runtime workloads, outputs and summaries.")
