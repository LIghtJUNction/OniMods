#!/usr/bin/env python3
"""Verify high-confidence localized building aliases used by build planning."""

from __future__ import annotations

import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ALIASES = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanTextAliases.cs"
PARSER = ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanTextParser.cs"
GLYPHS = ROOT / "mods/OniMcp/Tools/WorldEditor/WorldEditorGeneratedGlyphData.cs"


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def building_alias_body(source: str) -> str:
    match = re.search(
        r"private\s+static\s+Dictionary<string,\s*string>\s+PlanBuildingAliases\(\)"
        r"\s*\{.*?return\s+new\s+Dictionary<string,\s*string>"
        r"\(StringComparer\.OrdinalIgnoreCase\)\s*\{(?P<body>.*?)\n\s*\};\s*\}",
        source,
        re.DOTALL,
    )
    if not match:
        fail("could not locate PlanBuildingAliases dictionary")
    return match.group("body")


def require_alias(body: str, key: str, value: str) -> None:
    pattern = re.compile(
        r'\[\s*"' + re.escape(key) + r'"\s*\]\s*=\s*"'
        + re.escape(value)
        + r'"\s*,',
        re.IGNORECASE,
    )
    if not pattern.search(body):
        fail(f'missing building alias "{key}" -> "{value}"')


def main() -> int:
    alias_source = ALIASES.read_text(encoding="utf-8")
    parser_source = PARSER.read_text(encoding="utf-8")
    glyph_source = GLYPHS.read_text(encoding="utf-8")
    body = building_alias_body(alias_source)

    require_alias(body, "洗手盆", "WashBasin")
    require_alias(body, "洗手池", "WashSink")

    if "Building|WashBasin|洗|洗手盆" not in glyph_source:
        fail("generated ONI glyph data no longer identifies WashBasin as 洗手盆")
    if "Building|WashSink|洗|洗手池" not in glyph_source:
        fail("generated ONI glyph data no longer identifies WashSink as 洗手池")

    alias_score = re.search(
        r"ResolveBuildingAlias\(term,\s*aliases,\s*out exactAlias\).*?"
        r"aliasScore\s*=\s*\(exactAlias\s*\?\s*950\s*:\s*SuffixBuildingAliasScore\)\s*\+\s*orderBoost.*?"
        r'bestKind\s*=\s*exactAlias\s*\?\s*"alias"\s*:\s*"alias_suffix"',
        parser_source,
        re.DOTALL,
    )
    if not alias_score:
        fail("building aliases no longer distinguish exact high-confidence matches from suffix discovery matches")

    print("OK: build planning keeps 洗手盆/WashBasin distinct from 洗手池/WashSink and exact aliases high-confidence")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
