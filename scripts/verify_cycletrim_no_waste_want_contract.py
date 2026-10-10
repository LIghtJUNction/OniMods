#!/usr/bin/env python3
"""Verify CycleTrim's pinned Waste Not, Want Not fetch compatibility assumptions."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "scripts/upstream/cycletrim-no-waste-want.json"
CYCLETRIM_FETCH_PATCH = ROOT / "mods/CycleTrim/Patches/FetchPickupCandidatePatch.cs"
RAW_ROOT = "https://raw.githubusercontent.com"


def fail(message: str, failures: list[str]) -> None:
    failures.append(message)


def require_text(source: str, needle: str, message: str, failures: list[str]) -> None:
    if needle not in source:
        fail(message, failures)


def require_regex(source: str, pattern: str, message: str, failures: list[str]) -> None:
    if re.search(pattern, source, re.MULTILINE | re.DOTALL) is None:
        fail(message, failures)


def git_blob_sha1(payload: bytes) -> str:
    header = f"blob {len(payload)}\0".encode("ascii")
    return hashlib.sha1(header + payload).hexdigest()


def fetch_pinned_source(repository: str, commit: str, path: str) -> bytes:
    url = f"{RAW_ROOT}/{repository}/{commit}/{path}"
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "OniMods-CycleTrim-NoWasteWant-contract/1"},
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def verify_local(manifest: dict, failures: list[str]) -> None:
    if manifest.get("schemaVersion") != 1:
        fail("manifest schemaVersion must be 1", failures)
    if manifest.get("repository") != "peterhaneve/ONIMods":
        fail("manifest repository must stay pinned to peterhaneve/ONIMods", failures)

    commit = manifest.get("commit")
    if not isinstance(commit, str) or re.fullmatch(r"[0-9a-f]{40}", commit) is None:
        fail("manifest commit must be a full 40-character Git SHA", failures)

    files = manifest.get("files")
    if not isinstance(files, list) or len(files) != 1:
        fail("manifest must pin exactly one Waste Not, Want Not source file", failures)
    else:
        item = files[0]
        if item.get("path") != "NoWasteWant/NoWasteWantPatches.cs":
            fail("manifest Waste Not, Want Not source path changed", failures)
        blob = item.get("gitBlobSha1")
        if not isinstance(blob, str) or re.fullmatch(r"[0-9a-f]{40}", blob) is None:
            fail("manifest Waste Not, Want Not Git blob SHA is invalid", failures)

    patch = CYCLETRIM_FETCH_PATCH.read_text(encoding="utf-8")
    require_text(
        patch,
        '"PeterHan.NoWasteWant.NoWasteWantPatches"',
        "CycleTrim Fetch is missing the Waste Not, Want Not type marker",
        failures,
    )
    require_regex(
        patch,
        r"Prepare\(\).*?if\s*\(\s*AccessTools\.TypeByName\(NoWasteWantPatchType\)\s*"
        r"!=\s*null\s*\)\s*\{\s*return\s+false\s*;\s*\}.*?"
        r"return\s+AccessTools\.TypeByName\(FastTrackPatchType\)\s*==\s*null\s*;",
        "CycleTrim Fetch no longer disables its replacement when Waste Not, Want Not is present",
        failures,
    )


def verify_upstream(manifest: dict, failures: list[str]) -> None:
    repository = manifest["repository"]
    commit = manifest["commit"]
    item = manifest["files"][0]
    path = item["path"]
    try:
        payload = fetch_pinned_source(repository, commit, path)
    except (OSError, urllib.error.URLError) as error:
        fail(f"failed to download pinned Waste Not, Want Not source {path}: {error}", failures)
        return

    actual_blob = git_blob_sha1(payload)
    if actual_blob != item["gitBlobSha1"]:
        fail(
            f"{path} Git blob mismatch: expected {item['gitBlobSha1']}, got {actual_blob}",
            failures,
        )
        return

    source = payload.decode("utf-8-sig")
    require_regex(
        source,
        r"namespace\s+PeterHan\.NoWasteWant\s*\{.*?"
        r"public\s+sealed\s+class\s+NoWasteWantPatches\s*:\s*KMod\.UserMod2",
        "Waste Not, Want Not patch class/type marker changed",
        failures,
    )
    require_regex(
        source,
        r"AlignFreshness\(.*?if\s*\(target\s*!=\s*null\s*&&\s*!target\.FoodInfo\.CanRot\)"
        r"\s*oldFreshness\s*=\s*int\.MaxValue\s*;",
        "Waste Not, Want Not non-rottable freshness rewrite changed",
        failures,
    )
    require_text(
        source,
        "FetchManager_FetchablesByPrefabId_AddPickupable_Patch",
        "Waste Not, Want Not AddPickupable patch marker changed",
        failures,
    )
    require_regex(
        source,
        r"class\s+FetchManager_PickupComparerIncludingPriority_Patch.*?"
        r"internal\s+static\s+IEnumerable<CodeInstruction>\s+Transpiler\(.*?"
        r"return\s+TranspileNegateLast\(method\)\s*;",
        "Waste Not, Want Not priority comparator freshness reversal changed",
        failures,
    )
    require_regex(
        source,
        r"TranspileNegateLast\(.*?newMethod\.Insert\(i\s*\+\s*1,\s*"
        r"new\s+CodeInstruction\(OpCodes\.Neg\)\)\s*;",
        "Waste Not, Want Not CompareTo negation changed",
        failures,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--verify-upstream",
        action="store_true",
        help="download and verify the source pinned by the manifest",
    )
    args = parser.parse_args()
    failures: list[str] = []
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    verify_local(manifest, failures)
    if args.verify_upstream:
        verify_upstream(manifest, failures)

    if failures:
        for message in failures:
            print(f"FAIL {message}", file=sys.stderr)
        return 1
    print("PASS CycleTrim Waste Not, Want Not compatibility contract")
    return 0


if __name__ == "__main__":
    sys.exit(main())
