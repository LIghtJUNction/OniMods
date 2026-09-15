#!/usr/bin/env python3
"""Verify ONI FetchManager semantics reimplemented by CycleTrim's fetch patch.

The check uses only commit-pinned decompiled source already recorded in the ONI
reference manifest. It does not execute, vendor, or redistribute game binaries.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from verify_cycletrim_navgrid_source_contract import download_source, method_body, require


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
    fetch_manager = download_source(repository, commit, files["FetchManager.cs"])
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

    update_body = method_body(
        fetch_manager,
        "public void UpdatePickups(Navigator worker_navigator, int worker)",
    )
    for pattern, message in (
        (
            r"this\.GatherPickupablesWhichCanBePickedUp\s*\(\s*worker\s*\)\s*;\s*"
            r"this\.GatherReachablePickups\s*\(\s*worker_navigator\s*\)\s*;\s*"
            r"this\.finalPickups\.Sort\s*\(\s*"
            r"FetchManager\.PickupComparerIncludingPriority\.CompareInst\s*\)\s*;",
            "UpdatePickups gather/sort order drifted",
        ),
        (
            r"pickup\.masterPriority\s*==\s*pickup2\.masterPriority\s*&&\s*"
            r"tagBitsHash\s*==\s*num",
            "UpdatePickups dedup key drifted from (masterPriority, tagBitsHash)",
        ),
        (
            r"num3\+\+\s*;\s*this\.finalPickups\s*\[\s*num3\s*\]\s*=\s*pickup2\s*;",
            "UpdatePickups in-place survivor compaction drifted",
        ),
        (
            r"this\.finalPickups\.RemoveRange\s*\(\s*num2\s*,\s*"
            r"this\.finalPickups\.Count\s*-\s*num2\s*\)\s*;",
            "UpdatePickups duplicate removal drifted",
        ),
    ):
        require(pattern, update_body, message)

    pickupable_body = method_body(
        fetch_manager,
        "private void GatherPickupablesWhichCanBePickedUp(int worker)",
    )
    for pattern, message in (
        (
            r"this\.pickupsWhichCanBePickedUp\.Clear\s*\(\s*\)\s*;",
            "pickup candidate staging no longer starts from an empty list",
        ),
        (
            r"foreach\s*\(\s*FetchManager\.Fetchable\s+fetchable\s+in\s+"
            r"this\.fetchables\.GetDataList\s*\(\s*\)\s*\)",
            "pickup candidate enumeration source drifted",
        ),
        (
            r"pickupable\.CouldBePickedUpByMinion\s*\(\s*worker\s*\)",
            "pickup eligibility predicate drifted",
        ),
    ):
        require(pattern, pickupable_body, message)

    reachable_body = method_body(
        fetch_manager,
        "private void GatherReachablePickups(Navigator navigator)",
    )
    for pattern, message in (
        (
            r"this\.cellCosts\.Clear\s*\(\s*\)\s*;\s*"
            r"this\.finalPickups\.Clear\s*\(\s*\)\s*;",
            "reachable pickup scratch-state reset drifted",
        ),
        (
            r"this\.cellCosts\.TryGetValue\s*\(\s*pickupable\.cachedCell\s*,\s*out\s+num\s*\)",
            "reachable pickup cell-cost cache lookup drifted",
        ),
        (
            r"pickupable\.GetNavigationCost\s*\(\s*navigator\s*,\s*"
            r"pickupable\.cachedCell\s*\)",
            "reachable pickup navigation-cost query drifted",
        ),
        (
            r"this\.cellCosts\s*\[\s*pickupable\.cachedCell\s*\]\s*=\s*num\s*;",
            "reachable pickup cell-cost cache write drifted",
        ),
        (
            r"if\s*\(\s*num\s*!=\s*-1\s*\)",
            "reachable pickup unreachable sentinel drifted",
        ),
    ):
        require(pattern, reachable_body, message)

    comparer_body = method_body(
        fetch_manager,
        "private static class PickupComparerIncludingPriority",
    )
    require(
        r"a\.tagBitsHash\.CompareTo\s*\(\s*b\.tagBitsHash\s*\).*?"
        r"b\.masterPriority\.CompareTo\s*\(\s*a\.masterPriority\s*\).*?"
        r"a\.PathCost\.CompareTo\s*\(\s*b\.PathCost\s*\).*?"
        r"b\.foodQuality\.CompareTo\s*\(\s*a\.foodQuality\s*\).*?"
        r"b\.freshness\.CompareTo\s*\(\s*a\.freshness\s*\)",
        comparer_body,
        "PickupComparerIncludingPriority ordering drifted",
    )

    print(
        f"PASS FetchablesByPrefabId.UpdatePickups source contract matches ONI "
        f"{expected_build} at {repository}@{commit}"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
