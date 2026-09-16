#!/usr/bin/env python3
"""Verify fuzzy build-plan resolution cannot silently cross into confirmed writes."""

from __future__ import annotations

import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ACTION = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanningActionBuildArea.cs"
POLICY = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanExecutionSafety.cs"


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def require(source: str, pattern: str, message: str) -> None:
    if not re.search(pattern, source, re.DOTALL):
        fail(message)


def main() -> int:
    action = ACTION.read_text(encoding="utf-8")
    policy = POLICY.read_text(encoding="utf-8")

    require(policy, r"MinimumAutoWriteBuildingScore\s*=\s*700\s*;",
            "write-safety score floor drifted from the parser's strong-match floor")
    require(policy, r"MinimumAutoWriteBuildingGap\s*=\s*80\s*;",
            "write-safety near-tie gap drifted from one scoring tier")
    require(policy, r'matchKind,\s*"alias".*?matchKind,\s*"prefabId"',
            "exact alias/prefabId bypass is missing")
    require(policy, r'reason\s*=\s*"low_confidence"',
            "low-confidence fuzzy rejection is missing")
    require(policy, r'reason\s*=\s*"near_tie"',
            "near-tied fuzzy rejection is missing")

    guard = action.find("!dryRun && !IsAutoWriteBuildingResolutionSafe")
    promotion = action.find('args["prefabId"] = planResolution.PrefabId;')
    if guard < 0:
        fail("build_area no longer guards confirmed fuzzy promotion")
    if promotion < 0:
        fail("build_area no longer promotes resolved prefabId")
    if guard > promotion:
        fail("build_area checks fuzzy write safety after prefab promotion")

    require(action, r'topCandidate\.MatchKind,\s*"sequence".*?sequenceBuilding\.BuildingCandidates',
            "sequence resolution no longer unwraps the underlying candidate confidence")
    require(action, r'\["reasonCode"\]\s*=\s*"ambiguous_plan_building"',
            "ambiguous write rejection lacks a stable reasonCode")
    require(action, r'\["planResolution"\]\s*=\s*planResolution\.ToDictionary\(\)',
            "ambiguous write rejection no longer returns planning evidence")
    require(action, r'Run dryRun=true to inspect ranked candidates.*?explicit prefabId',
            "ambiguous write rejection no longer tells the caller how to disambiguate")

    print("OK: build_area keeps fuzzy discovery available while confirmed ambiguous writes fail closed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
