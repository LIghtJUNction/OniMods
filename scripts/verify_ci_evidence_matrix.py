#!/usr/bin/env python3
"""Lock the CI evidence matrix so synthetic timing cannot masquerade as correctness."""

from fnmatch import fnmatchcase
from pathlib import Path
import sys

from run_cycletrim_synthetic_performance import (
    SYNTHETIC_TIMING_REGRESSION_MARKER,
)
from verify_oni_reference_provenance_contract import workflow_paths


ROOT = Path(__file__).resolve().parents[1]
REFERENCE_WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"
MOD_QUALITY_WORKFLOW = ROOT / ".github/workflows/mod-quality.yml"
PERFORMANCE_PROGRAM = ROOT / "tests/CycleTrim.PerformanceProbe.Tests/Program.cs"
HOST_REGRESSION_COMMAND = "python3 scripts/check_mods.py --skip-synthetic-performance"
SYNTHETIC_REPORT_COMMAND = "python3 scripts/run_cycletrim_synthetic_performance.py"
SYNTHETIC_ONLY_ARGUMENT = "--synthetic-performance-only"
SKIP_SYNTHETIC_ARGUMENT = "--skip-synthetic-performance"
NETSTANDARD_COMPAT_PROJECTS = (
    "mods/CycleTrim/CycleTrim.csproj",
    "mods/OniMcp/OniMcp.csproj",
)
STEAM_PUBLISHER_TEST_INPUTS = (
    "tools/OniMods.SteamPublisher/CandidateCreationJournal.cs",
    "tools/OniMods.SteamPublisher/LegacyCandidatePlan.cs",
    "tools/OniMods.SteamPublisher/LegacyPackage.cs",
    "tools/OniMods.SteamPublisher/WorkshopMetadata.cs",
    "tools/OniMods.SteamPublisher/WorkshopTarget.cs",
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def verify_netstandard_compatibility_coverage(reference: str) -> None:
    for project in NETSTANDARD_COMPAT_PROJECTS:
        require(
            project in reference,
            f"reference CI must compile {project} in the .NET Standard 2.1 compatibility probe",
        )
    require(
        reference.count("TargetFramework=netstandard2.1") >= len(NETSTANDARD_COMPAT_PROJECTS) * 2,
        "reference CI must restore and compile both mods with TargetFramework=netstandard2.1",
    )


def verify_steam_publisher_trigger_coverage() -> None:
    workflows = (
        ("reference CI", REFERENCE_WORKFLOW),
        ("Mod quality", MOD_QUALITY_WORKFLOW),
    )
    for workflow_name, workflow in workflows:
        for event_name in ("pull_request", "push"):
            patterns = workflow_paths(workflow, event_name)
            for path in STEAM_PUBLISHER_TEST_INPUTS:
                require(
                    any(fnmatchcase(path, pattern) for pattern in patterns),
                    f"{workflow_name} {event_name} does not cover Steam publisher test input {path}",
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
            "reference compatibility must not execute the synthetic timing probe",
        )
        require(
            SYNTHETIC_REPORT_COMMAND in quality,
            "Mod quality must explicitly report CycleTrim synthetic performance evidence",
        )
        require(
            SYNTHETIC_ONLY_ARGUMENT not in quality,
            "Mod quality must route wall-clock timing through the evidence reporter",
        )
        require(
            SKIP_SYNTHETIC_ARGUMENT in performance_program
            and SYNTHETIC_ONLY_ARGUMENT in performance_program,
            "performance probe executable must expose separate host and synthetic modes",
        )
        require(
            SYNTHETIC_TIMING_REGRESSION_MARKER in performance_program,
            "the managed timing-only verdict must emit the reporter marker",
        )
        verify_netstandard_compatibility_coverage(reference)
        verify_steam_publisher_trigger_coverage()
    except (AssertionError, OSError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print("PASS: CI separates correctness evidence from synthetic wall-clock timing and covers compatibility/publisher inputs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())