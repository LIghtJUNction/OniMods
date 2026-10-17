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
        "native": build_root / "BuildPlanningNativeUtilityPath.cs",
        "placement": build_root / "BuildPlanningActionPlacement.cs",
        "plan_one": build_root / "BuildPlanningPlanOne.cs",
        "materials": build_root / "BuildPlanningMaterials.cs",
        "runtime": build_root / "BuildPlanningRuntimePlacement.cs",
    }
    for path in paths.values():
        if not path.is_file():
            fail(f"required source file not found: {path.relative_to(root)}")

    if sources is None:
        selected = {path: path.read_text(encoding="utf-8") for path in paths.values()}
    else:
        selected = sources

    native_method = extract_block(
        selected[paths["native"]],
        "private static Dictionary<string, object> TryPlaceUtilityPathNative",
    )
    require_order(native_method, ("ValidateUtilityPathSafety", "SelectElements", "SelectUtilityBuildTool"),
                  "native path safety and materials must precede tool execution")
    if "IsFreeBuildContext" in native_method:
        fail("native utility paths cannot bypass material rules")

    auto_connect = extract_block(
        selected[paths["placement"]],
        "public static McpTool AutoConnectUtility()",
    )
    require_order(
        auto_connect,
        (
            "TryPlaceUtilityPathNative",
            'GetBool(nativePath, "success")',
            'GetBool(nativePath, "shouldFallback")',
            "foreach (var point in path)",
            "TryPlanOne(def.PrefabID, point.x, point.y",
        ),
        "utility auto-connect must continue from native fallback into per-cell planning",
    )
    path_loop = extract_block(auto_connect, "foreach (var point in path)")
    if "TryPlanOne(def.PrefabID, point.x, point.y" not in path_loop:
        fail("utility fallback path loop must call TryPlanOne for each cell")

    plan_one = extract_block(
        selected[paths["plan_one"]],
        "private static Dictionary<string, object> TryPlanOne",
    )
    require_order(plan_one, ("ValidateFootprint", "def.TryPlace(", 'return ErrorResult(prefabId, x, y, "Placement failed"',
                              '["blueprintPlaced"] = true,', '["buildingCompleted"] = false,'),
                  "native placement must report blueprints, not completed work")
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
    if "available.FirstOrDefault()" not in auto_selection:
        fail("automatic selection must use available inventory")
    if "MaterialSelection.Success(" in select_elements:
        fail("SelectElements success exits must use the unified validation helper")
    if select_elements.count("return ValidatedMaterialSelection(") != 3:
        fail("all three SelectElements success exits must use the unified validation helper")

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
    print("OK: normal utility paths use native placement and preserve material identity")


if __name__ == "__main__":
    main()
