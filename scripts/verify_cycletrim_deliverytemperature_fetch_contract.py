#!/usr/bin/env python3
"""Verify CycleTrim's pinned Delivery Temperature Limit fetch compatibility contract."""

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
MANIFEST = ROOT / "scripts/upstream/cycletrim-deliverytemperature-fetch.json"
CYCLETRIM_FETCH_PATCH = ROOT / "mods/CycleTrim/Patches/FetchPickupCandidatePatch.cs"
CYCLETRIM_COMPAT = ROOT / "mods/CycleTrim/Core/FetchPatchCompatibility.cs"
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


def load_manifest() -> dict:
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def fetch_pinned_source(repository: str, commit: str, path: str) -> bytes:
    url = f"{RAW_ROOT}/{repository}/{commit}/{path}"
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "OniMods-CycleTrim-DTL-contract/1"},
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def verify_local(manifest: dict, failures: list[str]) -> None:
    if manifest.get("schemaVersion") != 1:
        fail("manifest schemaVersion must be 1", failures)

    repository = manifest.get("repository")
    commit = manifest.get("commit")
    if repository != "llunak/oni-deliverytemperaturelimit":
        fail("manifest repository must stay pinned to llunak/oni-deliverytemperaturelimit", failures)
    if not isinstance(commit, str) or re.fullmatch(r"[0-9a-f]{40}", commit) is None:
        fail("manifest commit must be a full 40-character Git SHA", failures)

    files = manifest.get("files")
    if not isinstance(files, list) or not files:
        fail("manifest files must be a non-empty list", failures)
    else:
        expected_paths = {"Source/Patch.cs", "Source/PatchFastTrack.cs", "Source/Mod.cs"}
        seen_paths = set()
        for item in files:
            if not isinstance(item, dict):
                fail("manifest file entries must be objects", failures)
                continue
            path = item.get("path")
            blob = item.get("gitBlobSha1")
            if path in seen_paths:
                fail(f"duplicate manifest path: {path}", failures)
            seen_paths.add(path)
            if not isinstance(blob, str) or re.fullmatch(r"[0-9a-f]{40}", blob) is None:
                fail(f"invalid Git blob SHA for {path!r}", failures)
        if seen_paths != expected_paths:
            fail(
                "manifest paths changed: expected " + ", ".join(sorted(expected_paths)),
                failures,
            )

    compat = CYCLETRIM_COMPAT.read_text(encoding="utf-8")
    fetch_patch = CYCLETRIM_FETCH_PATCH.read_text(encoding="utf-8")

    require_text(
        compat,
        '"DeliveryTemperatureLimit.FetchManager_FetchablesByPrefabId_Patch"',
        "CycleTrim Delivery Temperature Limit patch marker changed",
        failures,
    )
    require_text(
        fetch_patch,
        '"PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate"',
        "CycleTrim Fetch FastTrack type marker changed",
        failures,
    )
    require_regex(
        fetch_patch,
        r"return\s+AccessTools\.TypeByName\(FastTrackPatchType\)\s*==\s*null\s*;",
        "CycleTrim Fetch no longer keeps its conservative FastTrack type guard",
        failures,
    )
    require_regex(
        fetch_patch,
        r'typeof\(FetchManager\.FetchablesByPrefabId\).*?"UpdatePickups".*?'
        r'new\[\]\s*\{\s*typeof\(Navigator\),\s*typeof\(int\)\s*\}',
        "CycleTrim Fetch target signature changed",
        failures,
    )
    require_text(
        fetch_patch,
        "Harmony.GetPatchInfo(targetMethod)",
        "CycleTrim Fetch no longer inspects the installed Harmony topology",
        failures,
    )
    require_text(
        fetch_patch,
        "patchInfo.Transpilers",
        "CycleTrim Fetch compatibility check no longer limits itself to transpilers",
        failures,
    )
    require_regex(
        fetch_patch,
        r"if\s*\(ShouldRunOriginalForCompatibility\(\)\)\s*\{\s*return\s+true\s*;\s*\}",
        "CycleTrim Fetch no longer fails open to the original/transpiled UpdatePickups path",
        failures,
    )
    require_regex(
        fetch_patch,
        r"if\s*\(!compatibilityChecked\).*?runOriginalForCompatibility\s*=\s*"
        r"HasDeliveryTemperatureLimitTranspiler\(\)\s*;.*?compatibilityChecked\s*=\s*true\s*;",
        "CycleTrim Fetch compatibility decision is no longer cached after first real execution",
        failures,
    )
    require_text(
        fetch_patch,
        "FetchPatchCompatibility.IsDeliveryTemperatureLimitTranspiler",
        "CycleTrim Fetch no longer classifies the pinned Delivery Temperature Limit transpiler",
        failures,
    )


def verify_upstream(manifest: dict, failures: list[str]) -> None:
    repository = manifest["repository"]
    commit = manifest["commit"]
    sources: dict[str, str] = {}

    for item in manifest["files"]:
        path = item["path"]
        expected_blob = item["gitBlobSha1"]
        try:
            payload = fetch_pinned_source(repository, commit, path)
        except (OSError, urllib.error.URLError) as error:
            fail(f"failed to download pinned Delivery Temperature Limit source {path}: {error}", failures)
            continue
        actual_blob = git_blob_sha1(payload)
        if actual_blob != expected_blob:
            fail(
                f"{path} Git blob mismatch: expected {expected_blob}, got {actual_blob}",
                failures,
            )
            continue
        sources[path] = payload.decode("utf-8-sig")

    required_paths = {"Source/Patch.cs", "Source/PatchFastTrack.cs", "Source/Mod.cs"}
    missing = required_paths - sources.keys()
    if missing:
        fail("missing verified upstream source: " + ", ".join(sorted(missing)), failures)
        return

    patch = sources["Source/Patch.cs"]
    fasttrack = sources["Source/PatchFastTrack.cs"]
    mod = sources["Source/Mod.cs"]

    require_regex(
        patch,
        r"namespace\s+DeliveryTemperatureLimit.*?"
        r"\[HarmonyPatch\(typeof\(FetchManager\.FetchablesByPrefabId\)\)\].*?"
        r"class\s+FetchManager_FetchablesByPrefabId_Patch.*?"
        r"\[HarmonyTranspiler\].*?\[HarmonyPatch\(nameof\(UpdatePickups\)\)\].*?"
        r"IEnumerable<CodeInstruction>\s+UpdatePickups\s*\(",
        "pinned Delivery Temperature Limit UpdatePickups transpiler target changed",
        failures,
    )
    require_text(
        patch,
        "keep one for each tag+priority+temperatureindex",
        "pinned Delivery Temperature Limit temperature-partition rationale changed",
        failures,
    )
    require_text(
        patch,
        "UpdatePickups_Hook",
        "pinned Delivery Temperature Limit UpdatePickups temperature hook changed",
        failures,
    )
    require_regex(
        fasttrack,
        r"class\s+FetchManagerFastUpdate_PickupTagDict_Patch.*?"
        r"FetchManagerFastUpdate\+PickupTagDict, FastTrack.*?"
        r'"AddItem".*?AddItem_Hook',
        "pinned Delivery Temperature Limit FastTrack fetch compatibility path changed",
        failures,
    )
    require_text(
        fasttrack,
        "TemperatureIndex( pickupable.PrimaryElement.Temperature )",
        "pinned Delivery Temperature Limit FastTrack temperature partition changed",
        failures,
    )
    require_regex(
        mod,
        r"override\s+void\s+OnLoad\s*\(\s*Harmony\s+harmony\s*\).*?base\.OnLoad\(\s*harmony\s*\)",
        "pinned Delivery Temperature Limit load path changed",
        failures,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--verify-upstream",
        action="store_true",
        help="download the immutable pinned source and verify Git blob hashes and patch shape",
    )
    args = parser.parse_args()

    failures: list[str] = []
    manifest = load_manifest()
    verify_local(manifest, failures)
    if args.verify_upstream:
        verify_upstream(manifest, failures)

    if failures:
        for message in failures:
            print("FAIL " + message, file=sys.stderr)
        return 1

    scope = "local + pinned upstream" if args.verify_upstream else "local"
    print("PASS CycleTrim Delivery Temperature Limit fetch compatibility contract (" + scope + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
