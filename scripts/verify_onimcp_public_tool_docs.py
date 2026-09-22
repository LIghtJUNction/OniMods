#!/usr/bin/env python3
"""Verify user-facing OniMcp docs match the registered public tool surface."""

from __future__ import annotations

import ast
import re
from pathlib import Path

from onimcp_verify_parsing import fail
from verify_onimcp_tool_surface import EXPECTED_DEFAULT_PUBLIC, EXPECTED_INTERNAL


def extract_nested_tool_list(text: str, marker: str, source: str) -> set[str]:
    marker_index = text.find(marker)
    if marker_index < 0:
        fail(f"{source}: missing marker {marker!r}")

    tools: set[str] = set()
    for line in text[marker_index:].splitlines()[1:]:
        if not line.startswith("  - "):
            break
        match = re.match(r"\s*-\s+`([a-z0-9_]+)`", line)
        if match is not None:
            tools.add(match.group(1))
    return tools


def extract_quick_start_tools(text: str) -> set[str]:
    for line in text.splitlines():
        if line.startswith("- Legacy/default public tools:"):
            return set(re.findall(r"`([a-z0-9_]+)`", line))
    fail("docs/mcp-tools-reference.md: missing Legacy/default public tools line")


def extract_core_tool_table(text: str) -> set[str]:
    marker = "## 核心工具"
    marker_index = text.find(marker)
    if marker_index < 0:
        fail("docs/mcp-tools-reference.md: missing core-tools section")

    lines = text[marker_index:].splitlines()
    table_start = next(
        (index for index, line in enumerate(lines) if line.strip().startswith("| 工具 |")),
        None,
    )
    if table_start is None:
        fail("docs/mcp-tools-reference.md: core-tools table was not found")

    tools: set[str] = set()
    for line in lines[table_start + 2 :]:
        if not line.lstrip().startswith("|"):
            break
        match = re.match(r"\|\s*`([a-z0-9_]+)`\s*\|", line)
        if match is not None:
            tools.add(match.group(1))
    return tools


def extract_python_string_set(text: str, name: str, source: str) -> set[str]:
    tree = ast.parse(text, filename=source)
    for node in tree.body:
        if not isinstance(node, ast.Assign):
            continue
        if not any(isinstance(target, ast.Name) and target.id == name for target in node.targets):
            continue
        try:
            value = ast.literal_eval(node.value)
        except (TypeError, ValueError, SyntaxError) as error:
            fail(f"{source}: {name} is not a literal string set: {error}")
        if not isinstance(value, set) or not all(isinstance(item, str) for item in value):
            fail(f"{source}: {name} must be a literal string set")
        return value
    fail(f"{source}: missing {name} assignment")


def assert_exact_public(actual: set[str], source: str) -> None:
    if actual != EXPECTED_DEFAULT_PUBLIC:
        fail(
            f"{source}: documented public tools mismatch: "
            f"expected {sorted(EXPECTED_DEFAULT_PUBLIC)}, got {sorted(actual)}"
        )


def assert_no_direct_internal_guidance(text: str) -> None:
    for internal in EXPECTED_INTERNAL:
        if re.search(rf"\bUse\s+`{re.escape(internal)}`", text, re.IGNORECASE):
            fail(
                "docs/mcp-tools-reference.md: internal operation "
                f"{internal!r} must not be recommended as a direct MCP tool"
            )
        if re.search(rf"`{re.escape(internal)}\s+domain=", text):
            fail(
                "docs/mcp-tools-reference.md: internal operation "
                f"{internal!r} must not be shown as a direct domain call"
            )


def main() -> None:
    root = Path(__file__).resolve().parents[1]

    readme_zh_path = root / "mods" / "OniMcp" / "README.md"
    readme_en_path = root / "mods" / "OniMcp" / "README_EN.md"
    reference_path = root / "docs" / "mcp-tools-reference.md"
    runtime_smoke_path = (
        root
        / ".agents"
        / "skills"
        / "oni-mcp-autonomous-iteration"
        / "scripts"
        / "runtime_smoke.py"
    )

    readme_zh = readme_zh_path.read_text(encoding="utf-8")
    readme_en = readme_en_path.read_text(encoding="utf-8")
    reference = reference_path.read_text(encoding="utf-8")
    runtime_smoke = runtime_smoke_path.read_text(encoding="utf-8")

    assert_exact_public(
        extract_nested_tool_list(readme_zh, "- 常用公开工具:", str(readme_zh_path.relative_to(root))),
        str(readme_zh_path.relative_to(root)),
    )
    assert_exact_public(
        extract_nested_tool_list(readme_en, "- Default public aggregates:", str(readme_en_path.relative_to(root))),
        str(readme_en_path.relative_to(root)),
    )
    assert_exact_public(
        extract_quick_start_tools(reference),
        "docs/mcp-tools-reference.md quick start",
    )

    core_tools = extract_core_tool_table(reference)
    assert_exact_public(core_tools, "docs/mcp-tools-reference.md core-tools table")
    internal_core = core_tools & EXPECTED_INTERNAL
    if internal_core:
        fail(
            "docs/mcp-tools-reference.md: internal-only operations appear in core public table: "
            + ", ".join(sorted(internal_core))
        )

    assert_exact_public(
        extract_python_string_set(
            runtime_smoke,
            "DEFAULT_PUBLIC_TOOLS",
            str(runtime_smoke_path.relative_to(root)),
        ),
        str(runtime_smoke_path.relative_to(root)),
    )

    assert_no_direct_internal_guidance(reference)

    print(
        "OK: OniMcp READMEs, tool reference, and runtime smoke exactly "
        f"{len(EXPECTED_DEFAULT_PUBLIC)} registered public tools"
    )


if __name__ == "__main__":
    main()
