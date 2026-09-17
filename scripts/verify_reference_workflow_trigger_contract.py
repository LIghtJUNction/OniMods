#!/usr/bin/env python3
"""Verify that reference CI triggers cover executable regression inputs."""

from fnmatch import fnmatchcase
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"
CHECK_MODS = ROOT / "scripts/check_mods.py"
BENCHMARK_PROJECT = "benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def workflow_paths(event_name: str) -> list[str]:
    lines = WORKFLOW.read_text(encoding="utf-8").splitlines()
    event_header = f"  {event_name}:"
    try:
        start = lines.index(event_header)
    except ValueError as error:
        raise AssertionError(f"missing {event_name} trigger") from error

    paths_start = None
    for index in range(start + 1, len(lines)):
        line = lines[index]
        if line.startswith("  ") and not line.startswith("    ") and line.strip():
            break
        if line == "    paths:":
            paths_start = index + 1
            break
    if paths_start is None:
        raise AssertionError(f"missing {event_name}.paths trigger list")

    paths = []
    for line in lines[paths_start:]:
        if not line.startswith("      - "):
            if line.strip():
                break
            continue
        value = line[len("      - "):].strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
            value = value[1:-1]
        paths.append(value)
    return paths


def main() -> int:
    require((ROOT / BENCHMARK_PROJECT).is_file(), "CycleTrim benchmark project is missing")

    check_mods_source = CHECK_MODS.read_text(encoding="utf-8")
    require(
        "benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj"
        in check_mods_source,
        "reference regression runner no longer executes the CycleTrim benchmark project",
    )

    for event_name in ("pull_request", "push"):
        patterns = workflow_paths(event_name)
        require(
            any(fnmatchcase(BENCHMARK_PROJECT, pattern) for pattern in patterns),
            f"{event_name} reference CI does not cover executable benchmark input "
            f"{BENCHMARK_PROJECT}",
        )

    print("PASS: reference CI trigger coverage includes executable benchmark inputs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
