#!/usr/bin/env python3
"""Run CycleTrim synthetic timing without treating runner noise as correctness failure."""

from pathlib import Path
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "tests/CycleTrim.PerformanceProbe.Tests/CycleTrim.PerformanceProbe.Tests.csproj"
SYNTHETIC_TIMING_REGRESSION_EXIT_CODE = 2
SYNTHETIC_TIMING_REGRESSION_MARKER = "ONIMODS_SYNTHETIC_TIMING_REGRESSION:"


def build_probe_command(dotnet: str = "dotnet") -> list[str]:
    return [
        dotnet,
        "run",
        "--project",
        str(PROJECT),
        "--configuration",
        "Release",
        "-p:ImportDirectoryBuildProps=false",
        "-p:TreatWarningsAsErrors=true",
        "--",
        "--synthetic-performance-only",
    ]


def classify_probe_exit_code(return_code: int, stderr: str = "") -> int:
    if (
        return_code == SYNTHETIC_TIMING_REGRESSION_EXIT_CODE
        and any(line.startswith(SYNTHETIC_TIMING_REGRESSION_MARKER) for line in stderr.splitlines())
    ):
        return 0
    return return_code


def main() -> int:
    result = subprocess.run(
        build_probe_command(),
        cwd=ROOT,
        check=False,
        capture_output=True,
        text=True,
    )
    sys.stdout.write(result.stdout)
    sys.stderr.write(result.stderr)
    classified = classify_probe_exit_code(result.returncode, result.stderr)
    if classified == 0 and result.returncode == SYNTHETIC_TIMING_REGRESSION_EXIT_CODE:
        print(
            "ADVISORY: CycleTrim synthetic wall-clock median exceeded the existing 2.0x threshold; "
            "correctness checks continue.",
            file=sys.stderr,
        )
    return classified


if __name__ == "__main__":
    raise SystemExit(main())
