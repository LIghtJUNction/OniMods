#!/usr/bin/env python3
"""Verify pinned ONI robot consumers stay outside the duplicant identity contract."""

from __future__ import annotations

import argparse
import json
import sys
import urllib.request
from pathlib import Path

from verify_cycletrim_navgrid_source_contract import download_source, method_body, require


RAW_PREFIX = "https://raw.githubusercontent.com/"


def download_pinned_path(repository: str, commit: str, path: str) -> str:
    url = f"{RAW_PREFIX}{repository}/{commit}/{path}"
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "OniMods-cycletrim-robot-contract/1"},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read()
    print(f"SOURCE_PROVENANCE {path} commit={commit} bytes={len(data)}")
    return data.decode("utf-8-sig")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="ci/oni-reference-assemblies.json")
    args = parser.parse_args()

    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    source = manifest["method_body_source"]
    expected_build = int(manifest["official_oni_build"])
    if int(source["official_oni_build"]) != expected_build:
        raise ValueError(
            "method-body source build does not match manifest official_oni_build"
        )

    files = {str(item["name"]): item for item in source["files"]}
    repository = str(source["repository"])
    commit = str(source["commit"])
    klei_version = download_source(repository, commit, files["KleiVersion.cs"])
    require(
        rf"public\s+const\s+uint\s+ChangeList\s*=\s*{expected_build}U\s*;",
        klei_version,
        f"KleiVersion.ChangeList is not {expected_build}",
    )

    robot_sources = {
        "BaseRoverConfig": download_pinned_path(
            repository,
            commit,
            "Assembly-CSharp/BaseRoverConfig.cs",
        ),
        "FetchDroneConfig": download_pinned_path(
            repository,
            commit,
            "Assembly-CSharp/FetchDroneConfig.cs",
        ),
    }
    for name, robot_source in robot_sources.items():
        require(
            r"EntityTemplates\.AddCreatureBrain\s*\(",
            robot_source,
            f"{name} no longer uses the creature-brain/chore-consumer path",
        )
        require(
            r"AddOrGet<StandardWorker>\s*\(",
            robot_source,
            f"{name} no longer creates a worker",
        )
        require(
            r"AddOrGet<Navigator>\s*\(",
            robot_source,
            f"{name} no longer creates a navigator",
        )
        require(
            r"new\s+PickupableSensor\s*\(",
            robot_source,
            f"{name} no longer installs PickupableSensor",
        )
        if "MinionIdentity" in robot_source:
            raise ValueError(f"{name} now declares MinionIdentity")

    entity_templates = download_pinned_path(
        repository,
        commit,
        "Assembly-CSharp/EntityTemplates.cs",
    )
    add_creature_brain = method_body(
        entity_templates,
        "public static void AddCreatureBrain(",
    )
    if "MinionIdentity" in add_creature_brain:
        raise ValueError("EntityTemplates.AddCreatureBrain now adds MinionIdentity")

    print(
        f"PASS Rover and Fetch Drone remain non-MinionIdentity pickup consumers in "
        f"ONI {expected_build} source at {repository}@{commit}"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
