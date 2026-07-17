#!/usr/bin/env python3
"""Verify source-level material alias safeguards for build plan parsing."""

from __future__ import annotations

import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[4]
ALIASES = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanTextAliases.cs"
PARSER = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanTextParser.cs"


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def require_alias(source: str, key: str, value: str) -> None:
    pattern = re.compile(
        r'\[\s*"' + re.escape(key) + r'"\s*\]\s*=\s*"'
        + re.escape(value)
        + r'"\s*,',
        re.IGNORECASE,
    )
    if not pattern.search(source):
        fail(f'missing material alias "{key}" -> "{value}"')


def main() -> int:
    alias_source = ALIASES.read_text(encoding="utf-8")
    parser_source = PARSER.read_text(encoding="utf-8")

    require_alias(alias_source, "sandstone", "SandStone")
    require_alias(alias_source, "siltstone", "SiltStone")
    require_alias(alias_source, "砂岩", "SandStone")
    require_alias(alias_source, "粉砂岩", "SiltStone")

    alias_score = re.search(r"return\s+980\s*;", parser_source)
    if not alias_score:
        fail("material alias score must stay high confidence at 980")

    high_confidence_order = parser_source.find(
        ".OrderByDescending(item => item.Score >= 900)"
    )
    valid_material_order = parser_source.find(
        ".ThenByDescending(item => item.ValidForBuilding)"
    )
    score_order = parser_source.find(".ThenByDescending(item => item.Score)")

    if min(high_confidence_order, valid_material_order, score_order) < 0:
        fail("material candidate ordering clauses are incomplete")
    if not high_confidence_order < valid_material_order < score_order:
        fail("high-confidence aliases must sort before material availability and score")

    print("OK: build plan material aliases preserve SandStone/SiltStone precision")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
