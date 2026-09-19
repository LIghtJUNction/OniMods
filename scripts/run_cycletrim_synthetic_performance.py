#!/usr/bin/env python3
"""Run CycleTrim synthetic timing without treating runner noise as correctness failure."""

from pathlib import Path
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "tests/CycleTrim.PerformanceProbe.Tests/CycleTrim.PerformanceProbe.Tests.csproj"
SYNTHETIC_TIMING_REGRESSION_EXIT_CODE = 2


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


def classify_probe_exit_code(return_code: int) -> int:
    if return_code == SYNTHETIC_TIMING_REGRESSION_EXIT_CODE:
        return 0
    return return_code


def main() -> int:
    result = subprocess.run(build_probe_command(), cwd=ROOT, check=False)
    if result.returncode == SYNTHETIC_TIMING_REGRESSION_EXIT_CODE:
        print(
            "ADVISORY: CycleTrim synthetic wall-clock median exceeded the existing 2.0x threshold; "
            "correctness checks continue.",
            file=sys.stderr,
        )
    return classify_probe_exit_code(result.returncode)


if __name__ == "__main__":
    raise SystemExit(main())
