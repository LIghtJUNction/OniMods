#!/usr/bin/env python3
"""Verify pinned Research queue source semantics used by OniMcp."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from verify_cycletrim_navgrid_source_contract import download_source, method_body, require


def verify_set_active_contract(set_active_body: str) -> None:
    require(
        r"\A\s*if\s*\(\s*clearQueue\s*\)\s*\{\s*this\.queuedTech\.Clear\s*\(\s*\)\s*;\s*\}\s*"
        r"this\.activeResearch\s*=\s*null\s*;\s*"
        r"if\s*\(\s*tech\s*!=\s*null\s*\)",
        set_active_body,
        "SetActiveResearch must unconditionally reset activeResearch before the tech branch",
    )

    for pattern, message in (
        (
            r"if\s*\(\s*tech\s*!=\s*null\s*\).*?\}\s*else\s*\{\s*"
            r"this\.queuedTech\.Clear\s*\(\s*\)\s*;\s*\}",
            "SetActiveResearch null path no longer clears queuedTech",
        ),
        (
            r"this\.NotifyResearchCenters\s*\(\s*GameHashes\.ActiveResearchChanged\s*,\s*"
            r"this\.queuedTech\s*\)\s*;",
            "SetActiveResearch no longer emits ActiveResearchChanged",
        ),
    ):
        require(pattern, set_active_body, message)

    require(
        r"else\s*\{\s*this\.queuedTech\.Clear\s*\(\s*\)\s*;\s*\}\s*"
        r"this\.NotifyResearchCenters\s*\(",
        set_active_body,
        "queue clear must occur before notification",
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="ci/oni-reference-assemblies.json")
    args = parser.parse_args()

    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    source = manifest["method_body_source"]
    expected_build = int(manifest["official_oni_build"])
    if int(source["official_oni_build"]) != expected_build:
        raise ValueError("method-body source build does not match manifest official_oni_build")

    files = {str(item["name"]): item for item in source["files"]}
    repository = str(source["repository"])
    commit = str(source["commit"])
    research = download_source(repository, commit, files["Research.cs"])
    klei_version = download_source(repository, commit, files["KleiVersion.cs"])

    require(
        rf"public\s+const\s+uint\s+ChangeList\s*=\s*{expected_build}U\s*;",
        klei_version,
        f"KleiVersion.ChangeList is not {expected_build}",
    )
    require(
        r'public\s+const\s+string\s+BuildBranch\s*=\s*"release"\s*;',
        klei_version,
        "pinned source is not a release build",
    )

    queue_body = method_body(research, "public List<TechInstance> GetResearchQueue()")
    require(
        r"return\s+new\s+List<TechInstance>\s*\(\s*this\.queuedTech\s*\)\s*;",
        queue_body,
        "GetResearchQueue no longer returns a defensive copy of queuedTech",
    )

    target_body = method_body(research, "public TechInstance GetTargetResearch()")
    require(
        r"if\s*\(\s*this\.queuedTech\s*!=\s*null\s*&&\s*"
        r"this\.queuedTech\.Count\s*>\s*0\s*\).*?"
        r"return\s+this\.queuedTech\s*\[\s*this\.queuedTech\.Count\s*-\s*1\s*\]\s*;.*?"
        r"return\s+null\s*;",
        target_body,
        "GetTargetResearch no longer derives the target from queuedTech",
    )

    set_active_body = method_body(
        research,
        "public void SetActiveResearch(Tech tech, bool clearQueue = false)",
    )
    verify_set_active_contract(set_active_body)

    print(
        f"PASS Research queue source contract matches ONI {expected_build} "
        f"at {repository}@{commit}"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
