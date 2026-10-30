#!/usr/bin/env python3
"""Validate every retained sample and summarize each measurement round separately."""

import json
import math
from pathlib import Path
import statistics


PAIRS = 7
CONVEYOR_SCENARIOS = {
    "long-misses": (100, 32, 256, 128),
    "shared-rails": (100, 32, 256, 128),
    "early-hits": (1000, 1, 1024, 128),
    "single-query": (1000, 1, 1024, 1),
    "short-paths": (1000, 8, 16, 16),
    "contained-path": (100000, 8, 256, 32),
}
ANIMATION_SCENARIOS = {
    "repeat-single": (1, False),
    "change-single": (1, True),
    "repeat-range4": (4, False),
    "repeat-range256": (256, False),
    "change-range4": (4, True),
    "change-range256": (256, True),
    "change-last-range256": (256, True),
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def integer(value, name, minimum=0, maximum=None):
    require(type(value) is int, f"{name}: expected an integer")
    require(value >= minimum, f"{name}: below {minimum}")
    require(maximum is None or value <= maximum, f"{name}: above {maximum}")
    return value


def positive(value, name):
    require(type(value) in (int, float), f"{name}: expected a number")
    require(math.isfinite(value) and value > 0, f"{name}: expected finite positive value")
    return value


def unique_object(items):
    result = {}
    for key, value in items:
        require(key not in result, f"duplicate field: {key}")
        result[key] = value
    return result


def invalid_constant(value):
    raise ValueError(f"non-finite JSON constant: {value}")


def json_record(line):
    record = json.loads(line.split(" ", 1)[1], object_pairs_hook=unique_object,
                        parse_constant=invalid_constant)
    require(isinstance(record, dict), "expected a JSON object")
    return record


def text_record(line):
    items = []
    for word in line.split()[2:]:
        require("=" in word, f"invalid benchmark field: {word}")
        items.append(word.split("=", 1))
    return unique_object(items)


def distribution(values):
    return {"median": statistics.median(values), "min": min(values), "max": max(values)}


def summarize_pairs(pairs):
    require(len(pairs) == PAIRS, f"expected {PAIRS} pairs, found {len(pairs)}")
    result = {}
    for field in ("ms", "allocated_bytes", "direction_reads", "query_calls", "copies", "copied_bytes"):
        for variant in ("baseline", "candidate"):
            key = variant + "_" + field
            if key in pairs[0]:
                result[key] = distribution([pair[key] for pair in pairs])
    result["paired_speedup"] = distribution([
        pair["baseline_ms"] / pair["candidate_ms"] for pair in pairs
    ])
    result["pairs"] = pairs
    return result


def power(path):
    scenarios = {"repeated-circuits": 32, "all-distinct-circuits": 512}
    groups = {}
    summaries = {}
    environments = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("POWER_BENCH_SAMPLE "):
            record = json_record(line)
            name = record.get("scenario")
            require(name in scenarios, f"{path}: unknown power scenario {name}")
            groups.setdefault(name, []).append(record)
        elif line.startswith("POWER_BENCH_SUMMARY "):
            record = json_record(line)
            name = record.get("scenario")
            require(name not in summaries, f"{path}: duplicate power summary {name}")
            summaries[name] = record
        elif line.startswith("POWER_BENCH_ENV "):
            environments.append(json_record(line))
        elif line.startswith("POWER_BENCH_"):
            raise ValueError(f"{path}: unrecognized power record: {line}")
    require(len(environments) == 1, f"{path}: expected one power environment record")
    for key, expected in {
        "requests_per_update": 512, "updates_per_sample": 100, "warmup_updates": 20,
        "pairs": PAIRS, "synthetic_work_iterations": 128, "temporary_bytes_per_query": 128,
    }.items():
        require(environments[0].get(key) == expected, f"{path}: changed power protocol {key}")
    require(set(groups) == set(scenarios), f"{path}: missing power scenarios")
    require(set(summaries) == set(scenarios), f"{path}: missing power summaries")
    result = {}
    for name, unique_circuits in scenarios.items():
        pairs = groups[name]
        require([pair.get("pair") for pair in pairs] == list(range(1, PAIRS + 1)),
                f"{path}: missing, repeated or unordered power pair: {name}")
        # The unchanged fixture returns circuit % 13 + 1 for each request.
        expected_checksum = 100 * sum((index % unique_circuits) % 13 + 1 for index in range(512))
        for pair in pairs:
            label = f"{path}/{name}/{pair['pair']}"
            integer(pair["pair"], label + "/pair", 1, PAIRS)
            expected_order = "baseline,candidate" if pair["pair"] % 2 else "candidate,baseline"
            require(pair.get("order") == expected_order, f"{label}: incorrect sample order")
            integer(pair["unique_circuits"], label + "/unique_circuits", 1)
            require(pair["unique_circuits"] == unique_circuits, f"{label}: changed workload")
            for variant, expected_calls in (("baseline", 51200), ("candidate", unique_circuits * 100)):
                positive(pair[variant + "_ms"], label + "/" + variant + "_ms")
                integer(pair[variant + "_allocated_bytes"], label + "/" + variant + "_allocated_bytes")
                integer(pair[variant + "_query_calls"], label + "/" + variant + "_query_calls")
                require(pair[variant + "_query_calls"] == expected_calls,
                        f"{label}: incorrect {variant} query count")
            positive(pair["checksum"], label + "/checksum")
            require(pair["checksum"] == expected_checksum, f"{label}: incorrect power output checksum")
            positive(pair["speedup"], label + "/speedup")
            require(math.isclose(pair["speedup"], pair["baseline_ms"] / pair["candidate_ms"],
                                 rel_tol=1e-12), f"{label}: incorrect printed speedup")
        result[name] = summarize_pairs(pairs)
        for statistic in ("min", "median", "max"):
            printed = summaries[name]["speedup_" + statistic]
            positive(printed, f"{path}/{name}/speedup_{statistic}")
            require(math.isclose(printed, result[name]["paired_speedup"][statistic], rel_tol=1e-12),
                    f"{path}/{name}: incorrect printed {statistic}")
    return result


def conveyor(path):
    groups = {}
    summaries = {}
    environments = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("CONVEYOR_BENCH_SAMPLE "):
            record = json_record(line)
            name = record.get("scenario")
            require(name in CONVEYOR_SCENARIOS, f"{path}: unknown conveyor scenario {name}")
            groups.setdefault(name, []).append(record)
        elif line.startswith("CONVEYOR_BENCH_SUMMARY "):
            record = json_record(line)
            name = record.get("scenario")
            require(name not in summaries, f"{path}: duplicate conveyor summary {name}")
            summaries[name] = record
        elif line.startswith("CONVEYOR_BENCH_ENV "):
            environments.append(json_record(line))
        elif line.startswith("CONVEYOR_BENCH_"):
            raise ValueError(f"{path}: unrecognized conveyor record: {line}")
    require(len(environments) == 1, f"{path}: expected one conveyor environment record")
    require(environments[0].get("warmup_batches") == 3
            and environments[0].get("pairs") == PAIRS,
            f"{path}: unexpected conveyor measurement protocol")
    require(set(groups) == set(CONVEYOR_SCENARIOS), f"{path}: missing conveyor scenarios")
    require(set(summaries) == set(CONVEYOR_SCENARIOS), f"{path}: missing conveyor summaries")
    result = {}
    for name, expected_workload in CONVEYOR_SCENARIOS.items():
        pairs = groups[name]
        require([pair.get("pair") for pair in pairs] == list(range(1, PAIRS + 1)),
                f"{path}: missing, repeated or unordered conveyor pair: {name}")
        checksums = set()
        direction_reads = set()
        for pair in pairs:
            label = f"{path}/{name}/{pair['pair']}"
            integer(pair["pair"], label + "/pair", 1, PAIRS)
            expected_order = "baseline,candidate" if pair["pair"] % 2 else "candidate,baseline"
            require(pair.get("order") == expected_order, f"{label}: incorrect sample order")
            workload = tuple(integer(pair[key], label + "/" + key, 1) for key in (
                "iterations_per_sample", "retained_paths", "rails_per_path", "new_path_rails"))
            require(workload == expected_workload, f"{label}: changed workload")
            for variant in ("baseline", "candidate"):
                positive(pair[variant + "_ms"], label + "/" + variant + "_ms")
                integer(pair[variant + "_allocated_bytes"], label + "/" + variant + "_allocated_bytes")
                integer(pair[variant + "_direction_reads"], label + "/" + variant + "_direction_reads")
            require(pair["baseline_direction_reads"] == pair["candidate_direction_reads"],
                    f"{label}: unequal direction callbacks")
            integer(pair["checksum"], label + "/checksum", -(2 ** 63), 2 ** 63 - 1)
            checksums.add(pair["checksum"])
            direction_reads.add(pair["baseline_direction_reads"])
            positive(pair["speedup"], label + "/speedup")
            require(math.isclose(pair["speedup"], pair["baseline_ms"] / pair["candidate_ms"],
                                 rel_tol=1e-12), f"{label}: incorrect printed speedup")
        require(len(checksums) == 1 and len(direction_reads) == 1,
                f"{path}/{name}: inconsistent deterministic output across pairs")
        result[name] = summarize_pairs(pairs)
        for statistic in ("min", "median", "max"):
            printed = summaries[name]["speedup_" + statistic]
            positive(printed, f"{path}/{name}/speedup_{statistic}")
            require(math.isclose(printed, result[name]["paired_speedup"][statistic], rel_tol=1e-12),
                    f"{path}/{name}: incorrect printed {statistic}")
    return result


def animation(path, expected_groups):
    require(expected_groups in (8, 14), "animation expected_groups must be 8 or 14")
    scenarios = dict(list(ANIMATION_SCENARIOS.items())[:expected_groups // 2])
    expected_names = {name + "/" + mode for name in scenarios for mode in ("setter", "consumer")}
    groups = {}
    orders = {}
    configs = []
    numeric_fields = ("sample", "updates", "symbols", "range", "settersPerUpdate", "ticks",
                      "frequency", "allocated", "copies", "copiedBytes", "checksum")
    expected_fields = set(numeric_fields) | {"scenario", "mode", "variant"}
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("BENCH animation-config "):
            configs.append(text_record(line))
            continue
        if not line.startswith("BENCH animation-symbols "):
            require(not line.startswith("BENCH animation-"),
                    f"{path}: unrecognized animation record: {line}")
            continue
        record = text_record(line)
        require(set(record) == expected_fields, f"{path}: missing or unknown animation fields")
        for key in numeric_fields:
            record[key] = int(record[key])
        name = record["scenario"] + "/" + record["mode"]
        require(name in expected_names, f"{path}: unexpected animation workload {name}")
        integer(record["sample"], f"{path}/{name}/sample", 0, PAIRS - 1)
        variant = record["variant"]
        require(variant in ("baseline", "candidate"), f"{path}/{name}: invalid variant")
        sample = groups.setdefault(name, {}).setdefault(record["sample"], {})
        require(variant not in sample, f"{path}/{name}: duplicate animation sample")
        sample[variant] = record
        orders.setdefault(name, []).append((record["sample"], variant))
    expected_config = {
        "warmups": "3", "pairs": "7", "symbols": "256", "updates": "30000",
        "settersPerUpdate": "1", "consumer": "managed-byte-copy",
        "firstUpload": "excluded", "timingGate": "none",
    }
    require(configs == [expected_config], f"{path}: missing, duplicate or changed animation config")
    require(set(groups) == expected_names, f"{path}: expected {expected_groups} animation workloads")
    expected_order = [(number, variant) for number in range(PAIRS) for variant in
                      (("baseline", "candidate") if number % 2 == 0 else ("candidate", "baseline"))]
    result = {}
    for name, records in groups.items():
        require(sorted(records) == list(range(PAIRS)), f"{path}/{name}: missing animation pair")
        require(orders[name] == expected_order, f"{path}/{name}: incorrect sample order")
        pairs = []
        checksums = set()
        for number, variants in sorted(records.items()):
            require(set(variants) == {"baseline", "candidate"}, f"{path}/{name}/{number}: missing variant")
            before, after = variants["baseline"], variants["candidate"]
            require(before["checksum"] == after["checksum"], f"{path}/{name}/{number}: unequal payload")
            checksums.add(before["checksum"])
            pair = {"pair": number + 1, "checksum": before["checksum"],
                    "order": "baseline,candidate" if number % 2 == 0 else "candidate,baseline",
                    "samples": variants}
            for variant, record in variants.items():
                label = f"{path}/{name}/{number}/{variant}"
                expected_range, changes = scenarios[record["scenario"]]
                require(record["symbols"] == 256 and record["range"] == expected_range
                        and record["updates"] == 30000 and record["settersPerUpdate"] == 1,
                        f"{label}: changed workload")
                integer(record["ticks"], label + "/ticks", 1)
                integer(record["frequency"], label + "/frequency", 1)
                integer(record["allocated"], label + "/allocated")
                integer(record["checksum"], label + "/checksum", -(2 ** 63), 2 ** 63 - 1)
                expected_copies = (30000 if variant == "baseline" or changes else 0) \
                    if record["mode"] == "consumer" else 0
                require(record["copies"] == expected_copies
                        and record["copiedBytes"] == expected_copies * 256 * 48,
                        f"{label}: incorrect consumer copy count or byte count")
                milliseconds = record["ticks"] * 1000 / record["frequency"]
                positive(milliseconds, label + "/milliseconds")
                pair[variant + "_ms"] = milliseconds
                pair[variant + "_allocated_bytes"] = record["allocated"]
                pair[variant + "_copies"] = record["copies"]
                pair[variant + "_copied_bytes"] = record["copiedBytes"]
            require(before["frequency"] == after["frequency"], f"{path}/{name}/{number}: unequal timer frequency")
            pairs.append(pair)
        require(len(checksums) == 1, f"{path}/{name}: inconsistent payload across pairs")
        result[name] = summarize_pairs(pairs)
    for scenario in scenarios:
        require(result[scenario + "/setter"]["pairs"][0]["checksum"]
                == result[scenario + "/consumer"]["pairs"][0]["checksum"],
                f"{path}/{scenario}: different setter and consumer payloads")
    return result


def same_outputs(first, second, label):
    """Compare deterministic results across rounds without comparing their timings."""
    for name in set(first) & set(second):
        before, after = first[name]["pairs"][0], second[name]["pairs"][0]
        require(before["checksum"] == after["checksum"], f"{label}/{name}: changed payload across rounds")
        for key in ("baseline_direction_reads", "candidate_direction_reads",
                    "baseline_query_calls", "candidate_query_calls",
                    "baseline_copies", "candidate_copies", "baseline_copied_bytes", "candidate_copied_bytes"):
            if key in before:
                require(before[key] == after[key], f"{label}/{name}: changed deterministic work count {key}")


if __name__ == "__main__":
    directory = Path(__file__).resolve().parent
    output = {
        "interpretation": (
            "Synthetic batch timings. Within each round, baseline means the original fixture and "
            "candidate means that round's production helper. Ratios are original/candidate, with "
            "seven alternating pairs; below one is a regression. Paired ratio medians need not "
            "equal ratios of timing medians. Separate rounds are not interleaved comparisons of "
            "old and new helpers: do not divide their ratios or timings to claim an improvement. "
            "The conveyor and power emitters compare both outputs before printing one shared checksum; "
            "this parser checks its consistency across all pairs and matching rounds. These "
            "fixtures do not measure Unity/Mono, GPU upload, workload frequency or game FPS."
        ),
        "conveyor": conveyor(directory / "candidate-conveyor.log"),
        "animation": animation(directory / "candidate-animation.log", expected_groups=14),
        "power": power(directory / "candidate-power.log"),
        "baseline_conveyor": conveyor(directory / "baseline-conveyor.log"),
        "baseline_animation": animation(directory / "baseline-animation.log", expected_groups=8),
        "baseline_power": power(directory / "baseline-power.log"),
    }
    same_outputs(output["baseline_conveyor"], output["conveyor"], "conveyor")
    same_outputs(output["baseline_animation"], output["animation"], "animation")
    same_outputs(output["baseline_power"], output["power"], "power")
    previous = directory / "previous-helper-animation.log"
    if previous.exists():
        output["previous_helper_animation"] = animation(previous, expected_groups=14)
        same_outputs(output["previous_helper_animation"], output["animation"], "previous helper animation")
    # Validate complete samples above, then keep their exact source log instead
    # of duplicating every raw record in the derived summary.
    for round_name, source_log in {
        "conveyor": "candidate-conveyor.log",
        "animation": "candidate-animation.log",
        "power": "candidate-power.log",
        "baseline_conveyor": "baseline-conveyor.log",
        "baseline_animation": "baseline-animation.log",
        "baseline_power": "baseline-power.log",
        "previous_helper_animation": "previous-helper-animation.log",
    }.items():
        if round_name in output:
            for summary in output[round_name].values():
                summary["pairs_count"] = len(summary.pop("pairs"))
                summary["source_log"] = source_log
    (directory / "benchmarks.json").write_text(
        json.dumps(output, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print("Wrote benchmarks.json after validating every retained paired sample.")
