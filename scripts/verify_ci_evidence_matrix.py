#!/usr/bin/env python3
"""Lock the CI evidence matrix so synthetic timing cannot masquerade as reference compatibility."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
REFERENCE_WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"
MOD_QUALITY_WORKFLOW = ROOT / ".github/workflows/mod-quality.yml"
CHECK_MODS = ROOT / "scripts/check_mods.py"
PERFORMANCE_PROGRAM = ROOT / "tests/CycleTrim.PerformanceProbe.Tests/Program.cs"
HOST_REGRESSION_COMMAND = "python3 scripts/check_mods.py --skip-synthetic-performance"
SYNTHETIC_ONLY_ARGUMENT = "--synthetic-performance-only"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> int:
    try:
        reference = REFERENCE_WORKFLOW.read_text(encoding="utf-8")
        quality = MOD_QUALITY_WORKFLOW.read_text(encoding="utf-8")
        runner = CHECK_MODS.read_text(encoding="utf-8")
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
            "--skip-synthetic-performance" in runner
            and "CycleTrim.PerformanceProbe.Tests" in runner,
            "check_mods must keep the probe's host regressions while skipping only timing",
        )
        require(
            "--skip-synthetic-performance" in performance_program
            and SYNTHETIC_ONLY_ARGUMENT in performance_program,
            "performance probe executable must expose separate host and synthetic modes",
        )
    except (AssertionError, OSError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print("PASS: CI separates host/reference evidence from synthetic performance timing")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
