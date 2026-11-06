#!/usr/bin/env python3
"""Both filter routers must validate native eligibility before filter mutations."""

from pathlib import Path
import sys

from onimcp_verify_parsing import matching_delimiter

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "mods/OniMcp/Tools/Impl/Build"
FILES = {
    "filters": BUILD / "FilterTreeWrite.cs",
    "storage": BUILD / "StorageTools.cs",
    "selection": BUILD / "TreeFilterSelection.cs",
    "policy": BUILD / "TreeFilterEligibility.cs",
}


def body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise ValueError(f"missing {signature}")
    opening = source.find("{", start)
    closing = matching_delimiter(source, opening, "{", "}")
    return source[opening:closing + 1]


def verify(sources: dict[str, str]) -> list[str]:
    failures = []

    def require(condition: bool, message: str) -> None:
        if not condition:
            failures.append(message)

    def ordered(source: str, markers: tuple[str, ...], message: str) -> None:
        offset = 0
        for marker in markers:
            found = source.find(marker, offset)
            if found < 0:
                failures.append(message)
                return
            offset = found + len(marker)

    tree = body(sources["filters"], "private static CallToolResult SetTreeFilter")
    storage = body(sources["storage"], "public static McpTool SetStorageFilter")
    for label, handler, call in (
        ("tree", tree, "TreeFilterSelection.TryApply(tree, requested, mode, out error)"),
        ("storage", storage, "TreeFilterSelection.TryApply(filterable, tags, mode, out error)"),
    ):
        ordered(handler, ("if (!" + call + ")", "return CallToolResult.Error(error);", "return CallToolResult.Text"),
                label + " router must propagate shared eligibility rejection before success")
        require(".UpdateFilters(" not in handler and ".SelectTag(" not in handler,
                label + " router must not bypass the shared mutation boundary")

    options = body(sources["selection"], "internal static HashSet<Tag> GetOptions")
    require("TreeFilterSelectableTags.For(tree.gameObject, tree)" in options,
            "tree options must preserve native category/discovery rules")
    ordered(options, ("if (!flat.currentlyUserAssignable)", "return options;", "flat.tagOptions.Where"),
            "disabled flat filters must not offer assignable options")
    require("!flat.displayOnlyDiscoveredTags" in options
            and "|| (discovered != null && discovered.IsDiscovered(tag))" in options,
            "flat options must match native discovery visibility and reject missing discovery")
    require("options.ExceptWith(tree.ForbiddenTags)" in options,
            "forbidden tags must be removed before option validation")

    apply = body(sources["selection"], "internal static bool TryApply")
    ordered(apply, ("if (flat != null && !flat.currentlyUserAssignable)", "return false;",
                    "if (!TreeFilterEligibility.TryApply(mode,", "GetOptions(tree)",
                    "out selected, out error))", "return false;", "TreeFilterEligibility.PlanFlatUpdates(",
                    "flat.SelectTag(", "tree.UpdateFilters("),
            "all eligibility guards must reject before ordered native writes")
    require("flat.selectedTags.Select(tag => tag.Name), selected," in apply
            and "flat.tagOptions.Select(tag => tag.Name)" in apply,
            "flat mutation planning must use actual selections and valid native options")
    require("flat.SelectTag(flat.tagOptions.First(tag => tag.Name == edit.Key), edit.Value)" in apply,
            "SelectTag must execute only edits authorized by the flat plan")
    require("tree.UpdateFilters(new HashSet<Tag>(flat.selectedTags))" in apply
            and "tree.UpdateFilters(new HashSet<Tag>(selected.Select(name => new Tag(name))))" in apply,
            "flat and tree paths must publish their actual final selection")

    policy = sources["policy"]
    plan = body(policy, "internal static List<KeyValuePair<string, bool>> PlanFlatUpdates")
    ordered(plan, ("desiredSet.Where(optionSet.Contains).Except(currentSet)",
                   "plan.Add(new KeyValuePair<string, bool>(tag, true))",
                   "currentSet.Where(optionSet.Contains).Except(desiredSet)",
                   "plan.Add(new KeyValuePair<string, bool>(tag, false))"),
            "flat plans must add before removal and never call native SelectTag for invalid saved options")
    require("StorageLocker" not in policy and "DefinitelyNotARealOniResource" not in policy,
            "eligibility must not special-case a building or hard-code a resource list")
    return failures


def main() -> int:
    sources = {name: path.read_text(encoding="utf-8") for name, path in FILES.items()}
    failures = verify(sources)
    if failures:
        for failure in failures:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1

    mutations = (
        ("filters", "if (!TreeFilterSelection.TryApply(tree, requested, mode, out error))",
         "if (TreeFilterSelection.TryApply(tree, requested, mode, out error))"),
        ("storage", "if (!TreeFilterSelection.TryApply(filterable, tags, mode, out error))",
         "if (TreeFilterSelection.TryApply(filterable, tags, mode, out error))"),
        ("selection", "TreeFilterSelectableTags.For(tree.gameObject, tree)", "new HashSet<string>()"),
        ("selection", "if (!flat.currentlyUserAssignable)", "if (false)"),
        ("selection", "if (flat != null && !flat.currentlyUserAssignable)", "if (false)"),
        ("selection", "!flat.displayOnlyDiscoveredTags", "true"),
        ("selection", "discovered != null && discovered.IsDiscovered(tag)", "true"),
        ("selection", "options.ExceptWith(tree.ForbiddenTags)", "options.ExceptWith(new Tag[0])"),
        ("selection", "if (!TreeFilterEligibility.TryApply(mode,", "if (TreeFilterEligibility.TryApply(mode,"),
        ("selection", "TreeFilterEligibility.PlanFlatUpdates(", "UnsafeFlatUpdates("),
        ("selection", "edit.Key), edit.Value)", "edit.Key), false)"),
        ("policy", "currentSet.Where(optionSet.Contains).Except(desiredSet)", "currentSet.Except(desiredSet)"),
        ("policy", "plan.Add(new KeyValuePair<string, bool>(tag, true))", "plan.Add(new KeyValuePair<string, bool>(tag, false))"),
    )
    for name, before, after in mutations:
        if before not in sources[name]:
            print(f"FAIL: mutation fixture no longer matches {name}: {before}", file=sys.stderr)
            return 1
        changed = dict(sources)
        changed[name] = changed[name].replace(before, after, 1)
        if not verify(changed):
            print(f"FAIL: source contract accepted unsafe mutation in {name}: {before}", file=sys.stderr)
            return 1
    print(f"PASS OniMcp tree filter eligibility source contract ({len(mutations)} unsafe mutations rejected)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
