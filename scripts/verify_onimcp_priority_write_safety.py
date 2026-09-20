#!/usr/bin/env python3
"""Verify single-target priority writes reject inactive Prioritizable targets."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
RESOLVER = ROOT / "mods/OniMcp/Tools/Impl/Orders/OrdersPriorityTargetResolver.cs"
TOOLS = ROOT / "mods/OniMcp/Tools/Impl/Orders/OrdersPriorityTools.cs"


def method_slice(source: str, start: str, end: str) -> str:
    start_index = source.find(start)
    if start_index < 0:
        raise RuntimeError(f"missing method marker: {start}")
    end_index = source.find(end, start_index + len(start))
    if end_index < 0:
        raise RuntimeError(f"missing method boundary: {end}")
    return source[start_index:end_index]


def main() -> int:
    resolver = RESOLVER.read_text(encoding="utf-8")
    tools = TOOLS.read_text(encoding="utf-8")

    set_building = method_slice(
        tools,
        "public static McpTool SetBuildingPriority()",
        "public static McpTool SetPriorityArea()",
    )
    if "TryFindPriorityTarget(args, out go, out error)" not in set_building:
        raise RuntimeError("single-target priority write no longer uses the audited resolver")
    if "prioritizable.SetMasterPriority(setting);" not in set_building:
        raise RuntimeError("single-target priority mutation site was not found")

    resolver_method = method_slice(
        resolver,
        "private static bool TryFindPriorityTarget(",
        "private static List<GameObject> PriorityTargetCandidates(",
    )
    policy_call = "PriorityWriteEligibilityPolicy.TryValidate"
    eligibility = "prioritizable.IsPrioritizable()"
    if policy_call not in resolver_method or eligibility not in resolver_method:
        raise RuntimeError(
            "single-target resolver can still return an inactive Prioritizable before SetMasterPriority"
        )

    if resolver_method.find(policy_call) > resolver_method.rfind("target ="):
        raise RuntimeError("priority eligibility gate must run before the resolver returns its target")

    area = method_slice(
        tools,
        "public static McpTool SetPriorityArea()",
        "public static McpTool ControlPriority()",
    )
    if "MatchesPriorityTarget(prioritizable, rect, worldId, query, includeInactive)" not in area:
        raise RuntimeError("area priority behavior changed outside this fix")

    print("OniMcp single-target priority write safety contract passed")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except RuntimeError as error:
        print(f"FAIL: {error}", file=sys.stderr)
        sys.exit(1)
