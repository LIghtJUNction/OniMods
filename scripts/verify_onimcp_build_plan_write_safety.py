#!/usr/bin/env python3
"""Verify fuzzy build-plan resolution cannot silently cross into confirmed writes."""

from __future__ import annotations

import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ACTION = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanningActionBuildArea.cs"
PARSER = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanTextParser.cs"
SEQUENCE_PARSER = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanTextSequenceParser.cs"
MODELS = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanTextModels.cs"
POLICY = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanExecutionSafety.cs"


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def require(source: str, pattern: str, message: str) -> None:
    if not re.search(pattern, source, re.DOTALL):
        fail(message)


def constant(source: str, name: str) -> int:
    match = re.search(rf"{re.escape(name)}\s*=\s*(\d+)\s*;", source)
    if not match:
        fail(f"missing numeric policy constant: {name}")
    return int(match.group(1))


def captured_int(source: str, pattern: str, message: str) -> int:
    match = re.search(pattern, source, re.DOTALL)
    if not match:
        fail(message)
    return int(match.group(1))


def main() -> int:
    action = ACTION.read_text(encoding="utf-8")
    parser = PARSER.read_text(encoding="utf-8")
    sequence_parser = SEQUENCE_PARSER.read_text(encoding="utf-8")
    models = MODELS.read_text(encoding="utf-8")
    policy = POLICY.read_text(encoding="utf-8")

    score_floor = constant(policy, "MinimumAutoWriteBuildingScore")
    score_gap = constant(policy, "MinimumAutoWriteBuildingGap")
    suffix_score = constant(policy, "SuffixBuildingAliasScore")
    max_order_boost = constant(policy, "MaximumPlanTermOrderBoost")

    parser_strong_floor = captured_int(
        sequence_parser,
        r"IsStrongBuildingMatch\(PlanBuildingCandidate\s+building\).*?building\.Score\s*>=\s*(\d+)",
        "cannot locate the sequence parser's strong-building score floor",
    )
    scoring_tier = captured_int(
        models,
        r"normalizedValue\.StartsWith\(normalizedQuery,\s*StringComparison\.Ordinal\).*?return\s+exactScore\s*-\s*(\d+)\s*;",
        "cannot locate the first fuzzy scoring tier in ScorePlanValue",
    )
    if score_floor != parser_strong_floor:
        fail(
            "write-safety score floor drifted from the sequence parser's strong-match floor "
            f"({score_floor} != {parser_strong_floor})"
        )
    if score_gap != scoring_tier:
        fail(
            "write-safety near-tie gap drifted from the parser's first scoring tier "
            f"({score_gap} != {scoring_tier})"
        )
    if suffix_score + max_order_boost >= score_floor:
        fail("suffix-derived alias score can cross the confirmed-write score floor")

    require(policy, r'matchKind,\s*"alias".*?matchKind,\s*"prefabId"',
            "exact alias/prefabId bypass is missing")
    require(policy, r'reason\s*=\s*"low_confidence"',
            "low-confidence fuzzy rejection is missing")
    require(policy, r'reason\s*=\s*"near_tie"',
            "near-tied fuzzy rejection is missing")

    require(parser, r"ResolveBuildingAlias\(term,\s*aliases,\s*out exactAlias\)",
            "building alias resolution no longer distinguishes exact and suffix matches")
    require(parser, r'bestKind\s*=\s*exactAlias\s*\?\s*"alias"\s*:\s*"alias_suffix"',
            "suffix-derived aliases can masquerade as exact aliases")
    require(parser, r"\(exactAlias\s*\?\s*950\s*:\s*SuffixBuildingAliasScore\)\s*\+\s*orderBoost",
            "suffix-derived aliases no longer use the bounded discovery-only score")
    require(parser, r"MaximumPlanTermOrderBoost\s*-\s*termIndex",
            "alias order boost no longer shares the write-safety bound")

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
