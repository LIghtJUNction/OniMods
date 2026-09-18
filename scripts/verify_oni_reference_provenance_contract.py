#!/usr/bin/env python3
"""Executable regressions for ONI reference provenance and CI coverage."""

from contextlib import redirect_stdout
from fnmatch import fnmatchcase
from io import StringIO
from pathlib import Path

from verify_oni_reference_provenance import (
    compare_official_steam_state,
    compare_upstream_state,
    report_upstream_state,
    validate_manifest,
)


ROOT = Path(__file__).resolve().parents[1]
REFERENCE_WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"
MOD_QUALITY_WORKFLOW = ROOT / ".github/workflows/mod-quality.yml"
PINNED_DOTNET_SDK = "10.0.401"
PINNED_COMMIT = "a" * 40
PINNED_MARKER = "d" * 40
ASSEMBLY_CSHARP = "b" * 40
ASSEMBLY_FIRSTPASS = "c" * 40
UNITY_ENGINE = "9" * 40
OFFICIAL_RELEASE_GID = "1839041357039119"
OFFICIAL_RELEASE_URL_ID = "719037282029407479"

REFERENCE = {
    "repository": "example/reference",
    "commit": PINNED_COMMIT,
    "upstream_tracking": {
        "branch": "master",
        "files": [
            {
                "path": "Lib/Assembly-CSharp.dll",
                "pinned_blob_sha": ASSEMBLY_CSHARP,
            },
            {
                "path": "Lib/Assembly-CSharp-firstpass.dll",
                "pinned_blob_sha": ASSEMBLY_FIRSTPASS,
            },
            {
                "path": "Lib/UnityEngine.dll",
                "pinned_blob_sha": UNITY_ENGINE,
            },
        ],
    },
}
MARKER = {
    "blob_sha": PINNED_MARKER,
    "property": "TargetGameVersion",
}
MARKER_TEXT = """<Project><PropertyGroup>
<Current>737790</Current>
<TargetGameVersion>$(Current)</TargetGameVersion>
</PropertyGroup></Project>"""
SAME_FILES = {
    "Lib/Assembly-CSharp.dll": {"sha": ASSEMBLY_CSHARP},
    "Lib/Assembly-CSharp-firstpass.dll": {"sha": ASSEMBLY_FIRSTPASS},
    "Lib/UnityEngine.dll": {"sha": UNITY_ENGINE},
}
REFERENCE_CONTRACT_INPUTS = (
    "AGENTS.md",
    "docs/autonomous-iteration.md",
    ".editorconfig",
    ".agents/skills/autonomous-gh-iteration/SKILL.md",
    ".agents/skills/oni-mcp-autonomous-iteration/scripts/runtime_smoke.py",
    "Directory.Build.props.example",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "global.json",
    "NuGet.Config",
    "nuget.config",
    "benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj",
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


def verify_workflow_contract_inputs() -> None:
    workflows = (
        ("reference CI", REFERENCE_WORKFLOW),
        ("Mod quality", MOD_QUALITY_WORKFLOW),
    )
    for workflow_name, workflow in workflows:
        for event_name in ("pull_request", "push"):
            patterns = workflow_paths(workflow, event_name)
            for path in REFERENCE_CONTRACT_INPUTS:
                require(
                    any(fnmatchcase(path, pattern) for pattern in patterns),
                    f"{workflow_name} {event_name} does not cover contract input {path}",
                )


def verify_workflow_sdk_pin() -> None:
    expected = f"dotnet-version: '{PINNED_DOTNET_SDK}'"
    for workflow_name, workflow in (
        ("reference CI", REFERENCE_WORKFLOW),
        ("Mod quality", MOD_QUALITY_WORKFLOW),
    ):
        text = workflow.read_text(encoding="utf-8")
        require(
            expected in text,
            f"{workflow_name} must pin the .NET SDK exactly to {PINNED_DOTNET_SDK}",
        )
        require(
            "dotnet-version: '10.0.x'" not in text,
            f"{workflow_name} must not use a moving 10.0.x SDK selector",
        )


def manifest_with_tracking(tracked_paths: list[str]) -> dict:
    blob_by_path = {
        "Lib/Assembly-CSharp.dll": ASSEMBLY_CSHARP,
        "Lib/UnityEngine.dll": UNITY_ENGINE,
    }
    return {
        "schema": 1,
        "official_oni_build": 744825,
        "official_oni_release_date": "2026-07-28",
        "official_tracking": {
            "steam_app_id": 457140,
            "release_news_gid": OFFICIAL_RELEASE_GID,
            "release_url": (
                "https://store.steampowered.com/news/app/457140/view/"
                + OFFICIAL_RELEASE_URL_ID
            ),
        },
        "reference_source": {
            "repository": "example/reference",
            "commit": PINNED_COMMIT,
            "declared_oni_build": 737790,
            "version_marker": {
                "path": "Directory.Build.props",
                "blob_sha": PINNED_MARKER,
                "property": "TargetGameVersion",
            },
            "upstream_tracking": {
                "branch": "master",
                "files": [
                    {"path": path, "pinned_blob_sha": blob_by_path[path]}
                    for path in tracked_paths
                ],
            },
        },
        "method_body_source": {
            "official_oni_build": 744825,
        },
        "files": [
            {
                "name": path.rsplit("/", 1)[-1],
                "path": path,
                "size": 1,
                "sha256": "0" * 64,
            }
            for path in blob_by_path
        ],
    }


def main() -> int:
    current_news = [
        {
            "gid": "900000000000000000",
            "title": "Aquatic Planet Pack community spotlight",
            "date": 1_788_000_000,
        },
        {
            "gid": OFFICIAL_RELEASE_GID,
            "title": "[Game Update] - 744825",
            "date": 1_775_000_000,
        },
        {
            "gid": "700000000000000000",
            "title": "[Game Update] - 740622",
            "date": 1_765_000_000,
        },
    ]
    official_current = compare_official_steam_state(
        744825,
        OFFICIAL_RELEASE_GID,
        current_news,
    )
    require(
        official_current["observed_build"] == 744825,
        "current official Steam build was not selected",
    )
    require(
        not official_current["has_official_drift"],
        "matching official Steam release was reported as drift",
    )
    require(
        official_current["release_identity_matches"],
        "matching official Steam release identity was not preserved",
    )

    advanced_news = current_news + [
        {
            "gid": "800000000000000001",
            "title": "[Game Hotfix] - 750123",
            "date": 1_789_000_000,
        }
    ]
    official_advanced = compare_official_steam_state(
        744825,
        OFFICIAL_RELEASE_GID,
        advanced_news,
    )
    require(
        official_advanced["observed_build"] == 750123,
        "newer official Steam build was not selected",
    )
    require(
        official_advanced["has_official_drift"],
        "newer official Steam build was missed",
    )
    require(
        not official_advanced["release_identity_matches"],
        "new official Steam release reused the tracked release identity",
    )

    try:
        compare_official_steam_state(
            744825,
            OFFICIAL_RELEASE_GID,
            [{"gid": "1", "title": "DLC announcement", "date": 1_790_000_000}],
        )
    except ValueError:
        pass
    else:
        raise AssertionError("official Steam news without a build marker must be unknown")

    unchanged = compare_upstream_state(
        REFERENCE,
        MARKER,
        737790,
        "e" * 40,
        PINNED_MARKER,
        MARKER_TEXT,
        SAME_FILES,
    )
    require(unchanged["head_advanced"], "advanced HEAD must be reported")
    require(
        not unchanged["has_reference_drift"],
        "repository-only commits must not be classified as reference drift",
    )
    require(unchanged["head_declared_build"] == 737790, "declared build changed")
    require(unchanged["changed_files"] == [], "unchanged blobs reported as drift")

    changed_files = dict(SAME_FILES)
    changed_files["Lib/UnityEngine.dll"] = {"sha": "f" * 40}
    binary_drift = compare_upstream_state(
        REFERENCE,
        MARKER,
        737790,
        "e" * 40,
        PINNED_MARKER,
        MARKER_TEXT,
        changed_files,
    )
    require(binary_drift["has_reference_drift"], "changed reference blob was missed")
    require(
        binary_drift["changed_files"] == ["Lib/UnityEngine.dll"],
        "changed non-game reference file was not identified exactly",
    )
    output = StringIO()
    with redirect_stdout(output):
        report_upstream_state(REFERENCE, binary_drift)
    require(
        "UPSTREAM_REFERENCE_STATUS status=drift " in output.getvalue(),
        "detected upstream reference drift must not be reported as status=ok",
    )

    new_marker_text = """<Project><PropertyGroup>
<Current>744825</Current>
<TargetGameVersion>$(Current)</TargetGameVersion>
</PropertyGroup></Project>"""
    version_drift = compare_upstream_state(
        REFERENCE,
        MARKER,
        737790,
        "e" * 40,
        "1" * 40,
        new_marker_text,
        SAME_FILES,
    )
    require(version_drift["has_reference_drift"], "version-marker drift was missed")
    require(version_drift["marker_changed"], "marker blob change was not recorded")
    require(version_drift["head_declared_build"] == 744825, "new build was not resolved")

    incomplete_files = {
        "Lib/Assembly-CSharp.dll": {"sha": ASSEMBLY_CSHARP},
        "Lib/Assembly-CSharp-firstpass.dll": {"sha": ASSEMBLY_FIRSTPASS},
    }
    try:
        compare_upstream_state(
            REFERENCE,
            MARKER,
            737790,
            "e" * 40,
            PINNED_MARKER,
            MARKER_TEXT,
            incomplete_files,
        )
    except ValueError:
        pass
    else:
        raise AssertionError("missing tracked upstream file must fail closed")

    incomplete_manifest = manifest_with_tracking(["Lib/Assembly-CSharp.dll"])
    try:
        validate_manifest(incomplete_manifest)
    except ValueError as error:
        require(
            "does not cover pinned reference assemblies" in str(error),
            "incomplete tracking failed for an unrelated reason",
        )
    else:
        raise AssertionError("manifest must reject untracked pinned reference assemblies")

    validate_manifest(
        manifest_with_tracking(["Lib/Assembly-CSharp.dll", "Lib/UnityEngine.dll"])
    )
    verify_workflow_contract_inputs()
    verify_workflow_sdk_pin()

    print(
        "PASS: official/upstream reference drift classification, CI input coverage, and SDK pin"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
