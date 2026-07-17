#!/usr/bin/env python3
"""Verify editable per-building world-editor virtual files."""

from pathlib import Path

from onimcp_verify_parsing import fail


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        fail(f"missing {label}: {needle}")


def verify_world_editor_building_files(root: Path) -> None:
    tools = root / "mods" / "OniMcp" / "Tools" / "WorldEditor"
    files = {
        name: (tools / name).read_text(encoding="utf-8")
        for name in (
            "WorldEditorBuildingFiles.cs",
            "WorldEditorReadSearch.cs",
            "WorldEditorEdits.cs",
            "WorldEditorListing.cs",
            "WorldEditorMapGrid.cs",
            "WorldEditorActiveIndex.cs",
        )
        if (tools / name).is_file()
    }
    if "WorldEditorBuildingFiles.cs" not in files:
        fail("missing per-building virtual-file implementation")
    building = files["WorldEditorBuildingFiles.cs"]
    for method in (
        "ReadBuildingIndexMarkdown", "ReadBuildingDetailMarkdown",
        "ResolveBuildingDetailFile", "PreflightBuildingDetailEdit",
        "ApplyBuildingDetailEdit", "AppendBuildingParameterReferences",
    ):
        require(building, method, f"building file method {method}")
    require(building, "KPrefabID", "stable building InstanceID")
    require(building, '"buildings/instances/"', "building instance path")
    require(building, "BuildingConfigTools.SnapshotConfig", "shared config snapshot")
    require(building, "StateControlTools.SnapshotState", "shared state snapshot")
    require(building, "BuildingControlTools.ControlBuilding().Handler", "existing building setter reuse")
    require(building, "readonly or unknown building parameter", "readonly edit rejection")
    preflight = building.split("private static CallToolResult PreflightBuildingDetailEdit", 1)[1].split(
        "private static CallToolResult ApplyBuildingDetailEdit", 1
    )[0]
    if "ControlBuilding().Handler" in preflight:
        fail("building parameter preflight must not call a mutating setter")
    for field in (
        "Enabled", "Toggle", "Threshold.", "Slider.", "Valve.Flow",
        "LimitValve.Limit", "LogicTimer.OnSeconds", "LogicTimer.OffSeconds",
        "LogicTimer.DisplayCycles", "Ribbon.SelectedBit", "Door.State",
        "Capacity", "Checkbox", "Counter.Max", "Counter.Advanced",
        "TimeRange.Start", "TimeRange.Duration",
    ):
        require(building, field, f"editable building field {field}")
    require(files["WorldEditorReadSearch.cs"], 'relative == "buildings/index.md"', "building index read route")
    require(files["WorldEditorReadSearch.cs"], "IsBuildingDetailMarkdown(relative)", "building detail read route")
    require(files["WorldEditorEdits.cs"], "PreflightBuildingDetailEdit", "building detail preflight route")
    require(files["WorldEditorEdits.cs"], "ApplyBuildingDetailEdit", "building detail apply route")
    require(files["WorldEditorListing.cs"], 'add("instances/", "dir"', "building instance directory listing")
    require(files["WorldEditorMapGrid.cs"], "AppendBuildingParameterReferences", "compact map building references")
    require(files["WorldEditorActiveIndex.cs"], "/active/buildings/index.md", "active building file hint")
    require(files["WorldEditorActiveIndex.cs"], "/active/buildings/instances/", "active building instance directory hint")
    skill = (root / ".agents" / "skills" / "oni-gameplay" / "SKILL.md").read_text(encoding="utf-8")
    reference = (root / ".agents" / "skills" / "oni-gameplay" / "references" / "world-editor.md").read_text(encoding="utf-8")
    reference_zh = (root / ".agents" / "skills" / "oni-gameplay" / "references" / "world-editor.zh.md").read_text(encoding="utf-8")
    for text, label in ((skill, "skill"), (reference, "English reference"), (reference_zh, "Chinese reference")):
        require(text, "/active/buildings/index.md", f"{label} building parameter index")
        require(text, "/active/buildings/instances/", f"{label} building instance files")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    verify_world_editor_building_files(root)
    print("OK: editable per-building world-editor virtual files")


if __name__ == "__main__":
    main()
