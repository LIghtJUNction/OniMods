#!/usr/bin/env python3
"""Lock the CI evidence matrix so synthetic timing cannot masquerade as reference compatibility."""

from pathlib import Path
import sys

from check_mods import SYNTHETIC_PERFORMANCE_PROJECT, build_project_command


ROOT = Path(__file__).resolve().parents[1]
REFERENCE_WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"
MOD_QUALITY_WORKFLOW = ROOT / ".github/workflows/mod-quality.yml"
PERFORMANCE_PROGRAM = ROOT / "tests/CycleTrim.PerformanceProbe.Tests/Program.cs"
HOST_REGRESSION_COMMAND = "python3 scripts/check_mods.py --skip-synthetic-performance"
SYNTHETIC_ONLY_ARGUMENT = "--synthetic-performance-only"
SKIP_SYNTHETIC_ARGUMENT = "--skip-synthetic-performance"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def verify_project_command_selection() -> None:
    performance_host = build_project_command(
        SYNTHETIC_PERFORMANCE_PROJECT,
        "dotnet",
        skip_synthetic_performance=True,
    )
    performance_full = build_project_command(
        SYNTHETIC_PERFORMANCE_PROJECT,
        "dotnet",
        skip_synthetic_performance=False,
    )
    normal_project = ROOT / "tests/OniMcp.Core.Tests/OniMcp.Core.Tests.csproj"
    normal_host = build_project_command(
        normal_project,
        "dotnet",
        skip_synthetic_performance=True,
    )

    require(
        performance_host[-2:] == ["--", SKIP_SYNTHETIC_ARGUMENT],
        "host mode must skip timing only for CycleTrim.PerformanceProbe.Tests",
    )
    require(
        SKIP_SYNTHETIC_ARGUMENT not in performance_full,
        "default performance-probe execution must retain the timing gate",
    )
    require(
        SKIP_SYNTHETIC_ARGUMENT not in normal_host,
        "host mode must not alter unrelated regression projects",
    )


def main() -> int:
    try:
        reference = REFERENCE_WORKFLOW.read_text(encoding="utf-8")
        quality = MOD_QUALITY_WORKFLOW.read_text(encoding="utf-8")
        performance_program = PERFORMANCE_PROGRAM.read_text(encoding="utf-8")

        require(
            HOST_REGRESSION_COMMAND in reference,
            "reference CI must run host regressions without synthetic performance timing",
        )
        require(
            HOST_REGRESSION_COMMAND in quality,
            "Mod quality must use the same non-synthetic host regression set",
        )
        require(
            SYNTHETIC_ONLY_ARGUMENT not in reference,
            "reference compatibility must not execute the synthetic timing gate",
        )
        require(
            SYNTHETIC_ONLY_ARGUMENT in quality,
            "Mod quality must explicitly retain CycleTrim synthetic performance coverage",
        )
        require(
            SKIP_SYNTHETIC_ARGUMENT in performance_program
            and SYNTHETIC_ONLY_ARGUMENT in performance_program,
            "performance probe executable must expose separate host and synthetic modes",
        )
        verify_project_command_selection()
    except (AssertionError, OSError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print("PASS: CI separates host/reference evidence from synthetic performance timing")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
