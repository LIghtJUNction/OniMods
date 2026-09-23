#!/usr/bin/env python3
"""Lock the CI evidence matrix so synthetic timing cannot masquerade as correctness."""

from fnmatch import fnmatchcase
from pathlib import Path
import sys

from check_mods import SYNTHETIC_PERFORMANCE_PROJECT, build_project_command
from run_cycletrim_synthetic_performance import (
    SYNTHETIC_TIMING_REGRESSION_MARKER,
    build_probe_command,
    classify_probe_exit_code,
)


ROOT = Path(__file__).resolve().parents[1]
REFERENCE_WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"
MOD_QUALITY_WORKFLOW = ROOT / ".github/workflows/mod-quality.yml"
PERFORMANCE_PROGRAM = ROOT / "tests/CycleTrim.PerformanceProbe.Tests/Program.cs"
HOST_REGRESSION_COMMAND = "python3 scripts/check_mods.py --skip-synthetic-performance"
SYNTHETIC_REPORT_COMMAND = "python3 scripts/run_cycletrim_synthetic_performance.py"
SYNTHETIC_ONLY_ARGUMENT = "--synthetic-performance-only"
SKIP_SYNTHETIC_ARGUMENT = "--skip-synthetic-performance"
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


def workflow_paths(workflow: Path, event_name: str) -> list[str]:
    lines = workflow.read_text(encoding="utf-8").splitlines()
    event_header = f"  {event_name}:"
    try:
        start = lines.index(event_header)
    except ValueError as error:
        raise AssertionError(f"missing {event_name} trigger in {workflow.name}") from error

    paths_start = None
    for index in range(start + 1, len(lines)):
        line = lines[index]
        if line.startswith("  ") and not line.startswith("    ") and line.strip():
            break
        if line == "    paths:":
            paths_start = index + 1
            break
    if paths_start is None:
        raise AssertionError(f"missing {event_name}.paths trigger list in {workflow.name}")

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


def verify_synthetic_reporter_policy() -> None:
    command = build_probe_command("dotnet")
    require(
        command[-2:] == ["--", SYNTHETIC_ONLY_ARGUMENT],
        "synthetic reporter must execute only the CycleTrim timing probe",
    )
    require(
        str(SYNTHETIC_PERFORMANCE_PROJECT) in command,
        "synthetic reporter must run the existing performance-probe project",
    )
    require(
        classify_probe_exit_code(0) == 0,
        "a successful synthetic probe must remain successful",
    )
    require(
        classify_probe_exit_code(2, SYNTHETIC_TIMING_REGRESSION_MARKER + " threshold") == 0,
        "a marked wall-clock threshold breach must be advisory",
    )
    require(
        classify_probe_exit_code(2, "MSBUILD tool failure") == 2,
        "an unmarked tool exit using the same numeric code must remain a failure",
    )
    require(
        classify_probe_exit_code(1, SYNTHETIC_TIMING_REGRESSION_MARKER + " threshold") == 1,
        "a real probe/assertion failure must remain a CI failure",
    )
    require(
        classify_probe_exit_code(137) == 137,
        "unexpected probe failures must not be swallowed by the advisory reporter",
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
        verify_steam_publisher_trigger_coverage()
        verify_project_command_selection()
        verify_synthetic_reporter_policy()
    except (AssertionError, OSError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print("PASS: CI separates correctness evidence from synthetic wall-clock timing and covers publisher inputs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
