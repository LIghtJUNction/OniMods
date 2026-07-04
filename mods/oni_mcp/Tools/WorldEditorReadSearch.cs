using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult Read(JObject args)
        {
            string path = NormalizePath(Text(args, "path"), _cwd);
            if (IsDirectory(path))
                return Ls(new JObject { ["path"] = path });

            if (path.StartsWith("/active/", StringComparison.Ordinal))
            {
                string relative = SaveRelativePath(path);
                if (relative == "index.md")
                    return CallToolResult.Text(ReadActiveIndexMarkdown(args));
                if (relative == "manifest.oni")
                    return GameControlEntryTools.ControlGame().Handler(Child(args, "state", "status"));
                if (relative == "colony/status.oni")
                    return ColonyControlEntryTools.ControlColony().Handler(Child(args, "snapshot", "get", ("profile", "minimal")));
                if (relative == "map/viewport.html" || relative == "map/viewport.md" || relative == "map/index.html" || relative == "map/index.md")
                    return CallToolResult.Text(ReadMapFileWithArgs(args, path));
                if (TryParseZoomPath(relative, out int zoomX1, out int zoomY1, out int zoomX2, out int zoomY2))
                {
                    var views = ResolveZoomViews(ParseZoomViews(args)).ToList();
                    if (views.Count == 0)
                        views = ResolveZoomViews(DefaultZoomViews()).ToList();
                    string syncNote = SyncZoomCameraAndView(args, zoomX1, zoomY1, zoomX2, zoomY2, views);
                    return CallToolResult.Text(ReadZoomMarkdown(
                        zoomX1,
                        zoomY1,
                        zoomX2,
                        zoomY2,
                        views.Select(view => view.Name),
                        syncNote,
                        ShouldCompactMap(args)));
                }
                if (TryParseCellSnapshotPath(relative, out int cellX, out int cellY))
                    return CallToolResult.Text(ReadCellSnapshotMarkdown(args, cellX, cellY));
                if (relative.StartsWith("map/layers/", StringComparison.Ordinal)
                    && (relative.EndsWith(".html", StringComparison.Ordinal) || relative.EndsWith(".md", StringComparison.Ordinal)))
                    return CallToolResult.Text(ReadFileDirectly(path));
                if (relative == "symbols/index.md" || relative == "symbols/glyphs.md")
                    return CallToolResult.Text(ReadSymbolMarkdown(path, Text(args, "query", "target", "search")));
                if (IsBlueprintVirtualFile(relative))
                    return ReadBlueprintVirtualFile(relative);
                if (IsInfrastructureMapMarkdown(relative))
                    return CallToolResult.Text(ReadInfrastructureMapMarkdown(args, path, relative));
                if (IsManagementMarkdown(relative))
                    return ReadManagementMarkdown(args, path, relative);
                if (IsOperationMarkdown(relative))
                    return ReadOperationMarkdown(path, relative);

                if (relative == "infrastructure/power.oni")
                    return ReadTools.ControlRead().Handler(Child(args, "infrastructure", "power_summary"));
                if (relative == "infrastructure/power_ports.oni")
                    return CallToolResult.Error("power_ports.oni is hidden from world_editor because broad port scans are crash-prone. Use /active/infrastructure/power.md or /active/map/cell_X_Y.md for low-token anchors.");
                if (relative == "infrastructure/rooms.oni")
                    return ReadTools.ControlRead().Handler(Child(args, "infrastructure", "rooms"));
                if (relative == "infrastructure/liquid_conduits.oni")
                    return ReadEditableTemplate(path, "Add or change liquid pipe connections by replacing text in this file.");
                if (relative == "infrastructure/gas_conduits.oni")
                    return ReadEditableTemplate(path, "Add or change gas pipe connections by replacing text in this file.");
                if (relative == "infrastructure/logic.oni")
                    return ReadEditableTemplate(path, "Add or change automation signal wire connections by replacing text in this file.");
                if (relative == "infrastructure/solid_conveyor.oni")
                    return ReadEditableTemplate(path, "Add or change conveyor rail connections by replacing text in this file.");
                if (relative == "buildings/index.oni")
                    return ReadTools.ControlRead().Handler(Child(args, "buildings", "list"));
                if (relative == "buildings/catalog.oni")
                    return Search(args, "buildings");
                if (relative == "buildings/plans.oni")
                    return ReadEditableTemplate(path, "Add or change desired buildings by replacing text in this file.");
                if (relative == "orders/orders.oni")
                    return ReadEditableTemplate(path, "Add dig/sweep/mop/deconstruct/cancel orders by replacing text in this file.");
                if (relative == "resources/inventory.oni")
                    return ReadTools.ControlRead().Handler(Child(args, "resources", "inventory"));
                if (relative == "resources/food.oni")
                    return ReadTools.ControlRead().Handler(Child(args, "resources", "food"));
                if (relative == "dupes/index.md")
                    return CallToolResult.Text(ReadDupeIndexMarkdown());
                if (relative == "dupes/reachability.md")
                    return CallToolResult.Text(ReadDupeReachabilityMarkdown(args));
                if (IsDupeDetailMarkdown(relative))
                    return CallToolResult.Text(ReadDupeDetailMarkdown(relative));
                if (relative == "dupes/index.oni")
                    return DupesControlEntryTools.ControlDupes().Handler(Child(args, "info", "status"));
                if (relative == "diagnostics/logs.md")
                    return CallToolResult.Text(ReadLogDiagnosticsMarkdown(args));

                return CallToolResult.Error("unknown /active/ virtual file: " + path);
            }

            if (path.StartsWith("/saves/", StringComparison.Ordinal))
            {
                string relative = SaveRelativePath(path);
                if (relative == "manifest.oni")
                {
                    string resolved;
                    if (!ResolveSaveFilePath(path, out resolved))
                        return CallToolResult.Error("Save file not found: " + path);
                    var files = SaveLoader.GetAllFiles(sort: true, type: SaveLoader.SaveType.both);
                    var entry = files.FirstOrDefault(f => string.Equals(f.path, resolved, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(entry.path))
                    {
                        var info = new JObject
                        {
                            ["colony"] = GetColonyName(entry.path),
                            ["name"] = Path.GetFileNameWithoutExtension(entry.path),
                            ["fileName"] = Path.GetFileName(entry.path),
                            ["path"] = entry.path,
                            ["timestampUtc"] = entry.timeStamp.ToString("o"),
                            ["cloud"] = SaveLoader.IsSaveCloud(entry.path),
                            ["local"] = SaveLoader.IsSaveLocal(entry.path),
                            ["autoSave"] = SaveLoader.IsSaveAuto(entry.path),
                            ["active"] = string.Equals(entry.path, SaveLoader.GetActiveSaveFilePath(), StringComparison.OrdinalIgnoreCase),
                            ["activeAlias"] = "/active/"
                        };
                        return JsonResult(info);
                    }
                    return CallToolResult.Error("Save file not found: " + path);
                }
                return CallToolResult.Error("Only manifest.oni is currently exposed for save snapshots.");
            }

            return CallToolResult.Error("unknown virtual file: " + path);
        }

        private static CallToolResult Search(JObject args, string forcedDomain = null)
        {
            var forwarded = CopyPayload(args);
            string domain = forcedDomain ?? Text(args, "domain");
            if (string.IsNullOrWhiteSpace(domain))
                domain = InferSearchDomain(NormalizePath(Text(args, "path"), _cwd));
            forwarded["domain"] = NormalizeSearchDomain(domain);
            if (forwarded["domain"]?.ToString() == "knowledge")
                return CallToolResult.Error("world_editor knowledge/database/guide search is disabled because in-game database queries are crash-prone. Use external docs or static files instead.");
            string query = Text(args, "query", "target", "search");
            if (!string.IsNullOrWhiteSpace(query))
                forwarded["query"] = query;
            return SearchControlTools.ControlSearch().Handler(forwarded);
        }

        private static CallToolResult ReadEditableTemplate(string path, string note)
        {
            string text =
                "# " + path + "\n" +
                "# " + note + "\n" +
                "# Edit file by sending exactly one SEARCH/REPLACE block. Non-empty SEARCH is validated against current virtual file snapshot.\n" +
                "<<<<<<< SEARCH\n" +
                "# observed or empty planning text\n" +
                "=======\n" +
                "# desired replacement text\n" +
                ">>>>>>> REPLACE\n";
            return CallToolResult.Text(text);
        }
    }
}
