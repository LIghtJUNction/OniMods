#!/usr/bin/env python3
"""Executable regressions for ONI reference provenance and CI coverage."""

from fnmatch import fnmatchcase
from pathlib import Path

from verify_oni_reference_provenance import compare_upstream_state, validate_manifest


ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = ROOT / ".github/workflows/oni-reference-compat.yml"
PINNED_COMMIT = "a" * 40
PINNED_MARKER = "d" * 40
ASSEMBLY_CSHARP = "b" * 40
ASSEMBLY_FIRSTPASS = "c" * 40
UNITY_ENGINE = "9" * 40

REFERENCE = {
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
    ".agents/skills/autonomous-gh-iteration/SKILL.md",
    "Directory.Build.props.example",
)


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


def verify_workflow_contract_inputs() -> None:
    for event_name in ("pull_request", "push"):
        patterns = workflow_paths(event_name)
        for path in REFERENCE_CONTRACT_INPUTS:
            require(
                any(fnmatchcase(path, pattern) for pattern in patterns),
                f"{event_name} reference CI does not cover contract input {path}",
            )


def manifest_with_tracking(tracked_paths: list[str]) -> dict:
    blob_by_path = {
        "Lib/Assembly-CSharp.dll": ASSEMBLY_CSHARP,
        "Lib/UnityEngine.dll": UNITY_ENGINE,
    }
    return {
        "schema": 1,
        "official_oni_build": 744825,
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

    print("PASS: upstream reference drift classification and CI input coverage")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
