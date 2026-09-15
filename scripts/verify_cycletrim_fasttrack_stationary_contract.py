#!/usr/bin/env python3
"""Verify CycleTrim's pinned FastTrack stationary-critter compatibility assumptions."""

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
MANIFEST = ROOT / "scripts/upstream/cycletrim-fasttrack-stationary-critter.json"
CYCLETRIM_PATCH = ROOT / "mods/CycleTrim/Patches/StationaryCritterNavigationThrottlePatch.cs"
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
        headers={"User-Agent": "OniMods-CycleTrim-FastTrack-stationary-contract/1"},
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

    patch = CYCLETRIM_PATCH.read_text(encoding="utf-8")
    require_text(
        patch,
        '"PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate"',
        "CycleTrim FastTrack type marker changed",
        failures,
    )
    require_regex(
        patch,
        r"return\s+AccessTools\.TypeByName\(FastTrackPatchType\)\s*==\s*null\s*;",
        "CycleTrim no longer uses the conservative FastTrack type-presence guard",
        failures,
    )
    require_regex(
        patch,
        r"AccessTools\.Method\(\s*typeof\(Navigator\),\s*\"UpdateProbe\",\s*"
        r"new\[\]\s*\{\s*typeof\(bool\)\s*\}\s*\)",
        "CycleTrim stationary target is no longer Navigator.UpdateProbe(bool)",
        failures,
    )
    for token, message in (
        ("forceUpdate", "CycleTrim force-update bypass drifted"),
        ("__instance.IsMoving()", "CycleTrim moving-creature bypass drifted"),
        ("___reportOccupation", "CycleTrim occupation-report bypass drifted"),
        ("___executePathProbeTaskAsync", "CycleTrim async-probe bypass drifted"),
        ("typeof(CreaturePathFinderAbilities)", "CycleTrim exact creature-abilities guard drifted"),
    ):
        require_text(patch, token, message, failures)


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
        "FastTrack/PathPatches/IdleMove.cs",
        "FastTrack/PathPatches/AsyncPathPatches.cs",
        "FastTrack/PathPatches/AsyncBrainGroupUpdater.cs",
    }
    missing = required_paths - sources.keys()
    if missing:
        fail("missing verified upstream source: " + ", ".join(sorted(missing)), failures)
        return

    mod = sources["FastTrack/FastTrackMod.cs"]
    options = sources["FastTrack/FastTrackOptions.cs"]
    idle_move = sources["FastTrack/PathPatches/IdleMove.cs"]
    async_patches = sources["FastTrack/PathPatches/AsyncPathPatches.cs"]
    async_updater = sources["FastTrack/PathPatches/AsyncBrainGroupUpdater.cs"]

    require_regex(
        mod,
        r"if\s*\(\s*options\.ReduceCritterIdleMove\s*\)\s*"
        r"PathPatches\.ReduceCritterIdleMoves\.ApplyPatch\(harmony\)\s*;",
        "FastTrack no longer gates critter idle patches on ReduceCritterIdleMove",
        failures,
    )
    require_regex(
        options,
        r"public\s+bool\s+ReduceCritterIdleMove\s*\{\s*get;\s*set;\s*\}",
        "FastTrack ReduceCritterIdleMove option declaration changed",
        failures,
    )
    require_regex(
        options,
        r"\bReduceCritterIdleMove\s*=\s*false\s*;",
        "FastTrack ReduceCritterIdleMove default is no longer false",
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
        idle_move,
        r"harmony\.Patch\(typeof\(IdleStates\),\s*nameof\(IdleStates\.MoveToNewCell\),\s*prefix:\s*"
        r"new HarmonyMethod\(typeof\(ReduceCritterIdleMoves\),\s*"
        r"nameof\(MoveToNewCellGround_Prefix\)\)\s*\)",
        "FastTrack ground critter idle target/prefix changed",
        failures,
    )
    require_regex(
        idle_move,
        r"harmony\.Patch\(typeof\(BuzzStates\),\s*nameof\(BuzzStates\.MoveToNewCell\),\s*prefix:\s*"
        r"new HarmonyMethod\(typeof\(ReduceCritterIdleMoves\),\s*"
        r"nameof\(MoveToNewCellAir_Prefix\)\)\s*\)",
        "FastTrack flying critter idle target/prefix changed",
        failures,
    )
    require_regex(
        idle_move,
        r"\[HarmonyPriority\(Priority\.Low\)\]\s*"
        r"internal\s+static\s+bool\s+MoveToNewCellGround_Prefix",
        "FastTrack ground critter prefix priority changed",
        failures,
    )
    require_regex(
        idle_move,
        r"\[HarmonyPriority\(Priority\.Low\)\]\s*"
        r"internal\s+static\s+bool\s+MoveToNewCellAir_Prefix",
        "FastTrack flying critter prefix priority changed",
        failures,
    )
    if "Navigator.UpdateProbe" in idle_move:
        fail(
            "FastTrack critter-idle source now mentions Navigator.UpdateProbe; "
            "re-evaluate direct target overlap",
            failures,
        )

    require_regex(
        async_patches,
        r"\[HarmonyPatch\(typeof\(BrainScheduler\),\s*"
        r"nameof\(BrainScheduler\.RenderEveryTick\)\)\]",
        "FastTrack BrainScheduler.RenderEveryTick target changed",
        failures,
    )
    require_regex(
        async_patches,
        r"internal\s+static\s+bool\s+Prepare\(\)\s*=>\s*"
        r"FastTrackOptions\.Instance\.PickupOpts\s*;",
        "FastTrack BrainScheduler replacement is no longer gated by PickupOpts",
        failures,
    )
    require_regex(
        async_patches,
        r"\[HarmonyPriority\(Priority\.Low\)\]\s*"
        r"internal\s+static\s+bool\s+Prefix\(BrainScheduler\s+__instance\)",
        "FastTrack BrainScheduler prefix priority/signature changed",
        failures,
    )
    require_regex(
        async_patches,
        r"PriorityBrainScheduler\.Instance\.UpdateBrainGroup\(inst,\s*brainGroup\)",
        "FastTrack BrainScheduler no longer routes groups through PriorityBrainScheduler",
        failures,
    )
    require_regex(
        async_updater,
        r"if\s*\(\s*brain\.prefabId\.HasTag\(GameTags\.DupeBrain\)\s*\)\s*"
        r"AddBrain\(brain\)\s*;\s*else\s*brain\.UpdateBrain\(\)\s*;",
        "FastTrack non-Dupe brain scheduling path changed",
        failures,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--verify-upstream",
        action="store_true",
        help="download immutable pinned FastTrack sources and verify stationary-critter compatibility assumptions",
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
        "OK: CycleTrim FastTrack stationary-critter compatibility contract "
        f"({mode}, {manifest['repository']}@{manifest['commit']})"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
