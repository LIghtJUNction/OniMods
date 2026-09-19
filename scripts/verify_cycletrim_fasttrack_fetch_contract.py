#!/usr/bin/env python3
"""Verify CycleTrim's pinned FastTrack fetch/pickup compatibility assumptions."""

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
MANIFEST = ROOT / "scripts/upstream/cycletrim-fasttrack-fetch.json"
CYCLETRIM_FETCH_PATCH = ROOT / "mods/CycleTrim/Patches/FetchPickupCandidatePatch.cs"
CYCLETRIM_BUSY_PATCH = ROOT / "mods/CycleTrim/Patches/BusyDuplicantChoreThrottlePatch.cs"
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
        headers={"User-Agent": "OniMods-CycleTrim-FastTrack-contract/1"},
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def verify_local(manifest: dict, failures: list[str]) -> None:
    if manifest.get("schemaVersion") != 1:
        fail("manifest schemaVersion must be 1", failures)

    repository = manifest.get("repository")
    commit = manifest.get("commit")
    if repository != "peterhaneve/ONIMods":
        fail("manifest repository must stay pinned to peterhaneve/ONIMods", failures)
    if not isinstance(commit, str) or re.fullmatch(r"[0-9a-f]{40}", commit) is None:
        fail("manifest commit must be a full 40-character Git SHA", failures)

    files = manifest.get("files")
    if not isinstance(files, list) or not files:
        fail("manifest files must be a non-empty list", failures)
    else:
        seen_paths = set()
        for item in files:
            if not isinstance(item, dict):
                fail("manifest file entries must be objects", failures)
                continue
            path = item.get("path")
            blob = item.get("gitBlobSha1")
            if not isinstance(path, str) or not path.startswith("FastTrack/"):
                fail(f"invalid FastTrack path in manifest: {path!r}", failures)
            if path in seen_paths:
                fail(f"duplicate manifest path: {path}", failures)
            seen_paths.add(path)
            if not isinstance(blob, str) or re.fullmatch(r"[0-9a-f]{40}", blob) is None:
                fail(f"invalid Git blob SHA for {path!r}", failures)

    fetch_patch = CYCLETRIM_FETCH_PATCH.read_text(encoding="utf-8")
    require_text(
        fetch_patch,
        '"PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate"',
        "CycleTrim Fetch FastTrack type marker changed",
        failures,
    )
    require_regex(
        fetch_patch,
        r"return\s+AccessTools\.TypeByName\(FastTrackPatchType\)\s*==\s*null\s*;",
        "CycleTrim Fetch no longer uses the conservative type-presence guard",
        failures,
    )
    require_regex(
        fetch_patch,
        r'typeof\(FetchManager\.FetchablesByPrefabId\).*?"UpdatePickups".*?'
        r'new\[\]\s*\{\s*typeof\(Navigator\),\s*typeof\(int\)\s*\}',
        "CycleTrim Fetch target signature changed",
        failures,
    )

    busy_patch = CYCLETRIM_BUSY_PATCH.read_text(encoding="utf-8")
    require_text(
        busy_patch,
        '"PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate"',
        "CycleTrim busy-duplicant FastTrack type marker changed",
        failures,
    )
    require_regex(
        busy_patch,
        r"return\s+AccessTools\.TypeByName\(FastTrackPatchType\)\s*==\s*null\s*;",
        "CycleTrim busy-duplicant patch no longer uses the conservative type-presence guard",
        failures,
    )
    require_regex(
        busy_patch,
        r"AccessTools\.Method\(\s*typeof\(PickupableSensor\),\s*\"Update\",\s*Type\.EmptyTypes\s*\)",
        "CycleTrim busy-duplicant pickup target is no longer PickupableSensor.Update()",
        failures,
    )
    require_regex(
        busy_patch,
        r"AccessTools\.Method\(\s*typeof\(ChoreConsumer\),\s*\"FindNextChore\",\s*"
        r"new\[\]\s*\{\s*typeof\(Chore\.Precondition\.Context\)\.MakeByRefType\(\)\s*\}\s*\)",
        "CycleTrim busy-duplicant chore target is no longer ChoreConsumer.FindNextChore(ref Context)",
        failures,
    )
    require_regex(
        busy_patch,
        r"AccessTools\.Method\(\s*typeof\(BrainScheduler\),\s*\"PrioritizeBrain\",\s*"
        r"new\[\]\s*\{\s*typeof\(Brain\)\s*\}\s*\)",
        "CycleTrim busy-duplicant priority target is no longer BrainScheduler.PrioritizeBrain(Brain)",
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
            fail(f"failed to download pinned FastTrack source {path}: {error}", failures)
            continue
        actual_blob = git_blob_sha1(payload)
        if actual_blob != expected_blob:
            fail(
                f"{path} Git blob mismatch: expected {expected_blob}, got {actual_blob}",
                failures,
            )
            continue
        sources[path] = payload.decode("utf-8-sig")

    required_paths = {
        "FastTrack/FastTrackMod.cs",
        "FastTrack/FastTrackOptions.cs",
        "FastTrack/FastTrackCompat.cs",
        "FastTrack/GamePatches/FetchManagerFastUpdate.cs",
        "FastTrack/SensorPatches/SensorPatches.cs",
        "FastTrack/PathPatches/AsyncPathPatches.cs",
        "FastTrack/PathPatches/PriorityBrainScheduler.cs",
    }
    missing = required_paths - sources.keys()
    if missing:
        fail("missing verified upstream source: " + ", ".join(sorted(missing)), failures)
        return

    mod = sources["FastTrack/FastTrackMod.cs"]
    options = sources["FastTrack/FastTrackOptions.cs"]
    compat = sources["FastTrack/FastTrackCompat.cs"]
    fetch_patch = sources["FastTrack/GamePatches/FetchManagerFastUpdate.cs"]
    sensors = sources["FastTrack/SensorPatches/SensorPatches.cs"]
    async_path = sources["FastTrack/PathPatches/AsyncPathPatches.cs"]
    priority_scheduler = sources["FastTrack/PathPatches/PriorityBrainScheduler.cs"]

    require_regex(
        mod,
        r"if\s*\(\s*options\.FastUpdatePickups\s*\)\s*"
        r"FastTrackCompat\.CheckFetchCompat\(harmony\)\s*;",
        "FastTrack no longer gates CheckFetchCompat on FastUpdatePickups",
        failures,
    )
    require_regex(
        options,
        r"public\s+bool\s+FastUpdatePickups\s*\{\s*get;\s*set;\s*\}",
        "FastTrack FastUpdatePickups option declaration changed",
        failures,
    )
    require_regex(
        options,
        r"\bFastUpdatePickups\s*=\s*true\s*;",
        "FastTrack FastUpdatePickups default is no longer true",
        failures,
    )
    require_text(
        compat,
        'private const string EFFICIENT_SUPPLY_TYPE = '
        '"PeterHan.EfficientFetch.EfficientFetchManager";',
        "FastTrack Efficient Supply compatibility marker changed",
        failures,
    )
    require_regex(
        compat,
        r"if\s*\(\s*PPatchTools\.GetTypeSafe\(EFFICIENT_SUPPLY_TYPE\)\s*==\s*null\s*\)",
        "FastTrack fetch compatibility gate no longer excludes Efficient Supply",
        failures,
    )
    require_regex(
        compat,
        r"harmony\.Patch\(typeof\(FetchManager\.FetchablesByPrefabId\),\s*"
        r"nameof\(FetchManager\.FetchablesByPrefabId\.UpdatePickups\),\s*"
        r"prefix:\s*new HarmonyMethod\(typeof\(GamePatches\.FetchManagerFastUpdate\),\s*"
        r"nameof\(GamePatches\.FetchManagerFastUpdate\.BeforeUpdatePickups\)\)\)",
        "FastTrack UpdatePickups Harmony target/prefix changed",
        failures,
    )
    require_regex(
        fetch_patch,
        r"internal\s+static\s+bool\s+BeforeUpdatePickups\s*\(\s*"
        r"FetchManager\.FetchablesByPrefabId\s+__instance,\s*"
        r"Navigator\s+worker_navigator,\s*int\s+worker\s*\)",
        "FastTrack BeforeUpdatePickups signature changed",
        failures,
    )

    require_regex(
        options,
        r"public\s+bool\s+PickupOpts\s*\{\s*get;\s*set;\s*\}",
        "FastTrack PickupOpts option declaration changed",
        failures,
    )
    require_regex(
        options,
        r"\bPickupOpts\s*=\s*true\s*;",
        "FastTrack PickupOpts default is no longer true",
        failures,
    )
    require_regex(
        sensors,
        r"\[HarmonyPatch\(typeof\(PickupableSensor\),\s*nameof\(PickupableSensor\.Update\)\)\]\s*"
        r"public\s+static\s+class\s+PickupableSensor_Update_Patch",
        "FastTrack PickupableSensor.Update target changed",
        failures,
    )
    require_regex(
        sensors,
        r"internal\s+static\s+bool\s+Prepare\(\)\s*=>\s*FastTrackOptions\.Instance\.PickupOpts\s*;",
        "FastTrack PickupableSensor replacement is no longer gated by PickupOpts",
        failures,
    )
    require_regex(
        sensors,
        r"\[HarmonyPriority\(Priority\.Low\)\]\s*internal\s+static\s+bool\s+Prefix\(\)\s*"
        r"\{\s*return\s+false\s*;\s*\}",
        "FastTrack PickupableSensor prefix no longer replaces the original method at low priority",
        failures,
    )

    require_regex(
        options,
        r"public\s+NextChorePriority\s+ChorePriorityMode\s*\{\s*get;\s*set;\s*\}",
        "FastTrack ChorePriorityMode option declaration changed",
        failures,
    )
    require_regex(
        options,
        r"\bChorePriorityMode\s*=\s*NextChorePriority\.Higher\s*;",
        "FastTrack ChorePriorityMode default is no longer Higher",
        failures,
    )
    require_regex(
        async_path,
        r"\[HarmonyPatch\(typeof\(BrainScheduler\),\s*nameof\(BrainScheduler\.RenderEveryTick\)\)\]\s*"
        r"internal\s+static\s+class\s+BrainScheduler_RenderEveryTick_Patch",
        "FastTrack BrainScheduler.RenderEveryTick target changed",
        failures,
    )
    require_regex(
        async_path,
        r"class\s+BrainScheduler_RenderEveryTick_Patch.*?"
        r"internal\s+static\s+bool\s+Prepare\(\)\s*=>\s*FastTrackOptions\.Instance\.PickupOpts\s*;",
        "FastTrack BrainScheduler replacement is no longer gated by PickupOpts",
        failures,
    )
    require_regex(
        async_path,
        r"class\s+BrainScheduler_RenderEveryTick_Patch.*?"
        r"\[HarmonyPriority\(Priority\.Low\)\].*?"
        r"PriorityBrainScheduler\.Instance\.UpdateBrainGroup\(inst,\s*brainGroup\)\s*;.*?"
        r"return\s+false\s*;",
        "FastTrack BrainScheduler replacement no longer routes groups through PriorityBrainScheduler and skips vanilla RenderEveryTick",
        failures,
    )
    require_regex(
        async_path,
        r"\[HarmonyPatch\(typeof\(Brain\),\s*nameof\(Brain\.UpdateChores\)\)\]\s*"
        r"public\s+static\s+class\s+Brain_UpdateChores_Patch.*?"
        r"return\s+opts\.PickupOpts\s*&&\s*opts\.FastReachability\s*&&\s*"
        r"opts\.ChorePriorityMode\s*==\s*FastTrackOptions\.NextChorePriority\.Delay\s*;",
        "FastTrack delayed chore-acquisition gate changed",
        failures,
    )
    require_regex(
        async_path,
        r"class\s+Brain_UpdateChores_Patch.*?"
        r"consumer\.choreDriver\.HasChore\(\).*?"
        r"!inst\.updateFirst\.\s*Contains\(__instance\)",
        "FastTrack Brain.UpdateChores prefix no longer gates chore acquisition through updateFirst",
        failures,
    )
    require_regex(
        priority_scheduler,
        r"var\s+pm\s*=\s*opts\.ChorePriorityMode\s*;.*?"
        r"if\s*\(\s*pm\s*==\s*FastTrackOptions\.NextChorePriority\.Delay\s*&&\s*!opts\.FastReachability\s*\)\s*"
        r"pm\s*=\s*FastTrackOptions\.NextChorePriority\.Normal\s*;",
        "FastTrack PriorityBrainScheduler mode/fallback contract changed",
        failures,
    )
    require_regex(
        priority_scheduler,
        r"private\s+void\s+PopulatePriorityBrains\(.*?"
        r"var\s+prioritize\s*=\s*brainGroup\.priorityBrains\s*;.*?"
        r"case\s+FastTrackOptions\.NextChorePriority\.Higher\s*:.*?"
        r"prioritize\.Dequeue\(\).*?inst\.QueueBrain\(brain\)\s*;",
        "FastTrack priority-brain queue handling changed",
        failures,
    )
    require_regex(
        priority_scheduler,
        r"internal\s+void\s+UpdateBrainGroup\(.*?"
        r"brainGroup\.BeginBrainGroupUpdate\(\)\s*;.*?"
        r"PopulatePriorityBrains\(inst,\s*brainGroup\)\s*;.*?"
        r"brainGroup\.EndBrainGroupUpdate\(\)\s*;",
        "FastTrack UpdateBrainGroup no longer owns the brain-group scheduling cycle",
        failures,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--verify-upstream",
        action="store_true",
        help="download immutable pinned FastTrack sources and verify fetch/pickup/scheduler activation assumptions",
    )
    args = parser.parse_args()

    failures: list[str] = []
    try:
        manifest = load_manifest()
    except (OSError, json.JSONDecodeError) as error:
        print(f"FAIL: cannot load {MANIFEST.relative_to(ROOT)}: {error}", file=sys.stderr)
        return 1

    verify_local(manifest, failures)
    if args.verify_upstream and not failures:
        verify_upstream(manifest, failures)

    if failures:
        for message in failures:
            print(f"FAIL: {message}", file=sys.stderr)
        return 1

    mode = "local + pinned upstream" if args.verify_upstream else "local"
    print(
        "OK: CycleTrim FastTrack fetch/pickup/scheduler compatibility contract "
        f"({mode}, {manifest['repository']}@{manifest['commit']})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
