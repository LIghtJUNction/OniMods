#!/usr/bin/env python3
"""Executable regression for moving-head ONI reference drift classification."""

from verify_oni_reference_provenance import compare_upstream_state


PINNED_COMMIT = "a" * 40
PINNED_MARKER = "d" * 40
ASSEMBLY_CSHARP = "b" * 40
ASSEMBLY_FIRSTPASS = "c" * 40

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
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


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
    changed_files["Lib/Assembly-CSharp.dll"] = {"sha": "f" * 40}
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
        binary_drift["changed_files"] == ["Lib/Assembly-CSharp.dll"],
        "changed reference file was not identified exactly",
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

    incomplete_files = {"Lib/Assembly-CSharp.dll": {"sha": ASSEMBLY_CSHARP}}
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

    print("PASS: upstream reference drift classification")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
