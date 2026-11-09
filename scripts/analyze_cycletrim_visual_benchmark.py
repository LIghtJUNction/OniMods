#!/usr/bin/env python3
"""Analyze every logged old/new sample; thresholds and scenarios are fixed.

Usage:
  python analyze.py benchmark-initial.log --output benchmark-initial-analysis.json

By default the existing <log-stem>-sources.json and <log-stem>-run.json are
embedded verbatim as JSON objects. --provenance and --run-metadata can name them
explicitly. This script never executes a benchmark, rehashes today's production
sources as historical evidence, removes outliers, or selects a favorable subset.
Exit codes: 0 all gates pass; 1 a performance/allocation/checksum gate fails;
2 incomplete, malformed, or inconsistent evidence.
"""

import argparse
import csv
import datetime
import hashlib
import json
import math
from pathlib import Path
import re
import statistics
import sys


BASELINE_JLIB = "2769c7efa7285949d85366ae31d1a9739cef71bf"
BLOCKS = 17
ITERATIONS = 3
WARMUP_RENDERS = 8
SAMPLES_PER_VARIANT = BLOCKS * 2
PATTERN_LIMITS = {"all_gas": 0.90, "one_gas_per_row_chunk": 1.03, "all_inactive": 1.03}
CSV_HEADER = "row,scenario,variant,block,position,elapsed_ticks,iterations,allocated_bytes,checksum"
HEX_SHA256 = re.compile(r"[0-9a-f]{64}\Z")
SIZES = ((96, 96), (256, 384), (736, 304))


def specifications():
    result = {}
    for width, height in SIZES:
        for mixture in (0, 10, 100):
            result[f"{width}x{height}_mixed{mixture}"] = (width, height, mixture, "all_gas")
    for pattern in ("one_gas_per_row_chunk", "all_inactive"):
        result[f"736x304_mixed10_{pattern}"] = (736, 304, 10, pattern)
    return result


SPECS = specifications()


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def fields(line):
    return dict(part.split("=", 1) for part in line.split(";") if "=" in part)


def percentile_linear(values, probability):
    """Hyndman-Fan type 7: interpolate at (n - 1) * probability."""
    index = (len(values) - 1) * probability
    lower = math.floor(index)
    upper = math.ceil(index)
    return values[lower] + (values[upper] - values[lower]) * (index - lower)


def timing_stats(rows, frequency):
    values = sorted(row["elapsed_ticks"] * 1000.0 / frequency / row["iterations"] for row in rows)
    q1 = percentile_linear(values, 0.25)
    q3 = percentile_linear(values, 0.75)
    return {
        "sample_count": len(rows),
        "render_count": sum(row["iterations"] for row in rows),
        "median_ms_per_render": statistics.median(values),
        "p95_sample_mean_ms": values[math.ceil(len(values) * 0.95) - 1],
        "q1_sample_mean_ms": q1,
        "q3_sample_mean_ms": q3,
        "iqr_sample_mean_ms": q3 - q1,
        "min_sample_mean_ms": values[0],
        "max_sample_mean_ms": values[-1],
        "mean_sample_mean_ms": statistics.mean(values),
    }


def allocation_stats(rows):
    values = [row["allocated_bytes"] / row["iterations"] for row in rows]
    total = sum(row["allocated_bytes"] for row in rows)
    renders = sum(row["iterations"] for row in rows)
    return {
        "total_bytes": total,
        "max_bytes_per_render": max(values),
        "median_bytes_per_render": statistics.median(values),
        "mean_bytes_per_render": total / renders,
        "nonzero_sample_count": sum(row["allocated_bytes"] != 0 for row in rows),
    }


def read_json_evidence(path):
    data = path.read_bytes()
    return {"path": str(path.resolve()), "sha256": sha256(data), "data": json.loads(data)}


def analyze(log_path, provenance_path, run_path):
    log_bytes = log_path.read_bytes()
    text = log_bytes.decode("utf-8", errors="strict")
    errors = []
    warnings = []
    metadata = {}
    metadata_lines = []
    fixture_rows = []
    samples = []
    summary_rows = []
    other_lines = []
    header_count = 0

    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.strip()
        if not line:
            continue
        if line == CSV_HEADER:
            header_count += 1
        elif line.startswith("sample,"):
            sample = {"line_number": number, "raw": raw}
            try:
                parts = next(csv.reader([line], strict=True))
                if len(parts) != 9:
                    raise ValueError("expected exactly nine CSV fields")
                sample.update(scenario=parts[1], variant=parts[2])
                for name, value in zip(("block", "position", "elapsed_ticks", "iterations", "allocated_bytes"), parts[3:8]):
                    sample[name] = int(value)
                checksum = int(parts[8])
                if not 0 <= checksum <= 0xffffffffffffffff:
                    raise ValueError("checksum is outside uint64")
                # Preserve all uint64 digits in consumers that parse JSON with doubles.
                sample["checksum"] = str(checksum)
            except (ValueError, csv.Error) as error:
                sample["parse_error"] = str(error)
                errors.append(f"line {number}: malformed sample: {error}")
            samples.append(sample)
        elif line.startswith("fixture="):
            fixture_rows.append({"line_number": number, "raw": raw, "fields": fields(line)})
        elif line.startswith("summary="):
            summary_rows.append({"line_number": number, "raw": raw, "fields": fields(line)})
        elif any(line.startswith(prefix) for prefix in ("multicomponent_visual_scope=", "runtime=", "native_path=", "protocol=")):
            parsed = fields(line)
            for key, value in parsed.items():
                if key in metadata:
                    errors.append(f"line {number}: duplicate metadata key {key}")
                metadata[key] = value
            metadata_lines.append({"line_number": number, "raw": raw})
        else:
            other_lines.append({"line_number": number, "raw": raw})

    def require(condition, message):
        if not condition:
            errors.append(message)

    def integer(mapping, key, where):
        try:
            return int(mapping[key])
        except (KeyError, TypeError, ValueError):
            errors.append(f"{where}: missing or invalid integer {key}")
            return None

    require(header_count == 1, "expected one CSV header and exactly one benchmark run")
    require(metadata.get("protocol") == "alternating_ABBA_BAAB", "unexpected sampling protocol")
    for key, expected in (("blocks", BLOCKS), ("samples_per_variant", SAMPLES_PER_VARIANT),
                          ("iterations_per_sample", ITERATIONS), ("warmup_renders_per_variant", WARMUP_RENDERS)):
        require(integer(metadata, key, "protocol") == expected, f"protocol {key} must equal {expected}")
    frequency = integer(metadata, "stopwatch_frequency", "runtime")
    require(frequency is not None and frequency > 0, "Stopwatch frequency must be positive")
    require(metadata.get("gpu") == "0", "native fixture must run with GPU disabled")
    if metadata.get("tiered_compilation") != "0":
        warnings.append("DOTNET_TieredCompilation was not logged as 0; recorded without changing the timing gate.")
    require(BASELINE_JLIB in metadata.get("multicomponent_visual_scope", ""), "frozen baseline revision is missing from scope")
    require(len(samples) == len(SPECS) * BLOCKS * 4, "expected all 748 samples from all 11 scenarios")
    require(len(fixture_rows) == len(SPECS), "expected exactly 11 fixture records")
    require(len(summary_rows) == len(SPECS) * 2, "expected both printed variant summaries for all 11 scenarios")
    require([row["fields"].get("fixture") for row in fixture_rows] == list(SPECS), "fixture roster/order differs from the fixed eleven-scene matrix")

    provenance = read_json_evidence(provenance_path)
    run = read_json_evidence(run_path)
    recorded = provenance["data"]
    require(isinstance(recorded, dict), "provenance must be a JSON object")
    if not isinstance(recorded, dict):
        recorded = {}
    require(recorded.get("baseline_jlib") == BASELINE_JLIB, "provenance baseline revision mismatch")
    require(recorded.get("native_sha256") == metadata.get("native_sha256"), "log and pre-run manifest native SHA-256 differ")
    source_hashes = recorded.get("sources", {})
    require(isinstance(source_hashes, dict) and bool(source_hashes), "pre-run source hash map is required")
    if isinstance(source_hashes, dict):
        for name, digest in source_hashes.items():
            require(isinstance(digest, str) and HEX_SHA256.fullmatch(digest) is not None, f"invalid recorded source SHA-256: {name}")
    gates = recorded.get("gates", {})
    require(gates.get("dense_max_median_ratio") == 0.90 and gates.get("controls_max_median_ratio") == 1.03,
            "pre-run timing gates differ from the fixed 0.90/1.03 limits")
    require(isinstance(run["data"], dict) and run["data"].get("returncode") == 0,
            "benchmark must have a recorded successful exit code")
    if isinstance(run["data"], dict) and "log" in run["data"]:
        require(Path(run["data"]["log"]).resolve() == log_path.resolve(), "run metadata points to another log")

    for row in samples:
        require(row.get("scenario") in SPECS, f"line {row['line_number']}: unknown/missing scenario")
    results = []
    for name, (width, height, mixture, pattern) in SPECS.items():
        scenario_errors = len(errors)
        fixture_matches = [row for row in fixture_rows if row["fields"].get("fixture") == name]
        require(len(fixture_matches) == 1, f"{name}: expected one fixture record")
        fixture = fixture_matches[0]["fields"] if len(fixture_matches) == 1 else {}
        count = width * height
        mixed = count // 100 * mixture + min(count % 100, mixture)
        chunks = ((width + 127) // 128) * height
        eligible = count if pattern == "all_gas" else chunks if pattern == "one_gas_per_row_chunk" else 0
        eligible_mixed = mixed if pattern == "all_gas" else sum(
            (y * width + x) % 100 < mixture for y in range(height) for x in range(0, width, 128)
        ) if pattern == "one_gas_per_row_chunk" else 0
        expected_fields = {"cells": count, "mixed_cells": mixed, "species_records": count + mixed,
                           "eligible_cells": eligible, "eligible_mixed_cells": eligible_mixed,
                           "expected_old_leases": eligible + 1,
                           "expected_batch_leases": 1 if pattern == "all_inactive" else chunks + 1}
        require(fixture.get("grid_pattern") == pattern, f"{name}: wrong eligibility pattern")
        for key, value in expected_fields.items():
            require(integer(fixture, key, name) == value, f"{name}: {key} must equal {value}")
        generation = integer(fixture, "generation", name)
        require(generation is not None and generation > 0, f"{name}: generation must be positive")
        limit = PATTERN_LIMITS[pattern]
        try:
            require(float(fixture["gate_max_median_ratio"]) == limit, f"{name}: logged gate differs from its fixed limit")
        except (KeyError, ValueError, TypeError):
            errors.append(f"{name}: missing/invalid logged timing gate")

        rows = [row for row in samples if row.get("scenario") == name]
        require(len(rows) == BLOCKS * 4, f"{name}: expected all 68 samples")
        for index, row in enumerate(rows):
            block, position = divmod(index, 4)
            expected_variant = "old" if ((position in (0, 3)) == (block % 2 == 0)) else "batch"
            require(row.get("block") == block and row.get("position") == position and row.get("variant") == expected_variant,
                    f"{name} line {row['line_number']}: invalid or incomplete ABBA/BAAB sequence")
            require("parse_error" not in row and row.get("iterations") == ITERATIONS
                    and isinstance(row.get("elapsed_ticks"), int) and row["elapsed_ticks"] > 0
                    and isinstance(row.get("allocated_bytes"), int) and row["allocated_bytes"] >= 0,
                    f"{name} line {row['line_number']}: invalid sample values")
        result = {"scenario": name, "kind": "dense" if pattern == "all_gas" else "control",
                  "grid_pattern": pattern, "fixture": fixture,
                  "sample_line_numbers": [row["line_number"] for row in rows]}
        # Invalid evidence has no cleaned or partial timing result. Raw rows remain above.
        if len(errors) != scenario_errors or not frequency or frequency <= 0:
            result.update(status="INVALID", timing=None, gates=None)
            results.append(result)
            continue

        variants = {variant: [row for row in rows if row["variant"] == variant] for variant in ("old", "batch")}
        timing = {variant: timing_stats(group, frequency) for variant, group in variants.items()}
        allocations = {variant: allocation_stats(group) for variant, group in variants.items()}
        ratio = timing["batch"]["median_ms_per_render"] / timing["old"]["median_ms_per_render"]
        checksum_values = sorted({row["checksum"] for row in rows})
        timing_pass = ratio <= limit
        allocation_pass = (allocations["batch"]["max_bytes_per_render"] <= allocations["old"]["max_bytes_per_render"]
                           and allocations["batch"]["total_bytes"] <= allocations["old"]["total_bytes"])
        checksum_pass = len(checksum_values) == 1

        for variant in variants:
            matches = [row for row in summary_rows if row["fields"].get("summary") == name and row["fields"].get("variant") == variant]
            require(len(matches) == 1, f"{name}/{variant}: expected one printed summary")
            if len(matches) == 1:
                checks = {"median_ms_per_render": timing[variant]["median_ms_per_render"],
                          "p95_sample_mean_ms": timing[variant]["p95_sample_mean_ms"],
                          "min_ms": timing[variant]["min_sample_mean_ms"],
                          "max_ms": timing[variant]["max_sample_mean_ms"],
                          "max_allocated_bytes_per_render": allocations[variant]["max_bytes_per_render"]}
                for key, computed in checks.items():
                    try:
                        printed = float(matches[0]["fields"][key])
                        precision = 0.0005 if key == "max_allocated_bytes_per_render" else 0.0000005
                        require(math.isfinite(printed) and abs(printed - computed) <= precision + 1e-10,
                                f"{name}/{variant}: printed {key} disagrees with all-sample calculation")
                    except (KeyError, TypeError, ValueError):
                        errors.append(f"{name}/{variant}: missing/invalid printed {key}")

        result.update(
            status="PASS" if timing_pass and allocation_pass and checksum_pass else "FAIL",
            timing=timing, allocation=allocations, median_ratio_new_over_old=ratio,
            median_reduction_percent=(1 - ratio) * 100,
            checksum={"unique_values": checksum_values, "old_new_all_samples_equal": checksum_pass},
            gates={"timing": {"pass": timing_pass, "max_median_ratio": limit},
                   "allocation": {"pass": allocation_pass, "rule": "both max bytes/render and total warm sample bytes must not increase"},
                   "checksum": {"pass": checksum_pass, "rule": "all 68 old/new sample checksums in this scenario must be identical"}},
        )
        if len(errors) != scenario_errors:
            result["status"] = "INVALID"
        results.append(result)

    valid = not errors
    passed = valid and all(result["status"] == "PASS" for result in results)
    report = {
        "schema_version": 1,
        "analyzed_at_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "analyzer_sha256": sha256(Path(__file__).read_bytes()),
        "input_log": {"path": str(log_path.resolve()), "sha256": sha256(log_bytes), "byte_count": len(log_bytes)},
        "scope": "frozen production Prefix vs candidate production Prefix, real native reader and mutex, host game doubles; not game FPS",
        "data_policy": "Every input sample is retained. No deletion, trimming, outlier filtering, winsorization, or favorable-run selection is implemented.",
        "statistical_methods": {
            "unit": "milliseconds per render; each logged timing sample averages three complete renders",
            "median": "middle value or arithmetic mean of the two middle values",
            "p95": "nearest rank: sorted sample means at ceil(0.95 * n) - 1; not individual-render p95 latency",
            "quartiles": "linear interpolation at (n - 1) * p (Hyndman-Fan type 7)",
            "iqr": "Q3 minus Q1 of all sample means",
        },
        "fixed_gate_policy": {"dense_max_median_ratio": 0.90, "controls_max_median_ratio": 1.03,
                              "allocation": "candidate maximum per render and total warm allocations must not exceed baseline",
                              "checksum": "all old/new checksums within each scenario equal"},
        "environment_from_log": metadata,
        "provenance_origin": "Existing pre-run capture embedded unchanged; current production sources were not rehashed as historical evidence.",
        "source_provenance": provenance,
        "run_metadata": run,
        "sample_count": len(samples),
        "samples_in_input_order": samples,
        "fixture_records": fixture_rows,
        "printed_summary_records": summary_rows,
        "metadata_records": metadata_lines,
        "other_log_lines": other_lines,
        "scenarios": results,
        "integrity": {"pass": valid, "errors": errors, "warnings": warnings},
        "overall": {"pass": passed, "status": "INVALID" if not valid else "PASS" if passed else "FAIL",
                    "failed_scenarios": [result["scenario"] for result in results if result["status"] != "PASS"]},
    }
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("log", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--provenance", type=Path, help="existing pre-run sources/environment JSON; never recaptured")
    parser.add_argument("--run-metadata", type=Path, help="existing command/return-code JSON")
    args = parser.parse_args()
    output = args.output or args.log.with_name(args.log.stem + "-analysis.json")
    provenance = args.provenance or args.log.with_name(args.log.stem + "-sources.json")
    run = args.run_metadata or args.log.with_name(args.log.stem + "-run.json")
    if output.resolve() in {path.resolve() for path in (args.log, provenance, run, Path(__file__))}:
        parser.error("output must not overwrite any input evidence or this script")
    try:
        report = analyze(args.log, provenance, run)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False) + "\n")
    except (OSError, ValueError, TypeError) as error:
        print(f"Evidence could not be analyzed: {error}", file=sys.stderr)
        return 2
    for result in report["scenarios"]:
        if result["timing"] is None:
            print(f"INVALID {result['scenario']}: incomplete/inconsistent evidence")
            continue
        before = result["timing"]["old"]["median_ms_per_render"]
        after = result["timing"]["batch"]["median_ms_per_render"]
        print(f"{result['status']:7} {result['scenario']}: {before:.6f} -> {after:.6f} ms; "
              f"reduction={result['median_reduction_percent']:.3f}%; ratio={result['median_ratio_new_over_old']:.6f}")
    for error in report["integrity"]["errors"]:
        print("INVALID: " + error, file=sys.stderr)
    print(f"Overall {report['overall']['status']}; retained {report['sample_count']} samples; {output.resolve()}")
    return 2 if not report["integrity"]["pass"] else 0 if report["overall"]["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
