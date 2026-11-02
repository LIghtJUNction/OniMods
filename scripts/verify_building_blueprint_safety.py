#!/usr/bin/env python3
"""Verify that normal utility paths retain blueprint placement semantics."""

from __future__ import annotations

from contextlib import redirect_stderr
from io import StringIO
from pathlib import Path

from onimcp_verify_parsing import fail, matching_delimiter


def extract_block(text: str, marker: str) -> str:
    marker_index = text.find(marker)
    if marker_index < 0:
        fail(f"missing marker: {marker}")
    open_index = text.find("{", marker_index)
    if open_index < 0:
        fail(f"missing block after marker: {marker}")
    close_index = matching_delimiter(text, open_index, "{", "}")
    return text[open_index : close_index + 1]


def require_order(text: str, markers: tuple[str, ...], label: str) -> None:
    cursor = 0
    for marker in markers:
        position = text.find(marker, cursor)
        if position < 0:
            fail(f"{label}: expected ordered markers {markers}")
        cursor = position + len(marker)


def verify_auto_material_selection(materials: str) -> None:
    """Lock the native per-slot recipe and local-stock guards, not a stale first-item choice."""
    select_elements = extract_block(materials, "private static MaterialSelection SelectElements")
    auto_selection = extract_block(select_elements, "if (auto)")
    require_order(
        auto_selection,
        (
            "var recipe = ConstructionRecipeAdapter.Read(def.Mass, def.MaterialCategory);",
            "var autoCandidates = AvailableMaterials(def, worldId, includeUnavailable: true);",
            "if (recipe.Known)",
            "ConstructionRecipeAdapter.SelectAvailable(",
            "recipe,",
            "autoCandidates,",
            "MaterialMatchesAnyCategory(item.Tag, ParseMaterialCategoryExpression(category).ToList())",
            "item => item.AvailableKg > 0f",
            "item => item.ReachableLooseKg > 0f",
            "if (slotMaterials == null)",
            "var underSupplied = Enumerable.Range(0, recipe.Slots.Count)",
            ".GroupBy(index => slotMaterials[index].Tag.Name",
            "group.Sum(index => recipe.Slots[index].RequiredKg)",
            "if (underSupplied != null)",
            "var elements = slotMaterials.Select(item => item.Tag).ToList();",
            'return ValidatedMaterialSelection(elements, "auto",',
            'return MaterialSelection.Invalid("Native construction recipe is unknown;',
        ),
        "automatic construction must select all recipe slots from local inventory and reject unknown recipes",
    )
    for marker, label in (
        ("if (slotMaterials == null)", "missing recipe slot"),
        ("if (underSupplied != null)", "aggregated material shortage"),
    ):
        guard = extract_block(auto_selection, marker)
        if "return MaterialSelection.Invalid(" not in guard:
            fail(f"automatic selection must reject {label} before placement")

    inventory = extract_block(materials, "private static List<BuildMaterialInfo> AvailableMaterials")
    require_order(
        inventory,
        (
            "CandidateMaterialTags(def, worldId)",
            ".Where(item => item.ValidForBuilding)",
            ".Where(item => includeUnavailable || item.AvailableKg > 0f)",
            ".OrderByDescending(item => item.ReachableLooseKg)",
        ),
        "available materials must be world-scoped and filtered by stock",
    )


def verify_auto_material_selection_regressions(materials: str) -> None:
    """Ensure the source contract rejects unsafe changes to each availability gate."""
    verify_auto_material_selection(materials)
    mutations = (
        ("skip stock predicate", "item => item.AvailableKg > 0f,", "item => true,"),
        ("accept unknown recipe", "if (recipe.Known)", "if (true)"),
        ("drop full candidate set", "var autoCandidates = AvailableMaterials(def, worldId, includeUnavailable: true);",
         "var autoCandidates = AvailableMaterials(def, worldId, includeUnavailable: false);"),
        ("ignore missing slot", "if (slotMaterials == null)", "if (false)"),
        ("ignore aggregated shortage", "if (underSupplied != null)", "if (false)"),
        ("ignore repeated material across slots", ".GroupBy(index => slotMaterials[index].Tag.Name,",
         ".GroupBy(index => index.ToString(),"),
        ("select no materials", "var elements = slotMaterials.Select(item => item.Tag).ToList();",
         "var elements = new List<Tag>();"),
        ("accept recipe without slots",
         'return MaterialSelection.Invalid("Native construction recipe is unknown; cannot select a complete material set.",',
         'return MaterialSelection.Invalid("unknown",'),
        ("ignore inventory filtering", ".Where(item => includeUnavailable || item.AvailableKg > 0f)",
         ".Where(item => includeUnavailable || true)"),
    )
    for label, before, after in mutations:
        if before not in materials:
            fail(f"material regression fixture no longer matches source: {label}")
        changed = materials.replace(before, after, 1)
        with redirect_stderr(StringIO()):
            try:
                verify_auto_material_selection(changed)
            except SystemExit:
                continue
        fail(f"material recipe verifier accepted unsafe change: {label}")
    print(f"OK: {len(mutations)} native recipe and stock guard mutations were rejected")


def verify_existing_material_reuse(plan_one: str) -> None:
    early_guard = (
        'if (earlyExistingBuild != null && ExistingMaterialRequestSatisfied('
        'def, earlyExistingBuild, args["material"]?.ToString()))'
    )
    require_order(
        plan_one,
        (
            "var earlyExistingBuild = ExistingMatchingBuildAtPlacement(def, earlyPlacement);",
            early_guard,
            'var materialResult = SelectElements(def, args["material"]?.ToString(), worldId);',
        ),
        "early reuse must test the explicit material in the branch condition",
    )

    # Check each actual reuse block. A global count can be satisfied by an
    # unrelated call and cannot prove that both paths reject before completion.
    for target in ("existingBuild", "executionExistingBuild"):
        branch = extract_block(plan_one, f"if ({target} != null)")
        reuse = "RegisterSupportBlueprint" if target == "existingBuild" else "return ExistingPlacementResult"
        require_order(
            branch,
            (
                "var materialMismatch = ExistingMaterialMismatchResult(",
                f'prefabId, x, y, {target}, materialResult, args["material"]?.ToString());',
                "if (materialMismatch != null)",
                "return materialMismatch;",
                reuse,
            ),
            f"{target} must reject its own material mismatch before reuse or completion",
        )


def verify_existing_material_reuse_regressions(plan_one: str) -> None:
    """Mutation checks for the source guard, not ONI runtime acceptance."""
    verify_existing_material_reuse(plan_one)
    early_test = (
        ' && ExistingMaterialRequestSatisfied('
        'def, earlyExistingBuild, args["material"]?.ToString())'
    )
    mutations = [("missing early guard", plan_one.replace(early_test, "", 1))]
    for target in ("existingBuild", "executionExistingBuild"):
        branch = extract_block(plan_one, f"if ({target} != null)")
        reuse = "RegisterSupportBlueprint" if target == "existingBuild" else "return ExistingPlacementResult"
        guard_start = branch.index("                var materialMismatch")
        completion_start = branch.rfind("\n", 0, branch.index(reuse)) + 1
        guard = branch[guard_start:completion_start]
        completion_end = branch.index("\n", completion_start) + 1
        late_guard = (
            branch[:guard_start]
            + branch[completion_start:completion_end]
            + guard
            + branch[completion_end:]
        )
        for label, broken in (
            ("missing guard", branch.replace(guard, "", 1)),
            ("wrong target", branch.replace(f"{target}, materialResult,", "otherBuild, materialResult,", 1)),
            ("late guard", late_guard),
        ):
            mutations.append((f"{target}: {label}", plan_one.replace(branch, broken, 1)))

    for label, mutated in mutations:
        if mutated == plan_one:
            fail(f"material guard regression did not change its fixture: {label}")
        with redirect_stderr(StringIO()):
            try:
                verify_existing_material_reuse(mutated)
            except SystemExit:
                continue
        fail(f"material guard verifier accepted broken source: {label}")
    print(f"OK: {len(mutations)} material reuse guard mutations were rejected")


def verify_building_blueprint_safety(
    root: Path, sources: dict[Path, str] | None = None
) -> None:
    build_root = root / "mods" / "OniMcp" / "Tools" / "Impl" / "Build"
    paths = {
        "placement": build_root / "BuildPlanningActionPlacement.cs",
        "plan_one": build_root / "BuildPlanningPlanOne.cs",
        "materials": build_root / "BuildPlanningMaterials.cs",
        "runtime": build_root / "BuildPlanningRuntimePlacement.cs",
        "coordinates": build_root / "BuildPlanningPlacementCoordinates.cs",
    }
    for path in paths.values():
        if not path.is_file():
            fail(f"required source file not found: {path.relative_to(root)}")

    if sources is None:
        selected = {path: path.read_text(encoding="utf-8") for path in paths.values()}
    else:
        selected = sources

    auto_connect = extract_block(
        selected[paths["placement"]],
        "public static McpTool AutoConnectUtility()",
    )
    require_order(
        auto_connect,
        (
            "ValidateUtilityPathSafety",
            "foreach (var point in path)",
            "TryPlanOne(def.PrefabID, point.x, point.y",
        ),
        "utility auto-connect must validate the whole path before per-cell planning",
    )
    if "TryPlaceUtilityPathNative" in auto_connect:
        fail("utility auto-connect must not activate shared player input tools")
    path_loop = extract_block(auto_connect, "foreach (var point in path)")
    if "TryPlanOne(def.PrefabID, point.x, point.y" not in path_loop:
        fail("utility fallback path loop must call TryPlanOne for each cell")

    plan_one = extract_block(
        selected[paths["plan_one"]],
        "private static Dictionary<string, object> TryPlanOne",
    )
    require_order(plan_one, ("ValidateFootprint", "TryPlaceRequestedBuilding(def, placement,", 'return ErrorResult(prefabId, x, y, "Placement failed"',
                              '["blueprintPlaced"] = true,', '["buildingCompleted"] = false,'),
                  "native placement must report blueprints, not completed work")
    native_place = extract_block(selected[paths["coordinates"]], "private static GameObject TryPlaceRequestedBuilding")
    if "def.TryPlace(null, placement.PlacementPoint, placement.Orientation, elements, facadeId)" not in native_place:
        fail("native placement must use the single converted preview point and requested orientation")
    for forbidden in ("TryBuildVirtualFileInstantBuild", "InstantCompletionFailureResult", "completedImmediately"):
        if forbidden in plan_one:
            fail("removed completion path: " + forbidden)

    materials = selected[paths["materials"]]
    select_elements = extract_block(
        materials,
        "private static MaterialSelection SelectElements",
    )
    auto_selection = extract_block(select_elements, "if (auto)")
    if "IsFreeBuildContext" in materials:
        fail("material selection must never bypass inventory")
    verify_auto_material_selection(materials)
    if "MaterialSelection.Success(" in select_elements:
        fail("SelectElements success exits must use the unified validation helper")
    # Native recipes now have four success paths: automatic, category,
    # explicit slot-complete and explicit fallback for an unknown recipe.
    # Each path must pass through the same material validation helper.
    if select_elements.count("return ValidatedMaterialSelection(") != 4:
        fail("all four SelectElements success exits must use the unified validation helper")

    validated_success = extract_block(
        materials,
        "private static MaterialSelection ValidatedMaterialSelection",
    )
    require_order(
        validated_success,
        (
            "if (elements == null || elements.Count == 0)",
            "ElementLoader.GetElement(elements[0]) == null",
            "return MaterialSelection.Success(",
        ),
        "material success validation must reject missing or non-element primary materials",
    )
    if validated_success.count("return MaterialSelection.Invalid(") < 2:
        fail("material success validation must reject empty and invalid primary elements")

    verify_existing_material_reuse(plan_one)

    runtime_existing = extract_block(
        selected[paths["runtime"]],
        "private static Dictionary<string, object> ExistingMatchingBuildAtPlacement",
    )
    if runtime_existing.count('["material"]') < 2:
        fail("existing completed buildings and blueprints must both report their construction material identity")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    verify_building_blueprint_safety(root)
    plan_path = root / "mods/OniMcp/Tools/Impl/Build/BuildPlanningPlanOne.cs"
    plan_one = extract_block(
        plan_path.read_text(encoding="utf-8"),
        "private static Dictionary<string, object> TryPlanOne",
    )
    verify_existing_material_reuse_regressions(plan_one)
    materials_path = root / "mods/OniMcp/Tools/Impl/Build/BuildPlanningMaterials.cs"
    verify_auto_material_selection_regressions(materials_path.read_text(encoding="utf-8"))
    print("OK: normal utility paths use native placement and preserve material identity")


if __name__ == "__main__":
    main()
