#!/usr/bin/env python3
"""Lock the CI evidence matrix so synthetic timing cannot masquerade as reference compatibility."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
REFERENCE_WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"
MOD_QUALITY_WORKFLOW = ROOT / ".github/workflows/mod-quality.yml"
CHECK_MODS = ROOT / "scripts/check_mods.py"
PERFORMANCE_PROJECT = "tests/CycleTrim.PerformanceProbe.Tests/CycleTrim.PerformanceProbe.Tests.csproj"
HOST_REGRESSION_COMMAND = "python3 scripts/check_mods.py --exclude-synthetic-performance"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> int:
    try:
        reference = REFERENCE_WORKFLOW.read_text(encoding="utf-8")
        quality = MOD_QUALITY_WORKFLOW.read_text(encoding="utf-8")
        runner = CHECK_MODS.read_text(encoding="utf-8")

        require(
            HOST_REGRESSION_COMMAND in reference,
            "reference CI must run host regressions without synthetic performance gates",
        )
        require(
            HOST_REGRESSION_COMMAND in quality,
            "Mod quality must use the same non-synthetic host regression set",
        )
        require(
            PERFORMANCE_PROJECT not in reference,
            "reference compatibility must not be decided by the synthetic timing project",
        )
        require(
            PERFORMANCE_PROJECT in quality,
            "Mod quality must explicitly retain CycleTrim synthetic performance coverage",
        )
        require(
            "--exclude-synthetic-performance" in runner
            and "CycleTrim.PerformanceProbe.Tests" in runner,
            "check_mods must support excluding only the synthetic performance project",
        )
    except (AssertionError, OSError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print("PASS: CI separates host/reference evidence from synthetic performance timing")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
