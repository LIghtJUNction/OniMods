#!/usr/bin/env python3
"""Verify scoped instant-build and world-editor sandbox policy guards."""

from pathlib import Path

from oni_mcp_verify_parsing import fail


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        fail(f"missing {label}: {needle}")


def verify_world_editor_sandbox_policy(root: Path) -> None:
    tools = root / "mods" / "oni_mcp" / "Tools"
    policy_path = tools / "WorldEditor" / "WorldEditorSandboxPolicy.cs"
    if not policy_path.is_file():
        fail("missing world-editor sandbox policy implementation")
    policy = policy_path.read_text(encoding="utf-8")
    world = (tools / "WorldEditor" / "WorldEditorTools.cs").read_text(encoding="utf-8")
    execution = (tools / "WorldEditor" / "WorldEditorExecutionPolicy.cs").read_text(encoding="utf-8")
    require(world, "Handler = HandleWorldEditorScoped", "scoped world-editor entry")
    require(world, 'case "sandbox":', "sandbox command route")
    for parameter in (
        "allowSandbox", "instantBuild", "allowForce", "allowTerrainMutation",
        "allowEntitySpawn", "allowDestroy", "sandboxMaxCells",
    ):
        require(world, f'["{parameter}"] = new McpToolParameter', f"sandbox policy parameter {parameter}")
    require(policy, "bool previous = DebugHandler.InstantBuildMode;", "instant-build prior state capture")
    require(policy, "DebugHandler.InstantBuildMode = false;", "default instant-build disable")
    require(policy, "DebugHandler.InstantBuildMode = previous;", "instant-build restore")
    require(policy, "EnforceNormalMaterialRules = true;", "default material consumption")
    require(policy, "EnforceNormalMaterialRules = !instantBuild;", "instant-build material override")
    require(policy, "EnforceNormalMaterialRules = previousMaterialRules;", "material policy restore")
    require(policy, "finally", "instant-build finally guard")
    require(policy, "instantBuild=true requires allowSandbox=true and confirm=true", "instant-build authorization")
    require(policy, "allowSandbox=true and confirm=true", "sandbox write master gate")
    require(policy, "allowTerrainMutation", "terrain mutation gate")
    require(policy, "allowEntitySpawn", "entity spawn gate")
    require(policy, "allowDestroy", "destroy gate")
    require(policy, "allowForce", "force gate")
    require(policy, "Math.Min(1000", "sandbox max-cell hard cap")
    require(policy, "GameControlTools.ControlGame().Handler", "existing sandbox forwarding")
    require(execution, "InheritWorldEditorSandboxPolicy", "batch sandbox policy inheritance")
    require(policy, "parentAllowed && childAllowed", "child cannot widen parent permissions")
    require(policy, "Math.Min(parentMaxCells, childMaxCells)", "child max cells cannot widen parent")
    materials = (tools / "Impl" / "Build" / "BuildPlanningMaterials.cs").read_text(encoding="utf-8")
    require(materials, "WorldEditorTools.EnforceNormalMaterialRules", "normal world-editor material rules enforced")
    force_gate = policy.index("Sandbox force=true requires allowForce=true")
    read_return = policy.index("if (read)")
    if force_gate > read_return:
        fail("sandbox force gate must run before the read-only early return")
    operations = (tools / "WorldEditor" / "WorldEditorOperationFiles.cs").read_text(encoding="utf-8")
    require(operations, "ValidateWorldEditorSandboxPolicy(arguments, out error)", "operation-file sandbox gate")
    require(operations, "RunWithWorldEditorInstantBuildScope(arguments", "operation child scoped instant build")
    require(world, "ValidateWorldEditorSandboxPolicy(step", "batch child sandbox gate")
    require(world, "return HandleWorldEditorScoped(step);", "world-editor batch child scoped instant build")
    require(world, "RunWithWorldEditorInstantBuildScope(step", "non-world batch child scoped instant build")
    require(policy, "RunWithWorldEditorInstantBuildScope", "nested instant-build scope helper")
    require(policy, "forwarded[key] = ToolUtil.GetBool(args, key, false);", "payload cannot widen sandbox flags")
    require(policy, 'forwarded["confirm"] = ToolUtil.GetBool(args, "confirm", false);', "payload cannot grant sandbox confirmation")
    skill = (root / ".agents" / "skills" / "oni-gameplay" / "SKILL.md").read_text(encoding="utf-8")
    reference = (root / ".agents" / "skills" / "oni-gameplay" / "references" / "world-editor.md").read_text(encoding="utf-8")
    reference_zh = (root / ".agents" / "skills" / "oni-gameplay" / "references" / "world-editor.zh.md").read_text(encoding="utf-8")
    for text, label in ((skill, "skill"), (reference, "English reference"), (reference_zh, "Chinese reference")):
        require(text, "instantBuild=false", f"{label} default blueprint policy")
        require(text, "allowSandbox=true", f"{label} explicit sandbox authorization")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    verify_world_editor_sandbox_policy(root)
    print("OK: scoped world-editor sandbox and instant-build policy")


if __name__ == "__main__":
    main()
