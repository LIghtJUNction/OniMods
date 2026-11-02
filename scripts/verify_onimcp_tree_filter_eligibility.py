#!/usr/bin/env python3
"""Tree and storage filter writes must consult eligibility before UpdateFilters."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
FILTERS = ROOT / "mods/OniMcp/Tools/Impl/Build/FilterTools.cs"
STORAGE = ROOT / "mods/OniMcp/Tools/Impl/Build/StorageTools.cs"
POLICY = ROOT / "mods/OniMcp/Tools/Impl/Build/TreeFilterEligibility.cs"


def body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"missing {signature}")
    next_method = source.find("\n        private static ", start + len(signature))
    if next_method < 0:
        next_method = source.find("\n        public static ", start + len(signature))
    return source[start:next_method if next_method > start else None]


def require(condition: bool, message: str, failures: list) -> None:
    if not condition:
        failures.append(message)


def main() -> int:
    filters = FILTERS.read_text(encoding="utf-8")
    storage = STORAGE.read_text(encoding="utf-8")
    policy = POLICY.read_text(encoding="utf-8")
    failures = []

    tree = body(filters, "private static CallToolResult SetTreeFilter")
    require("TreeFilterEligibility.TryApply" in tree, "SetTreeFilter must apply tree eligibility", failures)
    require("tree.UpdateFilters" in tree, "SetTreeFilter must still call UpdateFilters", failures)
    require(
        tree.find("TreeFilterEligibility.TryApply") < tree.find("tree.UpdateFilters"),
        "SetTreeFilter must reject an ineligible tag before UpdateFilters",
        failures,
    )
    require("TreeFilterSelectableTags.For" in tree, "SetTreeFilter must use the shared selectable-tag source", failures)
    flat = body(filters, "private static void ApplyFlatTags")
    require("TreeFilterEligibility.PlanFlatUpdates" in flat, "flat updates must use the ordered plan", failures)
    require(
        flat.find("PlanFlatUpdates") < flat.find("SelectTag"),
        "flat SelectTag calls must follow the add-then-remove plan",
        failures,
    )
    require("tagOptions" in flat and "SelectTag" in flat, "flat tagOptions handling must remain", failures)

    handler = storage[storage.find("Name = \"resources_storage_set_filter\""):storage.find("public static McpTool ControlStorage")]
    require("TreeFilterEligibility.TryApply" in handler, "storage set_filter must apply tree eligibility", failures)
    require("UpdateFilters" in handler, "storage set_filter must still call UpdateFilters", failures)
    require(
        handler.find("TreeFilterEligibility.TryApply") < handler.find("UpdateFilters"),
        "storage set_filter must reject an ineligible tag before UpdateFilters",
        failures,
    )
    require("StorageLocker" not in policy, "eligibility must not special-case StorageLocker", failures)
    require("DefinitelyNotARealOniResource" not in policy, "the policy must not hard-code a resource list", failures)

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1
    print("PASS OniMcp tree filter eligibility source contract")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
