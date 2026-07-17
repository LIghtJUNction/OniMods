#!/usr/bin/env python3
"""Verify the authoritative bidirectional ONI map-glyph lookup contract."""

from __future__ import annotations

from pathlib import Path

from oni_mcp_verify_parsing import fail


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        fail(f"missing {label}: {needle}")


def section(text: str, start: str, end: str) -> str:
    if start not in text or end not in text:
        fail(f"cannot inspect glyph filter section: {start}")
    return text.split(start, 1)[1].split(end, 1)[0]


def verify_glyph_lookup_contract(
    root: Path, sources: dict[Path, str] | None = None
) -> None:
    tools = root / "mods" / "oni_mcp" / "Tools"
    lookup_path = tools / "WorldEditorGlyphLookup.cs"
    symbols_path = tools / "WorldEditorSymbols.cs"
    lifecycle_path = root / "mods" / "oni_mcp" / "AutoDisinfectPolicy.cs"
    world_editor_path = tools / "WorldEditorTools.cs"
    query_path = tools / "WorldEditorQueryTools.cs"
    search_path = tools / "SearchControlTools.cs"
    for path in (lookup_path, symbols_path, lifecycle_path, world_editor_path, query_path, search_path):
        if not path.is_file():
            fail(f"required glyph source file not found: {path.relative_to(root)}")

    if sources is None:
        selected = {
            path: path.read_text(encoding="utf-8")
            for path in (lookup_path, symbols_path, lifecycle_path, world_editor_path, query_path, search_path)
        }
    else:
        selected = dict(sources)
        if lifecycle_path not in selected:
            selected[lifecycle_path] = lifecycle_path.read_text(encoding="utf-8")
    lookup = selected[lookup_path]
    symbols = selected[symbols_path]
    lifecycle = selected[lifecycle_path]
    world_editor = selected[world_editor_path]
    query_tools = selected[query_path]
    search = selected[search_path]

    for parameter in ("queries", "direction", "matchMode", "view", "perQueryLimit"):
        require(world_editor, f'["{parameter}"] = new McpToolParameter', f"world_editor {parameter} schema")
        require(search, f'["{parameter}"] = new McpToolParameter', f"search_control {parameter} schema")
    for value in ("auto", "code_to_meaning", "meaning_to_code", "exact", "contains"):
        require(lookup, f'"{value}"', f"glyph lookup mode {value}")

    require(query_tools, "return SearchGlyphs(args);", "world_editor symbols shared lookup")
    require(search, 'case "glyphs":', "search_control glyphs route")
    require(search, 'case "symbols":', "search_control symbols alias")
    require(search, 'case "codes":', "search_control codes alias")
    require(search, "WorldEditorTools.SearchGlyphs(args)", "shared glyph lookup dispatch")
    require(search, 'if (domain == "glyphs")\n                        return searchResult;', "glyph search bypasses coordinate action wrapper")
    require(lookup, "authoritative_glyph_mapping", "glyph-specific result contract")
    require(lookup, "Do not guess", "glyph no-guess next action")

    require(lookup, "GeneratedGlyphEntries", "generated building/element/entity glyph rows")
    require(lookup, "raw.Count == 0", "explicit empty glyph batch rejection")
    require(lookup, "raw.Count > 100", "glyph query batch cap")
    require(lookup, "token.Type != JTokenType.String", "glyph batch string-only validation")
    require(lookup, "string.IsNullOrWhiteSpace(token.ToString())", "mixed blank glyph batch rejection")
    require(lookup, 'error = "queries must contain only non-blank strings";', "strict glyph batch item error")
    require(lookup, "legacySingle ? 200 : 20", "legacy and batch limit defaults")
    require(lookup, "legacySingle ? 1000 : 100", "legacy and batch limit caps")
    require(lookup, 'result["query"]', "single-query compatibility query")
    require(lookup, 'result["count"]', "single-query compatibility count")
    require(lookup, 'result["symbols"]', "single-query compatibility symbols")
    for field in ("input", "resolvedDirection", "exact", "count", "matches"):
        require(lookup, f'["{field}"]', f"per-query glyph result {field}")

    for glyphs, label in (
        ("*─│┌┐└┘┬┴├┤┼←→↑↓●", "connection glyphs"),
        ("零寒冰和暖炎灼熔", "temperature glyphs"),
        ("■液不易可难", "oxygen glyphs"),
        ("晒明普弱暗", "light glyphs"),
        ("美好平差丑", "decor glyphs"),
        ("微菌疫", "disease glyphs"),
        ("低辐危", "radiation glyphs"),
        ("枯收植", "crop glyphs"),
    ):
        require(lookup, f'"{glyphs}"', label)
    require(lookup, '"Special", "empty", "空/无视图", "."', "empty glyph row")
    require(lookup, '"Special", "unknown", "未知", "?"', "unknown glyph row")
    require(lookup, '["view"]', "context-sensitive glyph view")
    require(lookup, "TryNormalizeGlyphView", "validated glyph view aliases")
    require(lookup, 'return CallToolResult.Error("unknown glyph view: " + requestedView);', "unknown view rejection")
    for alias in (
        "gas", "liquid_pipe", "automation", "room", "material",
        "heat_flow", "heatflow", "priorities", "priority",
        "thermal_conductivity", "sound", "suit", "rad", "tile", "tiles",
    ):
        require(lookup, f'case "{alias}":', f"glyph view alias {alias}")
    require(
        lookup,
        'case "all": normalized = string.Empty; return true;',
        "full-catalog glyph view alias parity",
    )
    require(lookup, "FilterOverlayGlyphRows", "overlay-only glyph filtering")
    require(lookup, "FilterRoomGlyphRows", "room-only glyph filtering")
    require(lookup, "FilterInfrastructureGlyphRows", "infrastructure glyph filtering")
    require(lookup, "FilterDefaultGlyphRows", "default/material glyph filtering")
    require(lookup, "IsGeneratedGlyphKind(kind)", "contextual filters preserve generated rows")
    for kind in ("Building", "Element", "Entity"):
        require(lookup, f'kind == "{kind}"', f"generic glyph kind {kind}")
    require(lookup, 'kind == "Overlay"\n                    && GlyphText(row, "view").Equals(view', "overlay adds only matching contextual rows")
    require(lookup, 'return kind == "Special" || IsGeneratedGlyphKind(kind) || kind == "Room";', "rooms preserve generic room anchors")
    require(lookup, 'return kind == "Special" || IsGeneratedGlyphKind(kind) || kind == "Connection";', "infrastructure preserves every generic anchor")
    require(lookup, 'return kind == "Special" || IsGeneratedGlyphKind(kind);', "default and materials preserve only generic rows")
    if "InfrastructureAnchorViews" in lookup:
        fail("infrastructure glyph filtering must not exclude generated rows by ID heuristics")
    overlay_filter = section(
        lookup, "private static List<JObject> FilterOverlayGlyphRows",
        "private static List<JObject> FilterRoomGlyphRows")
    room_filter = section(
        lookup, "private static List<JObject> FilterRoomGlyphRows",
        "private static List<JObject> FilterInfrastructureGlyphRows")
    infrastructure_filter = section(
        lookup, "private static List<JObject> FilterInfrastructureGlyphRows",
        "private static List<JObject> FilterDefaultGlyphRows")
    default_filter = section(
        lookup, "private static List<JObject> FilterDefaultGlyphRows",
        "private static bool IsGeneratedGlyphKind")
    require(
        overlay_filter,
        "IsGeneratedGlyphKind(kind)",
        "overlay filter preserves generated anchor rows",
    )
    for forbidden in ('kind == "Connection"', 'kind == "Room"'):
        if forbidden in overlay_filter:
            fail(f"overlay glyph filter leaks unrelated rows: {forbidden}")
    if 'kind == "Overlay"' in room_filter or 'kind == "Connection"' in room_filter:
        fail("room glyph filter must contain only generated, Room, and Special rows")
    if 'kind == "Overlay"' in infrastructure_filter or 'kind == "Room"' in infrastructure_filter:
        fail("infrastructure glyph filter must contain only generated, Connection, and Special rows")
    for forbidden in ('kind == "Overlay"', 'kind == "Room"', 'kind == "Connection"'):
        if forbidden in default_filter:
            fail(f"default/material glyph filter leaks contextual rows: {forbidden}")
    require(lookup, "RuntimeRoomGlyphEntries()", "runtime room lookup rows")
    require(lookup, 'GlyphRow("Room"', "runtime room row kind")
    require(lookup, 'GetUniqueChar(entry.Id, entry.Name)', "room lookup uses runtime map glyph")
    require(symbols, "Db.Get()?.RoomTypes?.resources", "runtime room type enumeration")
    require(symbols, "RuntimeRoomGlyphEntries", "shared runtime room entries")
    require(symbols, "EnsureRuntimeRoomGlyphs();", "late room database refresh")
    require(symbols, "RuntimeRoomGlyphsLoaded = true;", "room refresh completion guard")
    static_glyph_init = section(
        symbols, "private static Dictionary<string, char> BuildResolvedGlyphById()",
        "private static void AddRuntimeRoomGlyphs")
    for forbidden in ("RuntimeRoomGlyphEntries", "AddRuntimeRoomGlyphs", "Db.Get"):
        if forbidden in static_glyph_init:
            fail(f"static glyph initialization must not touch runtime database state: {forbidden}")
    runtime_rooms = section(
        symbols, "private static IReadOnlyList<SymbolGlyphEntry> RuntimeRoomGlyphEntries()",
        "private static string GlyphGroupKey")
    require(runtime_rooms, "if (!RuntimeDatabaseReady)", "room database readiness guard")
    require(runtime_rooms, "return entries;", "pre-database empty room result")
    if runtime_rooms.index("RuntimeDatabaseReady") > runtime_rooms.index("Db.Get"):
        fail("runtime room readiness guard must execute before Db.Get")
    require(symbols, "internal static void MarkRuntimeDatabaseReady()", "database-ready lifecycle hook")
    require(lifecycle, '[HarmonyPatch(typeof(Db), "Initialize")]', "explicit Db.Initialize Harmony patch")
    require(lifecycle, "WorldEditorTools.MarkRuntimeDatabaseReady();", "Db.Initialize completion marks glyph database ready")
    require(lookup, '["dirs"]', "connection direction metadata")
    require(lookup, '["meaning"]', "authoritative glyph meaning")

    skill = (root / ".agents" / "skills" / "oni-gameplay" / "SKILL.md").read_text(encoding="utf-8")
    world_ref = (root / ".agents" / "skills" / "oni-gameplay" / "references" / "world-editor.md").read_text(encoding="utf-8")
    world_ref_zh = (root / ".agents" / "skills" / "oni-gameplay" / "references" / "world-editor.zh.md").read_text(encoding="utf-8")
    for text, label in ((skill, "skill"), (world_ref, "English world-editor reference"), (world_ref_zh, "Chinese world-editor reference")):
        require(text, "search_control domain=glyphs queries=[", f"{label} mandatory batch lookup")
        require(text, "direction=auto", f"{label} auto direction")
        require(text, "code_to_meaning", f"{label} forward lookup example")
        require(text, "meaning_to_code", f"{label} reverse lookup example")
    require(skill, "Do not guess", "skill glyph no-guess rule")
    require(skill, "commentary", "skill glyph lookup commentary rule")
    require(world_ref, "/active/symbols/glyphs.md", "correct English glyph path")
    require(world_ref_zh, "/active/symbols/glyphs.md", "correct Chinese glyph path")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    verify_glyph_lookup_contract(root)
    print("OK: authoritative bidirectional glyph lookup contract")


if __name__ == "__main__":
    main()
