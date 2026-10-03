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
CYCLETRIM_MOD_INFO = ROOT / "mods/CycleTrim/ModInfo.cs"
RAW_ROOT = "https://raw.githubusercontent.com"

LEGACY_REPOSITORY = "llunak/oni-deliverytemperaturelimit"
LEGACY_PATHS = {
    "Source/Patch.cs",
    "Source/PatchFastTrack.cs",
    "Source/Mod.cs",
}
SUPERCOOLED_REPOSITORY = "MaksymShostak/oxygen-not-included"
SUPERCOOLED_PATHS = {
    "mods/delivery-temperature-limit-supercooled/Source/KleiImplementationAdapters/KleiPickupTemperatureGroupingPatches.cs",
    "mods/delivery-temperature-limit-supercooled/Source/RuntimePatchInstallation/DeliveryTemperatureRuntimePatchInstaller.cs",
    "mods/delivery-temperature-limit-supercooled/Source/HarmonyTranspilerInfrastructure/HarmonyPatchContractVerifier.cs",
    "mods/delivery-temperature-limit-supercooled/Source/DeliveryTemperatureLimitMod.cs",
}


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
        headers={"User-Agent": "OniMods-CycleTrim-DTL-contract/2"},
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def validate_pin(
    pin: dict,
    expected_repository: str,
    expected_paths: set[str],
    label: str,
    failures: list[str],
) -> None:
    if pin.get("repository") != expected_repository:
        fail(f"{label} repository must stay pinned to {expected_repository}", failures)
    commit = pin.get("commit")
    if not isinstance(commit, str) or re.fullmatch(r"[0-9a-f]{40}", commit) is None:
        fail(f"{label} commit must be a full 40-character Git SHA", failures)

    files = pin.get("files")
    if not isinstance(files, list) or not files:
        fail(f"{label} files must be a non-empty list", failures)
        return

    seen_paths: set[str] = set()
    for item in files:
        if not isinstance(item, dict):
            fail(f"{label} file entries must be objects", failures)
            continue
        path = item.get("path")
        blob = item.get("gitBlobSha1")
        if path in seen_paths:
            fail(f"duplicate {label} manifest path: {path}", failures)
        if isinstance(path, str):
            seen_paths.add(path)
        if not isinstance(blob, str) or re.fullmatch(r"[0-9a-f]{40}", blob) is None:
            fail(f"invalid {label} Git blob SHA for {path!r}", failures)

    if seen_paths != expected_paths:
        fail(
            f"{label} manifest paths changed: expected "
            + ", ".join(sorted(expected_paths)),
            failures,
        )


def verify_local(manifest: dict, failures: list[str]) -> None:
    if manifest.get("schemaVersion") != 1:
        fail("manifest schemaVersion must be 1", failures)

    validate_pin(manifest, LEGACY_REPOSITORY, LEGACY_PATHS, "legacy DTL", failures)
    supercooled = manifest.get("maintainedSupercooled")
    if not isinstance(supercooled, dict):
        fail("maintainedSupercooled pin is missing", failures)
    else:
        validate_pin(
            supercooled,
            SUPERCOOLED_REPOSITORY,
            SUPERCOOLED_PATHS,
            "maintained DTL",
            failures,
        )

    compat = CYCLETRIM_COMPAT.read_text(encoding="utf-8")
    fetch_patch = CYCLETRIM_FETCH_PATCH.read_text(encoding="utf-8")
    mod_info = CYCLETRIM_MOD_INFO.read_text(encoding="utf-8")

    require_text(
        compat,
        '"DeliveryTemperatureLimit.FetchManager_FetchablesByPrefabId_Patch"',
        "CycleTrim legacy Delivery Temperature Limit patch marker changed",
        failures,
    )
    require_text(
        compat,
        '"DeliveryTemperatureLimit.KleiPickupTemperatureGroupingPatches"',
        "CycleTrim maintained Delivery Temperature Limit marker changed",
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
        r"private\s+static\s+bool\s+Prepare\(\)\s*\{\s*return\s+installRequested\s*;\s*\}",
        "CycleTrim Fetch must stay withheld from initial PatchAll",
        failures,
    )
    require_text(
        fetch_patch,
        "InstallAfterAllModsLoaded(Harmony harmony)",
        "CycleTrim Fetch no longer defers installation until loaded-mod discovery",
        failures,
    )
    require_text(
        fetch_patch,
        "AccessTools.TypeByName(FastTrackPatchType)",
        "CycleTrim Fetch no longer keeps its conservative FastTrack type guard",
        failures,
    )
    require_text(
        fetch_patch,
        "DeliveryTemperatureLimitSupercooledPickupGroupingType",
        "CycleTrim Fetch no longer yields to maintained Delivery Temperature Limit before patching",
        failures,
    )
    require_text(
        fetch_patch,
        "Harmony.GetPatchInfo(target)",
        "CycleTrim Fetch no longer inspects legacy Delivery Temperature Limit topology before patching",
        failures,
    )
    require_text(
        fetch_patch,
        "patchInfo.Transpilers",
        "CycleTrim Fetch legacy compatibility check no longer limits itself to transpilers",
        failures,
    )
    require_text(
        fetch_patch,
        "FetchPatchCompatibility.IsDeliveryTemperatureLimitTranspiler",
        "CycleTrim Fetch no longer classifies the pinned legacy Delivery Temperature Limit transpiler",
        failures,
    )
    require_text(
        fetch_patch,
        "harmony.CreateClassProcessor(typeof(UpdatePickupsPatch)).Patch()",
        "CycleTrim Fetch no longer installs the withheld class explicitly after compatibility checks",
        failures,
    )
    require_regex(
        fetch_patch,
        r'typeof\(FetchManager\.FetchablesByPrefabId\).*?"UpdatePickups".*?'
        r'new\[\]\s*\{\s*typeof\(Navigator\),\s*typeof\(int\)\s*\}',
        "CycleTrim Fetch target signature changed",
        failures,
    )
    require_regex(
        mod_info,
        r"override\s+void\s+OnAllModsLoaded\s*\(.*?"
        r"FetchPickupCandidatePatch\.InstallAfterAllModsLoaded\(harmony\)",
        "CycleTrim ModInfo no longer installs the fetch patch from OnAllModsLoaded",
        failures,
    )


def fetch_verified_sources(
    pin: dict,
    label: str,
    failures: list[str],
) -> dict[str, str]:
    repository = pin["repository"]
    commit = pin["commit"]
    sources: dict[str, str] = {}
    for item in pin["files"]:
        path = item["path"]
        expected_blob = item["gitBlobSha1"]
        try:
            payload = fetch_pinned_source(repository, commit, path)
        except (OSError, urllib.error.URLError) as error:
            fail(f"failed to download pinned {label} source {path}: {error}", failures)
            continue
        actual_blob = git_blob_sha1(payload)
        if actual_blob != expected_blob:
            fail(
                f"{label} {path} Git blob mismatch: expected {expected_blob}, got {actual_blob}",
                failures,
            )
            continue
        sources[path] = payload.decode("utf-8-sig")
    return sources


def verify_legacy_upstream(manifest: dict, failures: list[str]) -> None:
    sources = fetch_verified_sources(manifest, "legacy Delivery Temperature Limit", failures)
    missing = LEGACY_PATHS - sources.keys()
    if missing:
        fail("missing verified legacy DTL source: " + ", ".join(sorted(missing)), failures)
        return

    patch = sources["Source/Patch.cs"]
    fasttrack = sources["Source/PatchFastTrack.cs"]
    mod = sources["Source/Mod.cs"]

    require_regex(
        patch,
        r"namespace\s+DeliveryTemperatureLimit.*?"
        r"class\s+FetchManager_FetchablesByPrefabId_Patch.*?"
        r"IEnumerable<CodeInstruction>\s+UpdatePickups\s*\(",
        "pinned legacy DTL UpdatePickups transpiler target changed",
        failures,
    )
    require_text(
        patch,
        "keep one for each tag+priority+temperatureindex",
        "pinned legacy DTL temperature-partition rationale changed",
        failures,
    )
    require_text(
        patch,
        "UpdatePickups_Hook",
        "pinned legacy DTL UpdatePickups temperature hook changed",
        failures,
    )
    require_regex(
        fasttrack,
        r"class\s+FetchManagerFastUpdate_PickupTagDict_Patch.*?"
        r"FetchManagerFastUpdate\+PickupTagDict, FastTrack.*?"
        r'"AddItem".*?AddItem_Hook',
        "pinned legacy DTL FastTrack fetch compatibility path changed",
        failures,
    )
    require_text(
        mod,
        "override void OnLoad( Harmony harmony )",
        "pinned legacy DTL load path changed",
        failures,
    )


def verify_supercooled_upstream(supercooled: dict, failures: list[str]) -> None:
    sources = fetch_verified_sources(supercooled, "maintained Delivery Temperature Limit", failures)
    missing = SUPERCOOLED_PATHS - sources.keys()
    if missing:
        fail("missing verified maintained DTL source: " + ", ".join(sorted(missing)), failures)
        return

    prefix = "mods/delivery-temperature-limit-supercooled/Source/"
    grouping = sources[prefix + "KleiImplementationAdapters/KleiPickupTemperatureGroupingPatches.cs"]
    installer = sources[prefix + "RuntimePatchInstallation/DeliveryTemperatureRuntimePatchInstaller.cs"]
    verifier = sources[prefix + "HarmonyTranspilerInfrastructure/HarmonyPatchContractVerifier.cs"]
    mod = sources[prefix + "DeliveryTemperatureLimitMod.cs"]

    require_text(
        grouping,
        "internal static class KleiPickupTemperatureGroupingPatches",
        "maintained DTL pickup grouping marker type changed",
        failures,
    )
    require_regex(
        grouping,
        r"typeof\(FetchManager\.FetchablesByPrefabId\).*?"
        r'"UpdatePickups".*?typeof\(Navigator\).*?typeof\(int\)',
        "maintained DTL UpdatePickups target changed",
        failures,
    )
    require_text(
        grouping,
        "UpdatePickupsTranspiler",
        "maintained DTL UpdatePickups transpiler changed",
        failures,
    )
    require_text(
        grouping,
        "HaveSameTemperatureEligibilityClass",
        "maintained DTL temperature grouping semantics changed",
        failures,
    )
    require_text(
        installer,
        "RuntimeCapabilityId.PickupTemperatureGrouping",
        "maintained DTL pickup grouping is no longer a selected runtime capability",
        failures,
    )
    require_text(
        installer,
        "PrepareKleiPickupTemperatureGroupingPatches",
        "maintained DTL pickup grouping installation path changed",
        failures,
    )
    require_text(
        installer,
        "CreateKleiOriginalAuthorityRequirements",
        "maintained DTL Klei authority requirement path changed",
        failures,
    )
    require_regex(
        verifier,
        r"PrefixMethod\.ReturnType\s*!=\s*typeof\(bool\).*?"
        r"ContainsExactOwner\(.*?permittedSkippingPrefixOwners",
        "maintained DTL no longer rejects unpermitted skipping bool Prefix owners",
        failures,
    )
    require_regex(
        mod,
        r"override\s+void\s+OnAllModsLoaded\s*\(.*?"
        r"InstallLoadedModTopologyDependentPatches\(harmony, loadedMods\)",
        "maintained DTL topology-dependent installation phase changed",
        failures,
    )


def verify_upstream(manifest: dict, failures: list[str]) -> None:
    verify_legacy_upstream(manifest, failures)
    supercooled = manifest.get("maintainedSupercooled")
    if isinstance(supercooled, dict):
        verify_supercooled_upstream(supercooled, failures)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--verify-upstream",
        action="store_true",
        help="download immutable pinned sources and verify blob hashes and compatibility shape",
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
