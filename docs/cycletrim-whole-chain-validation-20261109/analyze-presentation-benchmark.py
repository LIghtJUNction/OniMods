#!/usr/bin/env python3
"""Validate the fixed nine-scene gate from raw samples, before any rounding."""
import argparse
import csv
import json
import statistics
from pathlib import Path


def analyze(path):
    groups = {}
    reported = {}
    runtime = None
    for line in path.read_text().splitlines():
        if line.startswith("runtime="):
            runtime = dict(item.split("=", 1) for item in line.split(";"))
        elif line.startswith("sample,") and not line.startswith("sample,shape,"):
            fields = next(csv.reader([line]))
            if len(fields) != 14:
                raise ValueError(f"unexpected sample shape: {line}")
            _, shape, scenario, variant, block, position, ticks, iterations, loads, applies, loaded, allocated, revision, digest = fields
            sample = dict(block=int(block), position=int(position), ticks=int(ticks),
                          iterations=int(iterations), loads=int(loads), applies=int(applies),
                          loaded_bytes=int(loaded), managed_allocated_bytes=int(allocated),
                          revision=int(revision), sha256=digest)
            groups.setdefault((shape, scenario, variant), []).append(sample)
        elif line.startswith("summary="):
            values = dict(item.split("=", 1) for item in line.split(";"))
            reported[(values["summary"], values["scenario"], values["variant"])] = values
    expected_keys = {(shape, scene, variant)
                     for shape in ("96x96", "256x384", "736x304")
                     for scene in ("render_repeat", "zero_prepare", "command_two_prepares")
                     for variant in ("original", "revision_gate")}
    if set(groups) != expected_keys or set(reported) != expected_keys:
        raise ValueError("the complete fixed nine-scene paired matrix is required")
    assert runtime is not None
    assert runtime["tiered_compilation"] == "0" and runtime["gpu"] == "0" and runtime["workers"] == "1"
    assert runtime["blocks"] == "17" and runtime["iterations"] == "4" and runtime["warmup_per_sample"] == "4"
    frequency = int(runtime["stopwatch_frequency"])
    assert frequency > 0
    results = []
    for shape in ("96x96", "256x384", "736x304"):
        width, height = map(int, shape.split("x"))
        for scene in ("render_repeat", "zero_prepare", "command_two_prepares"):
            variants = {}
            digests = set()
            positions = set()
            for variant in ("original", "revision_gate"):
                samples = groups[(shape, scene, variant)]
                assert len(samples) == 34, (shape, scene, variant, len(samples))
                expected_uploads = 20 if variant == "original" or scene == "command_two_prepares" else 0
                for sample in samples:
                    assert 0 <= sample["block"] < 17 and 0 <= sample["position"] < 4
                    pair = (sample["block"], sample["position"])
                    assert pair not in positions
                    positions.add(pair)
                    original = ((sample["position"] in (0, 3)) == (sample["block"] % 2 == 0))
                    assert original == (variant == "original"), (shape, scene, sample)
                    assert sample["iterations"] == 4 and sample["ticks"] > 0
                    assert sample["loads"] == sample["applies"] == expected_uploads
                    assert sample["loaded_bytes"] == (width * height * 21 * 4 if expected_uploads else 0)
                    assert sample["revision"] > 0
                    assert len(sample["sha256"]) == 64
                    digests.add(sample["sha256"])
                ticks = sorted(sample["ticks"] for sample in samples)
                values = reported[(shape, scene, variant)]
                assert int(values["samples"]) == len(samples)
                median_ms = statistics.median(ticks) * 1000 / frequency / 4
                p95_ms = ticks[32] * 1000 / frequency / 4
                assert abs(median_ms - float(values["median_ms"])) <= 0.00000051
                assert abs(p95_ms - float(values["p95_sample_mean_ms"])) <= 0.00000051
                variants[variant] = {
                    "sample_count": len(samples),
                    "iterations_per_sample": 4,
                    "median_sample_ticks": statistics.median(ticks),
                    "median_update_ms": median_ms,
                    "p95_sample_mean_update_ms": p95_ms,
                    "reported_min_update_ms": float(values["min_ms"]),
                    "reported_max_update_ms": float(values["max_ms"]),
                    "loads_per_sample": expected_uploads,
                    "applies_per_sample": expected_uploads,
                    "input_bytes_per_sample": samples[0]["loaded_bytes"],
                    "median_measuring_thread_managed_allocated_bytes": statistics.median(
                        sample["managed_allocated_bytes"] for sample in samples),
                }
            assert len(positions) == 68 and len(digests) == 1
            ratio = variants["revision_gate"]["median_sample_ticks"] / variants["original"]["median_sample_ticks"]
            if scene == "command_two_prepares":
                gate = "candidate median <= 103% of baseline"
                accepted = ratio <= 1.03
            elif shape != "96x96":
                gate = "candidate median <= 90% of baseline"
                accepted = ratio <= 0.90
            else:
                gate = "scale control; no timing threshold"
                accepted = True
            results.append(dict(shape=shape, scenario=scene, variants=variants,
                                sha256=digests.pop(), candidate_over_original=ratio,
                                median_reduction_percent=(1.0-ratio)*100,
                                timing_gate=gate, timing_gate_pass=accepted))
    return dict(raw_log=str(path), runtime=runtime, sample_count=sum(map(len, groups.values())),
                correctness_gate_pass=True, timing_gate_pass=all(item["timing_gate_pass"] for item in results),
                scope="same candidate native library; real worker/FFI/descriptor and production managed gate; instrumented Unity CPU-copy stubs; no real graphics upload or game FPS", results=results)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("log", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = analyze(args.log)
    output = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(output)
    print(output, end="")
    raise SystemExit(0 if result["timing_gate_pass"] else 1)
