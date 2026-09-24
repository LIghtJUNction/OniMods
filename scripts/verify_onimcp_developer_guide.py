#!/usr/bin/env python3
"""Verify developer-guide examples against the current OniMcp public tool contract."""

from __future__ import annotations

import json
import re
from pathlib import Path

from onimcp_verify_parsing import fail
from verify_onimcp_tool_surface import EXPECTED_DEFAULT_PUBLIC, EXPECTED_INTERNAL


PUBLIC_SURFACE_MARKER = "Default public surface: compact aggregate tools:"


def extract_public_surface(text: str) -> set[str]:
    marker_index = text.find(PUBLIC_SURFACE_MARKER)
    if marker_index < 0:
        fail(f"developer guide is missing marker: {PUBLIC_SURFACE_MARKER}")

    lines = text[marker_index:].splitlines()
    table_start = next(
        (index for index, line in enumerate(lines) if line.strip().startswith("| Tool |")),
        None,
    )
    if table_start is None:
        fail("developer guide default-public table was not found")

    tools: set[str] = set()
    for line in lines[table_start + 2 :]:
        if not line.lstrip().startswith("|"):
            break
        match = re.match(r"\|\s*`([a-z0-9_]+)`\s*\|", line)
        if match is not None:
            tools.add(match.group(1))
    return tools


def json_code_blocks(text: str) -> list[tuple[int, dict[str, object]]]:
    blocks: list[tuple[int, dict[str, object]]] = []
    for match in re.finditer(r"```json\s*\n(.*?)\n```", text, re.DOTALL):
        line = text.count("\n", 0, match.start()) + 1
        try:
            payload = json.loads(match.group(1))
        except json.JSONDecodeError as error:
            fail(f"docs/api-developer-guide.md:{line}: invalid JSON example: {error.msg}")
        if isinstance(payload, dict):
            blocks.append((line, payload))
    return blocks


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    guide_path = root / "docs" / "api-developer-guide.md"
    if not guide_path.is_file():
        fail("docs/api-developer-guide.md is missing")

    guide = guide_path.read_text(encoding="utf-8")
    public_surface = extract_public_surface(guide)
    if public_surface != EXPECTED_DEFAULT_PUBLIC:
        fail(
            "developer-guide default-public surface mismatch: "
            f"expected {sorted(EXPECTED_DEFAULT_PUBLIC)}, got {sorted(public_surface)}"
        )

    tool_call_count = 0
    for line, payload in json_code_blocks(guide):
        if payload.get("method") != "tools/call":
            continue
        tool_call_count += 1
        params = payload.get("params")
        if not isinstance(params, dict):
            fail(f"docs/api-developer-guide.md:{line}: tools/call params must be an object")
        name = params.get("name")
        if not isinstance(name, str) or not name:
            fail(f"docs/api-developer-guide.md:{line}: tools/call example is missing params.name")
        if name in EXPECTED_INTERNAL:
            fail(
                f"docs/api-developer-guide.md:{line}: internal operation {name!r} "
                "must not be shown as a direct tools/call target"
            )
        if name not in EXPECTED_DEFAULT_PUBLIC:
            fail(
                f"docs/api-developer-guide.md:{line}: tools/call target {name!r} "
                "is not in the default public surface"
            )

        arguments = params.get("arguments")
        if not isinstance(arguments, dict):
            fail(f"docs/api-developer-guide.md:{line}: tools/call arguments must be an object")
        task = arguments.get("task")
        if not isinstance(task, str) or not task.strip():
            fail(
                f"docs/api-developer-guide.md:{line}: legacy public tools/call example "
                "must include a non-empty arguments.task"
            )

    if tool_call_count == 0:
        fail("developer guide must retain at least one tools/call example")

    print(
        "OK: developer guide public surface and "
        f"{tool_call_count} tools/call examples match runtime admission"
    )


if __name__ == "__main__":
    main()
