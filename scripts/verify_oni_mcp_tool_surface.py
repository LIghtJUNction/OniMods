#!/usr/bin/env python3
"""Verify the static ONI MCP public, registered, and compatibility surfaces."""

from __future__ import annotations

import re
from collections import Counter
from pathlib import Path

from oni_mcp_verify_parsing import fail, matching_delimiter


EXPECTED_DEFAULT_PUBLIC = {
    "building_control",
    "game_control",
    "navigation_control",
    "orders_control",
    "server_control",
    "world_editor",
}

EXPECTED_REGISTERED = EXPECTED_DEFAULT_PUBLIC | {
    "colony_control",
    "coordinate_control",
    "dupes_control",
    "read_control",
    "search_control",
}

EXPECTED_ALIASES = {
    "building_control": ("buildings_control", "building_system_control"),
    "colony_control": ("colony_status_control", "colony_ops_control"),
    "coordinate_control": ("coordinate_gateway", "coordinate_tool"),
    "dupes_control": ("duplicants_control", "dupe_control"),
    "game_control": ("game_system_control",),
    "navigation_control": ("spatial_control", "view_control"),
    "orders_control": ("orders", "orders_unified_control", "orders_action_control", "map_orders_control"),
    "read_control": ("state_read_control", "query_control"),
    "search_control": ("find_control", "search_action_control"),
    "server_control": ("mcp_server_control", "server_diagnostics_control", "mcp_client_request_control", "tools_catalog_control", "tools_call_many", "agent_program_execute"),
    "world_editor": ("oni_editor", "map_editor", "save_editor"),
}

EXPECTED_OPERATION_FILES = {
    "ops/any.md": "",
    "ops/game.md": "game_control",
    "ops/colony.md": "colony_control",
    "ops/read.md": "read_control",
    "ops/search.md": "search_control",
    "ops/build.md": "building_control",
    "ops/orders.md": "orders_control",
    "ops/dupes.md": "dupes_control",
    "ops/navigation.md": "navigation_control",
    "ops/coordinate.md": "coordinate_control",
    "ops/server.md": "server_control",
    "ops/tools.md": "",
    "ops/facilities.md": "",
    "ops/storage.md": "",
    "ops/power.md": "",
    "ops/automation.md": "",
    "ops/farming.md": "",
    "ops/ranching.md": "",
    "ops/rockets.md": "",
    "ops/resources.md": "",
    "ops/ui.md": "",
    "ops/medical.md": "",
    "ops/rooms.md": "",
    "ops/sandbox.md": "",
}

ALLOWED_NON_TOOL_CONTROL_REFERENCES = {
    "global_control",  # Surface-audit category, not a tool dependency.
}

REMOVED_POINTER_FILES = (
    "mods/oni_mcp/Tools/Impl/Build/BuildPlanningPointerActions.cs",
    "mods/oni_mcp/Tools/Impl/Navigation/AgentPointerModels.cs",
    "mods/oni_mcp/Tools/Impl/Navigation/AgentPointerRegistry.cs",
    "mods/oni_mcp/Tools/Impl/Navigation/AgentPointerRegistryMaintenance.cs",
)

PUBLIC_CONTRACT_FILES = (
    "README.md",
    "README_ZH.md",
    "docs/api-developer-guide.md",
    "docs/mcp-tools-reference.md",
    "mods/oni_mcp/README.md",
    "mods/oni_mcp/README_EN.md",
    "mods/oni_mcp/Tools/CoreToolEnglishDescriptions.cs",
    "mods/oni_mcp/Tools/Impl/Audit/ToolCatalogGuideTools.cs",
    "mods/oni_mcp/Tools/Impl/Audit/ToolCatalogManifestTools.cs",
    "mods/oni_mcp/Tools/Impl/Audit/ToolCoverageAnalysis.cs",
    "mods/oni_mcp/Tools/Impl/Audit/UiMenuSurfaceAuditTools.cs",
    "mods/oni_mcp/Tools/Impl/Build/BuildPlanningErrors.cs",
    "mods/oni_mcp/Tools/Impl/Build/BuildPlanningPlacementGeometry.cs",
    "mods/oni_mcp/Tools/Impl/Build/BuildPlanningTools.cs",
    "mods/oni_mcp/Tools/Impl/Server/AgentProgramTools.cs",
)

REMOVED_POINTER_PATTERNS = {
    "agent_pointer leaf": re.compile(r"\bagent_pointer(?:_[a-z0-9_]+)?\b", re.IGNORECASE),
    "agent pointer": re.compile(r"\bagent pointer\b", re.IGNORECASE),
    "pointer zh": re.compile(r"指针"),
    "invalid plan_one action": re.compile(r"\bplan_one\b", re.IGNORECASE),
    "invalid preview anchors": re.compile(r"action\s*=\s*preview[^\"\n]*anchors\s*=", re.IGNORECASE),
    "invalid candidate query": re.compile(r"action\s*=\s*placement_candidates[^\"\n]*query\s*=", re.IGNORECASE),
    "select_tool": re.compile(r"\bselect_tool\b", re.IGNORECASE),
    "left_click": re.compile(r"\bleft_click\b", re.IGNORECASE),
    "hold_left": re.compile(r"\bhold_left\b", re.IGNORECASE),
    "agentId": re.compile(r"\bagentId\b"),
    "visible pointer": re.compile(r"\bvisible (?:agent )?pointer\b", re.IGNORECASE),
    "visible pointer zh": re.compile(r"可视(?:\s*agent\s*)?指针"),
    "removed navigation pointer action": re.compile(
        r"navigation_control\s+action=(?:get|jump|aim_cell|user_mouse|"
        r"select_tool|left_click|hold_left)\b",
        re.IGNORECASE,
    ),
}

DISABLED_KNOWLEDGE_PATTERNS = {
    "disabled read knowledge domain": re.compile(r"read_control\s+domain=(?:knowledge|database|guide)\b", re.IGNORECASE),
    "removed database_query": re.compile(r"\bdatabase_query\b", re.IGNORECASE),
    "removed guide_mechanics_query": re.compile(r"\bguide_mechanics_query\b", re.IGNORECASE),
}


def extract_block(text: str, marker: str) -> str:
    marker_index = text.find(marker)
    if marker_index < 0:
        fail(f"required marker not found: {marker}")
    start = text.find("{", marker_index)
    if start < 0:
        fail(f"opening brace not found after marker: {marker}")
    end = matching_delimiter(text, start, "{", "}")
    return text[start + 1 : end]


def extract_calls(text: str, call_name: str) -> list[str]:
    calls: list[str] = []
    for match in re.finditer(rf"\b{re.escape(call_name)}\s*\(", text):
        start = text.find("(", match.start())
        end = matching_delimiter(text, start, "(", ")")
        calls.append(text[start + 1 : end])
    return calls


def class_bodies(
    type_name: str, sources: dict[Path, str]
) -> list[tuple[Path, str, str]]:
    pattern = re.compile(
        rf"\b(?:public|internal|private)\s+static\s+(?:partial\s+)?class\s+"
        rf"{re.escape(type_name)}\b"
    )
    results: list[tuple[Path, str, str]] = []
    for path, source in sources.items():
        for match in pattern.finditer(source):
            start = source.find("{", match.end())
            end = matching_delimiter(source, start, "{", "}")
            results.append((path, source[start + 1 : end], source))
    return results


def resolve_factory(
    type_name: str,
    method_name: str,
    sources: dict[Path, str],
    seen: set[tuple[str, str]] | None = None,
) -> dict[str, object] | None:
    seen = set() if seen is None else seen
    key = (type_name, method_name)
    if key in seen:
        fail(f"factory forwarding cycle at {type_name}.{method_name}")
    seen.add(key)
    method_pattern = re.compile(
        rf"\b(?:public|internal|private)\s+static\s+McpTool\s+"
        rf"{re.escape(method_name)}\s*\("
    )
    for path, class_body, full_source in class_bodies(type_name, sources):
        match = method_pattern.search(class_body)
        if match is None:
            continue
        start = class_body.find("{", match.end())
        end = matching_delimiter(class_body, start, "{", "}")
        body = class_body[start + 1 : end]
        name_match = re.search(r'\bName\s*=\s*"([^"]+)"', body)
        if name_match is not None:
            canonical = name_match.group(1)
        else:
            identifier_match = re.search(r"\bName\s*=\s*([A-Za-z_]\w*)", body)
            if identifier_match is not None:
                const_match = re.search(
                    rf'\bconst\s+string\s+{re.escape(identifier_match.group(1))}'
                    rf'\s*=\s*"([^"]+)"',
                    class_body,
                )
                if const_match is not None:
                    canonical = const_match.group(1)
                else:
                    canonical = ""
            else:
                canonical = ""
        if canonical:
            aliases_match = re.search(
                r"\bAliases\s*=\s*new\s+List<string>\s*\{([^}]*)\}", body
            )
            aliases = (
                re.findall(r'"([^"]+)"', aliases_match.group(1))
                if aliases_match is not None
                else []
            )
            return {
                "canonical": canonical,
                "aliases": aliases,
                "hidden": re.search(r"\bHidden\s*=\s*true\b", body) is not None,
                "path": path,
                "factory": f"{type_name}.{method_name}",
            }
        forwarded = re.search(
            r"\breturn\s+([A-Za-z_]\w*)\.([A-Za-z_]\w*)\s*\(\s*\)\s*;",
            body,
        )
        if forwarded is not None:
            return resolve_factory(
                forwarded.group(1), forwarded.group(2), sources, seen
            )
        fail(f"{path}: cannot resolve factory {type_name}.{method_name}")
    return None


def assert_dispatch_path(registry: str, method_marker: str) -> None:
    block = extract_block(registry, method_marker)
    canonical = re.search(
        r"_tools\.TryGetValue\(name,\s*out(?:\s+var)?\s+tool\)", block
    )
    alias = re.search(
        r"_aliases\.TryGetValue\(name,\s*out(?:\s+var)?\s+canonicalName\)", block
    )
    alias_target = re.search(
        r"_tools\.TryGetValue\(canonicalName,\s*out\s+tool\)", block
    )
    if canonical is None or alias is None or alias_target is None:
        fail(f"{method_marker} must preserve canonical and alias dispatch")


def assert_no_removed_pointer_contracts(root: Path, navigation: str) -> None:
    contract_files = list(PUBLIC_CONTRACT_FILES)
    contract_files.extend(
        str(path.relative_to(root)) for path in sorted((root / ".agents" / "skills").glob("**/SKILL.md"))
    )
    contract_files.append(".codex/config.toml")
    for relative in contract_files:
        path = root / relative
        text = path.read_text(encoding="utf-8")
        for label, pattern in REMOVED_POINTER_PATTERNS.items():
            match = pattern.search(text)
            if match is not None:
                line = text.count("\n", 0, match.start()) + 1
                fail(f"{relative}:{line}: stale removed-pointer contract: {label}")
        for label, pattern in DISABLED_KNOWLEDGE_PATTERNS.items():
            for match in pattern.finditer(text):
                line = text.count("\n", 0, match.start()) + 1
                line_text = text.splitlines()[line - 1].lower()
                if not any(marker in line_text for marker in ("禁止", "不要", "disabled", "已移除", "不可用")):
                    fail(f"{relative}:{line}: callable disabled-knowledge guidance: {label}")

    params = extract_block(
        navigation, "private static Dictionary<string, McpToolParameter> Params()"
    )
    forbidden_params = (
        "pointer",
        "agentId",
        "displayText",
        "jumpPointAction",
        "select_tool",
        "left_click",
        "hold_left",
    )
    for token in forbidden_params:
        if token.lower() in params.lower():
            fail(f"NavigationControlTools Params still advertises removed token: {token}")
    public_prefix = navigation[: navigation.find("Handler =")]
    if re.search(r"\bpointer\b", public_prefix, re.IGNORECASE):
        fail("navigation_control public description/tags still advertise pointer support")
    if "return string.Empty;" not in extract_block(
        navigation, "private static string InferDomain(string action)"
    ):
        fail("unknown navigation actions must not infer the removed pointer domain")
    if "agent pointer control has been removed" not in navigation:
        fail("navigation_control must retain the explicit removed-pointer diagnostic")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    tools_root = root / "mods" / "oni_mcp" / "Tools"
    registry_path = tools_root / "Core" / "OniToolRegistry.cs"
    operation_path = tools_root / "WorldEditorOperationFiles.cs"
    navigation_path = tools_root / "NavigationControlTools.cs"
    for path in (registry_path, operation_path, navigation_path):
        if not path.is_file():
            fail(f"required source file not found: {path.relative_to(root)}")
    sources = {path: path.read_text(encoding="utf-8") for path in tools_root.rglob("*.cs")}
    registry = sources[registry_path]
    for relative in REMOVED_POINTER_FILES:
        if (root / relative).exists():
            fail(f"removed pointer file still exists: {relative}")
    dead_pointer_symbols = re.compile(
        r"\b(?:AgentPointerRegistry|AgentPointerState|AgentPointerJumpPoint|PlanAtPointer|DragLineFromPointer|FillCurrentPointerCell)\b"
    )
    for path, source in sources.items():
        if path != navigation_path and dead_pointer_symbols.search(source):
            fail(f"{path.relative_to(root)} still references a removed pointer symbol")

    public_block = extract_block(registry, "DefaultPublicToolNames")
    default_public = set(re.findall(r'"([a-z0-9_]+)"', public_block))
    if default_public != EXPECTED_DEFAULT_PUBLIC:
        fail(
            "DefaultPublicToolNames mismatch: "
            f"expected {sorted(EXPECTED_DEFAULT_PUBLIC)}, got {sorted(default_public)}"
        )

    initialize = extract_block(registry, "public static void Initialize()")
    expressions = extract_calls(initialize, "Register")
    if len(expressions) != 11:
        fail(f"Initialize must contain exactly 11 Register calls, got {len(expressions)}")
    factories: list[dict[str, object]] = []
    for expression in expressions:
        qualified_calls = re.findall(
            r"\b([A-Za-z_]\w*)\.([A-Za-z_]\w*)\s*\(", expression
        )
        factory = None
        for type_name, method_name in reversed(qualified_calls):
            factory = resolve_factory(type_name, method_name, sources)
            if factory is not None:
                break
        if factory is None:
            fail(f"cannot resolve registered expression: {expression.strip()}")
        factory["hidden"] = bool(factory["hidden"]) or "HiddenCompat" in expression
        factories.append(factory)

    registered_list = [str(factory["canonical"]) for factory in factories]
    duplicates = sorted(name for name, count in Counter(registered_list).items() if count > 1)
    if duplicates:
        fail(f"duplicate registered canonical tools: {duplicates}")
    registered = set(registered_list)
    if registered != EXPECTED_REGISTERED:
        fail(
            "registered canonical mismatch: "
            f"missing={sorted(EXPECTED_REGISTERED - registered)}, "
            f"unexpected={sorted(registered - EXPECTED_REGISTERED)}"
        )
    if not default_public <= registered:
        fail(f"default-public tools are not registered: {sorted(default_public - registered)}")

    hidden = {str(factory["canonical"]) for factory in factories if factory["hidden"]}
    if hidden != {"coordinate_control"}:
        fail(f"exactly coordinate_control must be hidden, got {sorted(hidden)}")
    if len(registered) - len(hidden) != 10:
        fail("registered/hidden counts must yield exactly 10 visible tools")

    alias_owner: dict[str, str] = {}
    actual_aliases = {
        str(factory["canonical"]): tuple(str(alias) for alias in factory["aliases"])
        for factory in factories
    }
    if actual_aliases != EXPECTED_ALIASES:
        fail(f"registered alias mapping mismatch: expected {EXPECTED_ALIASES}, got {actual_aliases}")
    for factory in factories:
        canonical = str(factory["canonical"])
        for alias in factory["aliases"]:
            alias = str(alias)
            if alias in registered:
                fail(f"alias {alias!r} for {canonical} conflicts with a canonical tool")
            if alias in alias_owner:
                fail(
                    f"duplicate alias {alias!r}: {alias_owner[alias]} and {canonical}"
                )
            alias_owner[alias] = canonical

    assert_dispatch_path(registry, "public static bool TryGetTool")
    assert_dispatch_path(registry, "public static CallToolResult CallTool")
    get_tools = extract_block(registry, "public static List<McpTool> GetTools()")
    if "_tools.Values" not in get_tools or ".Where" in get_tools:
        fail("GetTools must return all registered tools without Hidden filtering")
    visible_tools = extract_block(
        registry, "public static List<McpTool> GetVisibleTools()"
    )
    if re.search(r"\.Where\(t\s*=>\s*!t\.Hidden\)", visible_tools) is None:
        fail("GetVisibleTools must filter Hidden tools")

    normalized_registry = re.sub(r"\s+", "", registry)
    expected_filter = (
        ".Where(tool=>!tool.Hidden&&(includeAll||"
        "DefaultPublicToolNames.Contains(tool.Name)))"
    )
    if expected_filter not in normalized_registry:
        fail("BuildToolInfos must expose defaults unless includeAll is true")
    if "_cachedAllToolInfos=BuildToolInfos(includeAll:true);" not in normalized_registry:
        fail("includeAll cache must include every visible registered tool")

    operation_block = extract_block(sources[operation_path], "OperationFileTools =")
    operation_files = dict(
        re.findall(r'\["([^"]+)"\]\s*=\s*"([^"]*)"', operation_block)
    )
    if operation_files != EXPECTED_OPERATION_FILES:
        fail(
            "OperationFileTools mapping mismatch: "
            f"expected {EXPECTED_OPERATION_FILES}, got {operation_files}"
        )
    missing_operation_tools = {
        value for value in operation_files.values() if value and value not in registered
    }
    if missing_operation_tools:
        fail(f"operation files reference unregistered tools: {sorted(missing_operation_tools)}")

    resource_controls: set[str] = set()
    for path in (tools_root / "Core").glob("OniResourceRegistry*.cs"):
        resource_controls.update(
            re.findall(r'"([a-z][a-z0-9_]*_control)"', path.read_text(encoding="utf-8"))
        )
    invalid_resources = resource_controls - registered - ALLOWED_NON_TOOL_CONTROL_REFERENCES
    if invalid_resources:
        fail(f"resource registry references unknown control tools: {sorted(invalid_resources)}")

    assert_no_removed_pointer_contracts(root, sources[navigation_path])
    navigation_params = extract_block(
        sources[navigation_path],
        "private static Dictionary<string, McpToolParameter> Params()",
    )
    camera_params = extract_block(
        sources[tools_root / "Impl" / "Navigation" / "CameraOverlayAndScreenshotHelpers.cs"],
        "private static Dictionary<string, McpToolParameter> CameraControlParams()",
    )
    navigation_keys = set(re.findall(r'\["([^"]+)"\]\s*=', navigation_params))
    camera_keys = set(re.findall(r'\["([^"]+)"\]\s*=', camera_params))
    if navigation_keys != camera_keys | {"domain"}:
        fail(
            "navigation_control parameters must equal CameraTools parameters plus domain: "
            f"extra={sorted(navigation_keys - camera_keys - {'domain'})}, "
            f"missing={sorted(camera_keys - navigation_keys)}"
        )
    config = (root / ".codex" / "config.toml").read_text(encoding="utf-8")
    sections = re.findall(
        r"(?ms)^\[mcp_servers\.oni\.tools\.([^]]+)\]\s*\n(.*?)(?=^\[|\Z)", config
    )
    config_names = [name for name, _ in sections]
    duplicate_config = sorted(name for name, count in Counter(config_names).items() if count > 1)
    if duplicate_config:
        fail(f"duplicate .codex ONI tool sections: {duplicate_config}")
    expected_visible = EXPECTED_REGISTERED - {"coordinate_control"}
    if set(config_names) != expected_visible:
        fail(
            ".codex ONI tools must equal the 10 visible canonical tools: "
            f"missing={sorted(expected_visible - set(config_names))}, "
            f"unexpected={sorted(set(config_names) - expected_visible)}"
        )
    for name, body in sections:
        if re.search(r'^approval_mode\s*=\s*"approve"\s*$', body, re.MULTILINE) is None:
            fail(f".codex ONI tool {name} must set approval_mode=approve")
    print(
        "OK: default-public=6, registered=11, visible=10, aliases="
        f"{len(alias_owner)}, operation-files={len(operation_files)}, "
        f"resource-controls={len(resource_controls)}"
    )
    print("Default public: " + ", ".join(sorted(default_public)))
    print("Registered: " + ", ".join(sorted(registered)))


if __name__ == "__main__":
    main()
