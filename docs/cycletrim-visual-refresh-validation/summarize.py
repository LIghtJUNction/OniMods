#!/usr/bin/env python3
"""Validate complete conduit-visual benchmark rounds and write compact evidence.

Run without arguments beside the logs, or use --directory for a temporary copy.
Missing named rounds are optional except the selected round (default: inplace);
a present but incomplete/corrupt round fails.
A recorded timing-gate failure is retained in the output and is not a parser
failure: exit status zero establishes consistency, not a performance improvement.
"""

import argparse
from functools import lru_cache
import hashlib
import json
import math
from pathlib import Path
import statistics
import struct
import sys


PREFIX = "CONDUIT_VISUAL_BENCH_"
FRAMES = 6000
WARMUPS = 3
PAIRS = 7
ROUNDS = (
    ("initial", "initial-benchmark.log"),
    ("dispatch", "dispatch-benchmark.log"),
    ("inplace", "inplace-benchmark.log"),
    ("aggregate", "aggregate-benchmark.log"),
)
# Name, work iterations, quality, change, required median timing improvement.
# These are the fixed inputs in ConduitVisualBenchmark, not fitted thresholds.
SCENARIOS = (
    ("stable-half-medium", 256, "Half", "none", True),
    ("stable-quarter-medium", 256, "Quarter", "none", True),
    ("stable-half-large", 4096, "Half", "none", True),
    ("stable-quarter-large", 4096, "Quarter", "none", True),
    ("stable-half-tiny", 0, "Half", "none", False),
    ("stable-quarter-tiny", 0, "Quarter", "none", False),
    ("changing-highlight", 256, "Quarter", "highlight", False),
    ("contents-overlay", 256, "Quarter", "overlay", False),
    ("audio-every-frame", 256, "Quarter", "audio", False),
    ("simulation-every-frame", 256, "Quarter", "simulation", False),
    ("low-fps", 256, "Quarter", "low-fps", False),
    ("simulation-every-third-frame", 256, "Quarter", "simulation-third", False),
)


class InvalidEvidence(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise InvalidEvidence(message)


def equal(actual, expected, label):
    require(type(actual) is type(expected) and actual == expected,
            f"{label}: expected {expected!r}, got {actual!r}")


def number(value, label):
    require(type(value) in (int, float) and math.isfinite(value),
            f"{label}: expected a finite number, got {value!r}")
    return value


def close(actual, expected, label):
    number(actual, label)
    require(math.isclose(actual, expected, rel_tol=1e-12, abs_tol=1e-12),
            f"{label}: expected {expected!r}, got {actual!r}")


def integer(value, label, minimum=0):
    require(type(value) is int and value >= minimum,
            f"{label}: expected an integer >= {minimum}, got {value!r}")
    return value


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"duplicate JSON key: {key}")
        result[key] = value
    return result


def reject_constant(value):
    raise InvalidEvidence(f"non-finite JSON constant: {value}")


def read_records(path):
    records = []
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.startswith(PREFIX):
            continue
        marker, separator, data = line.partition(" ")
        kind = marker[len(PREFIX):]
        require(separator and kind in ("ENV", "SAMPLE", "SUMMARY"),
                f"{path.name}:{line_number}: unknown or malformed benchmark record")
        try:
            value = json.loads(data, object_pairs_hook=unique_object, parse_constant=reject_constant)
        except (ValueError, TypeError) as error:
            raise InvalidEvidence(f"{path.name}:{line_number}: {error}") from error
        require(isinstance(value, dict), f"{path.name}:{line_number}: expected JSON object")
        records.append((kind, value))
    expected = 1 + len(SCENARIOS) * (WARMUPS + PAIRS + 1)
    equal(len(records), expected, f"{path.name} record count")
    return records


def signed(value, bits):
    value &= (1 << bits) - 1
    return value - (1 << bits) if value >= (1 << (bits - 1)) else value


@lru_cache(maxsize=None)
def payload(seed, work):
    # Expected fixture output only. All benchmark measurements are read from
    # the logs; this calculation neither runs nor changes a measured workload.
    value = seed
    for index in range(work):
        value = (value * 1664525 + index + 1013904223) & 0xffffffff
    return signed(value, 32)


@lru_cache(maxsize=None)
def expected_outputs(work, change):
    simulation = moving_sum = static_sum = checksum = 0
    interval = 1 if change == "simulation" else 3 if change == "simulation-third" else 0
    for frame in range(FRAMES):
        if interval and frame % interval == 0:
            simulation += 1
        highlight = frame if change == "highlight" else -1
        value = payload(17 + simulation * 31 + highlight + (7 if change == "overlay" else 0) + 51, work)
        moving_sum += value
        static_sum += ~value
        checksum = signed((checksum * 31 + value) * 31 + ~value, 64)
    return moving_sum, static_sum, checksum


def validate_measurement(value, scenario, candidate, phase, frequency, label):
    name, work, quality, change, _ = scenario
    require(isinstance(value, dict), f"{label}: expected a measurement object")
    interval = 1 if change == "simulation" else 3 if change == "simulation-third" else 0
    step = 0.125 if change == "low-fps" else 1.0 / 60.0
    stride = 3 if change == "simulation-third" else 2 if quality == "Half" else 4
    builds = (FRAMES + stride - 1) // stride if candidate and change in ("none", "simulation-third") else FRAMES
    simulations = (FRAMES + interval - 1) // interval if interval else 0
    audio = FRAMES if change == "audio" else 0
    moving_sum, static_sum, checksum = expected_outputs(work, change)
    expected = {
        "builds": builds, "empty_builds": 0, "moving_uploads": builds, "static_uploads": builds,
        "draws": FRAMES * 2, "simulation_calls": simulations, "simulation_version": simulations,
        "audio_resets": audio, "audio_calls": audio, "invalid_frames": 0,
        "moving_payload_sum": moving_sum, "static_payload_sum": static_sum,
        "ordered_checksum": checksum,
    }
    for key, wanted in expected.items():
        equal(value.get(key), wanted, f"{label}.{key}")
    delta = FRAMES * struct.unpack("<f", struct.pack("<f", step))[0]
    number(value.get("animation_delta"), f"{label}.animation_delta")
    require(value["animation_delta"] == delta, f"{label}.animation_delta: expected {delta!r}")
    ticks = integer(value.get("ticks"), f"{label}.ticks", 1)
    elapsed = number(value.get("elapsed_ms"), f"{label}.elapsed_ms")
    require(elapsed > 0, f"{label}.elapsed_ms must be positive")
    close(elapsed, ticks * 1000.0 / frequency, f"{label}.elapsed_ms from ticks")
    allocated = integer(value.get("allocated_bytes"), f"{label}.allocated_bytes")
    if phase == "measurement":
        equal(allocated, 0, f"{label}.allocated_bytes during measurement")
    area_reads = integer(value.get("visible_area_reads"), f"{label}.visible_area_reads")
    # Candidate read counts can fall when guard overhead is optimized. Do not
    # prescribe an implementation-specific count for successive revisions.
    if not candidate:
        equal(area_reads, FRAMES, f"{label}.visible_area_reads in original fixture")
    return elapsed


def validate_pair(sample, scenario, phase, pair, frequency, label):
    name, work, quality, change, _ = scenario
    expected = {
        "scenario": name, "phase": phase, "pair": pair,
        "order": "baseline,candidate" if pair % 2 else "candidate,baseline",
        "quality": quality, "synthetic_work_iterations": work, "frames": FRAMES,
        "simulation_interval": 1 if change == "simulation" else 3 if change == "simulation-third" else 0,
    }
    for key, wanted in expected.items():
        equal(sample.get(key), wanted, f"{label}.{key}")
    close(sample.get("seconds_per_frame"), 0.125 if change == "low-fps" else 1.0 / 60.0,
          f"{label}.seconds_per_frame")
    times = []
    for side in ("baseline", "candidate"):
        times.append(validate_measurement(sample.get(side), scenario, side == "candidate",
                                          phase, frequency, f"{label}.{side}"))
    for key in ("moving_payload_sum", "static_payload_sum", "ordered_checksum", "animation_delta"):
        equal(sample["candidate"][key], sample["baseline"][key], f"{label} paired {key}")
    close(sample.get("speedup"), times[0] / times[1], f"{label}.speedup")
    return times


def validate_round(name, path):
    records = read_records(path)
    equal(records[0][0], "ENV", f"{path.name} first record")
    environment = records[0][1]
    for key, wanted in (("frames_per_sample", FRAMES), ("warmup_pairs", WARMUPS), ("measured_pairs", PAIRS)):
        equal(environment.get(key), wanted, f"{path.name} environment.{key}")
    close(environment.get("maximum_age_seconds"), 0.1, f"{path.name} maximum mesh age")
    frequency = integer(environment.get("stopwatch_frequency"), f"{path.name} stopwatch frequency", 1)
    integer(environment.get("logical_processors"), f"{path.name} logical processors", 1)
    for key in ("runtime", "architecture", "os"):
        require(isinstance(environment.get(key), str) and environment[key],
                f"{path.name} missing environment.{key}")

    at = 1
    summaries = []
    for scenario in SCENARIOS:
        scenario_name, work, quality, _, required = scenario
        baseline, candidate, ratios = [], [], []
        for phase, count in (("warmup", WARMUPS), ("measurement", PAIRS)):
            for pair in range(1, count + 1):
                kind, sample = records[at]
                at += 1
                label = f"{path.name}/{scenario_name}/{phase}/{pair}"
                equal(kind, "SAMPLE", label)
                before, after = validate_pair(sample, scenario, phase, pair, frequency, label)
                if phase == "measurement":
                    baseline.append(before)
                    candidate.append(after)
                    ratios.append(before / after)
        kind, recorded = records[at]
        at += 1
        label = f"{path.name}/{scenario_name}/summary"
        equal(kind, "SUMMARY", label)
        equal(recorded.get("scenario"), scenario_name, f"{label}.scenario")
        computed = {"scenario": scenario_name}
        for field, values in (("baseline_ms", baseline), ("candidate_ms", candidate),
                              ("paired_speedup", ratios)):
            for suffix, operation in (("min", min), ("median", statistics.median), ("max", max)):
                key = f"{field}_{suffix}"
                computed[key] = operation(values)
                close(recorded.get(key), computed[key], f"{label}.{key}")
        improved = statistics.median(candidate) < statistics.median(baseline)
        computed.update(median_total_time_improved=improved,
                        timing_gate_required=required, timing_gate_passed=not required or improved)
        for key in ("median_total_time_improved", "timing_gate_required", "timing_gate_passed"):
            equal(recorded.get(key), computed[key], f"{label}.{key}")
        computed.update(quality=quality, synthetic_work_iterations=work,
                        warmup_pairs=WARMUPS, measurement_pairs=PAIRS)
        summaries.append(computed)
    equal(at, len(records), f"{path.name} consumed record count")
    failures = [item["scenario"] for item in summaries if not item["timing_gate_passed"]]
    return {
        "round": name, "raw_log": path.name,
        "raw_log_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "data_validation_passed": True, "environment": environment,
        "scenario_count": len(summaries), "sample_pairs": len(SCENARIOS) * (WARMUPS + PAIRS),
        "warmup_pairs": len(SCENARIOS) * WARMUPS, "measurement_pairs": len(SCENARIOS) * PAIRS,
        "timing_gate_passed": not failures, "timing_gate_failures": failures,
        "regressed_scenarios": [item["scenario"] for item in summaries
                                if item["candidate_ms_median"] > item["baseline_ms_median"]],
        "scenarios": summaries,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument("--output", type=Path, help="default: DIRECTORY/benchmarks.json")
    parser.add_argument("--selected-round", choices=[name for name, _ in ROUNDS], default="inplace",
                        help="candidate selected for review; its log must exist (default: inplace)")
    args = parser.parse_args()
    try:
        rounds = [validate_round(name, args.directory / filename)
                  for name, filename in ROUNDS if (args.directory / filename).exists()]
        require(rounds, "no named benchmark logs found; expected " + ", ".join(filename for _, filename in ROUNDS))
        selected = next((item for item in rounds if item["round"] == args.selected_round), None)
        require(selected is not None, f"selected round {args.selected_round!r} has no benchmark log")
        report = {
            "schema": 1, "data_validation_passed": True,
            "interpretation": "Validated synthetic fixture logs; timing failures remain failures. No game FPS claim.",
            "protocol": {
                "scenarios_per_round": len(SCENARIOS), "frames_per_sample": FRAMES,
                "warmup_pairs_per_scenario": WARMUPS, "measurement_pairs_per_scenario": PAIRS,
                "pair_order": "odd: baseline,candidate; even: candidate,baseline (reset for each phase)",
                "timing_gate": "candidate median milliseconds < baseline median milliseconds for the four medium/large stable scenarios",
                "measurement_allocated_bytes": 0,
            },
            "round_count": len(rounds), "sample_pairs": sum(item["sample_pairs"] for item in rounds),
            "all_rounds_timing_gates_passed": all(item["timing_gate_passed"] for item in rounds),
            "latest_round": rounds[-1]["round"],
            "latest_round_timing_gate_passed": rounds[-1]["timing_gate_passed"],
            "selected_round": selected["round"],
            "selected_round_timing_gate_passed": selected["timing_gate_passed"],
            "rounds": rounds,
        }
        destination = args.output or args.directory / "benchmarks.json"
        require(destination.resolve() not in {(args.directory / filename).resolve() for _, filename in ROUNDS},
                "output must not replace a raw benchmark log")
        destination.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n",
                               encoding="utf-8")
    except (InvalidEvidence, OSError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    failures = sum(len(item["timing_gate_failures"]) for item in rounds)
    print(f"Validated {len(rounds)} rounds / {report['sample_pairs']} sample pairs; "
          f"retained {failures} timing-gate failures; wrote {destination}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
