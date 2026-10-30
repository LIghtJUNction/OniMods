#!/usr/bin/env python3
"""Validate and summarize the unchanged whole-program benchmark cross-checks."""

import argparse
import importlib.util
import json
from pathlib import Path


def summarize(directory):
    helper = Path(__file__).resolve().parents[2] / "cycletrim-moono-followup-validation/summarize.py"
    spec = importlib.util.spec_from_file_location("prior_evidence_summary", helper)
    parser = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(parser)
    result = {}
    for family in ("conveyor", "animation", "power"):
        revisions = {}
        for revision in ("prior", "candidate"):
            path = directory / f"{family}-{revision}.log"
            if family != "power":
                groups = getattr(parser, family)(path)
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
                    groups[name] = parser.summarize_pairs(pairs)
            revisions[revision] = groups
        if set(revisions["prior"]) != set(revisions["candidate"]):
            raise ValueError("prior/candidate workload mismatch")
        for name in revisions["prior"]:
            before = revisions["prior"][name]["pairs"]
            after = revisions["candidate"][name]["pairs"]
            for left, right in zip(before, after):
                keys = [key for key in left if key == "checksum"
                        or key.endswith(("reads", "calls", "copies", "copied_bytes"))]
                if any(left[key] != right[key] for key in keys):
                    raise ValueError(f"cross-revision result mismatch: {family}/{name}")
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
