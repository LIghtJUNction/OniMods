#!/usr/bin/env python3
"""Repeat CycleTrim's NavGrid gate benchmark in independent processes."""

from __future__ import annotations

import argparse
import re
import statistics
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj"
LINE_RE = re.compile(
    r"^gate-attack: layout=(?P<layout>[a-z]+), rawSeeds=(?P<raw>\d+), "
    r"uniqueSeeds=(?P<unique>\d+), range=(?P<range_x>\d+)x(?P<range_y>\d+), "
    r"route=(?P<route>[a-z]+), expanded=(?P<expanded>\d+), "
    r"vanilla=(?P<vanilla>[0-9.]+) ms, adaptive=(?P<adaptive>[0-9.]+) ms, "
    r"speedup=(?P<speedup>[0-9.]+)x, allocated=(?P<vanilla_alloc>\d+)/"
    r"(?P<adaptive_alloc>\d+) B$"
)


@dataclass(frozen=True)
class CaseKey:
    layout: str
    raw_seeds: int
    unique_seeds: int
    range_x: int
    range_y: int
    route: str


@dataclass(frozen=True)
class Sample:
    expanded: int
    vanilla_ms: float
    adaptive_ms: float
    vanilla_alloc: int
    adaptive_alloc: int

    @property
    def speedup(self) -> float:
        return self.vanilla_ms / self.adaptive_ms


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--runs", type=int, default=5)
    return parser.parse_args()


def run_once(run_index: int) -> dict[CaseKey, Sample]:
    command = [
        "dotnet",
        "run",
        "--no-build",
        "--project",
        str(PROJECT),
        "--configuration",
        "Release",
        "-p:ImportDirectoryBuildProps=false",
        "-p:TreatWarningsAsErrors=true",
        "--",
        "--benchmark",
    ]
    result = subprocess.run(
        command,
        cwd=ROOT,
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        sys.stderr.write(result.stdout)
        sys.stderr.write(result.stderr)
        raise RuntimeError(f"NavGrid stability process {run_index} failed")

    cases: dict[CaseKey, Sample] = {}
    for line in result.stdout.splitlines():
        match = LINE_RE.match(line)
        if not match:
            continue
        key = CaseKey(
            layout=match.group("layout"),
            raw_seeds=int(match.group("raw")),
            unique_seeds=int(match.group("unique")),
            range_x=int(match.group("range_x")),
            range_y=int(match.group("range_y")),
            route=match.group("route"),
        )
        sample = Sample(
            expanded=int(match.group("expanded")),
            vanilla_ms=float(match.group("vanilla")),
            adaptive_ms=float(match.group("adaptive")),
            vanilla_alloc=int(match.group("vanilla_alloc")),
            adaptive_alloc=int(match.group("adaptive_alloc")),
        )
        if key in cases:
            raise RuntimeError(f"duplicate gate case in process {run_index}: {key}")
        cases[key] = sample

    if not cases:
        raise RuntimeError(f"NavGrid stability process {run_index} produced no gate cases")
    print(f"process-stability: completed process {run_index} with {len(cases)} gate cases")
    return cases


def is_stability_target(key: CaseKey) -> bool:
    if key.route != "bitset" or key.unique_seeds != 16:
        return False
    if (key.range_x, key.range_y) in {(2, 4), (4, 2)}:
        return True
    return key.layout in {"edge", "mixed", "duplicateheavy"}


def main() -> int:
    args = parse_args()
    if args.runs < 3:
        print("FAIL: --runs must be at least 3 for a process-level median", file=sys.stderr)
        return 2

    process_results = [run_once(index + 1) for index in range(args.runs)]
    expected_keys = set(process_results[0])
    for index, cases in enumerate(process_results[1:], start=2):
        if set(cases) != expected_keys:
            print(f"FAIL: gate case set changed in process {index}", file=sys.stderr)
            return 1

    targets = sorted(
        (key for key in expected_keys if is_stability_target(key)),
        key=lambda key: (
            key.layout,
            key.raw_seeds,
            key.unique_seeds,
            key.range_x,
            key.range_y,
        ),
    )
    if not targets:
        print("FAIL: no 16-cell candidate stability targets found", file=sys.stderr)
        return 1

    overall_min = float("inf")
    overall_key: CaseKey | None = None
    for key in targets:
        samples = [cases[key] for cases in process_results]
        expanded = samples[0].expanded
        if any(sample.expanded != expanded for sample in samples):
            print(f"FAIL: expanded count changed across processes for {key}", file=sys.stderr)
            return 1
        if any(sample.vanilla_alloc != 0 or sample.adaptive_alloc != 0 for sample in samples):
            print(f"FAIL: managed allocation appeared across processes for {key}", file=sys.stderr)
            return 1

        speedups = [sample.speedup for sample in samples]
        minimum = min(speedups)
        median = statistics.median(speedups)
        maximum = max(speedups)
        if minimum < overall_min:
            overall_min = minimum
            overall_key = key
        print(
            "process-stability: "
            f"layout={key.layout}, rawSeeds={key.raw_seeds}, uniqueSeeds={key.unique_seeds}, "
            f"range={key.range_x}x{key.range_y}, expanded={expanded}, runs={args.runs}, "
            f"min={minimum:.3f}x, median={median:.3f}x, max={maximum:.3f}x"
        )

    assert overall_key is not None
    print(
        "process-stability-min: "
        f"layout={overall_key.layout}, rawSeeds={overall_key.raw_seeds}, "
        f"uniqueSeeds={overall_key.unique_seeds}, "
        f"range={overall_key.range_x}x{overall_key.range_y}, min={overall_min:.3f}x"
    )
    if overall_min <= 1.0:
        print(
            "process-stability-note: at least one independent process did not show a CPU win; "
            "do not promote the 16-cell gate to runtime from this evidence."
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
