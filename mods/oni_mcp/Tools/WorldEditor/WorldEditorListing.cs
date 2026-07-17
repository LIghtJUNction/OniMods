using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static List<object> ListEntries(string path)
        {
            var entries = new List<object>();
            Action<string, string, string> add = (name, type, description) =>
            {
                string child = path == "/" ? "/" + name : path.TrimEnd('/') + "/" + name;
                entries.Add(new { name, type, path = child, description });
            };

            if (path == "/")
            {
                add("active/", "dir", "Currently active running colony in memory.");
                add("saves/", "dir", "All historical colony saves on disk.");
                return entries;
            }

            if (path == "/active/")
            {
                add("index.md", "file", "Low-token active world status and next-call guide.");
                add("manifest.oni", "file", "Active save metadata and game state.");
                add("colony/", "dir", "Colony-wide files.");
                add("map/", "dir", "World map code files.");
                add("infrastructure/", "dir", "Power, pipe, and room files.");
                add("buildings/", "dir", "Building state and desired build plan file.");
 add("orders/", "dir", "Order intent file.");
 add("resources/", "dir", "Inventory files.");
                add("dupes/", "dir", "Duplicant files.");
                add("symbols/", "dir", "Generated one-character glyph mapping files.");
                add("management/", "dir", "Editable Markdown tables for schedules, priorities, food, skills, and research.");
                add("diagnostics/", "dir", "Low-token stability and log diagnostic files.");
             add("screenshots/", "dir", "Viewport screenshot command guide and latest screenshot link.");
             add("blueprints/", "dir", "Blueprints Expanded files with editable text-map views.");
             add("ops/", "dir", "Generic editable operation files for all control tools.");
 return entries;
 }

            if (path.StartsWith("/active/", StringComparison.Ordinal))
            {
                string relative = path.Substring("/active/".Length);
                if (relative == "colony/")
                    add("status.oni", "file", "Colony snapshot.");
                else if (relative == "map/")
                {
                    add("index.html", "file", "Visual floor layout HTML page of the CURRENT visible camera screen.");
                    add("index.md", "file", "Token-efficient Markdown map of the CURRENT visible camera screen.");
                    add("viewport.md", "file", "Editable Markdown map of the current camera viewport; read with format=edit before changing cells.");
                    add("layers/", "dir", "Visual floor layout pages divided by height layers.");
                }
                else if (relative == "map/layers/")
                {
                    try
                    {
                        int worldId = ClusterManager.Instance?.activeWorldId ?? 0;
                        var world = ClusterManager.Instance?.GetWorld(worldId);
                        if (world != null)
                        {
                            int height = world.WorldSize.y;
                            int layerSize = 32;
                            for (int y = 0; y < height; y += layerSize)
                            {
                                int yMin = y;
                                int yMax = Math.Min(y + layerSize - 1, height - 1);
                                add($"layer_{yMin}_{yMax}.html", "file", $"Visual map layout HTML page for Y height {yMin} to {yMax}.");
                                add($"layer_{yMin}_{yMax}.md", "file", $"Token-efficient Markdown map for Y height {yMin} to {yMax}.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OniMcpLog.Warning($"[OniMcp] Failed to generate layers list: {ex.Message}");
                    }
                }
                else if (relative == "blueprints/")
                {
                    add("index.md", "file", "Blueprint index and text-map conversion guide.");
                    entries.AddRange(BlueprintEntries("/active/blueprints/"));
                }

                else if (relative == "infrastructure/")
                {
                    add("power.oni", "file", "Power circuit file. SEARCH/REPLACE can request wire edits.");
                    add("power.md", "file", "Power connection map using one-character connection glyphs.");
                    add("liquid_conduits.oni", "file", "Liquid pipe file. SEARCH/REPLACE can request pipe edits.");
                    add("liquid_conduits.md", "file", "Liquid pipe connection map using one-character connection glyphs.");
                    add("gas_conduits.oni", "file", "Gas pipe file. SEARCH/REPLACE can request pipe edits.");
                    add("gas_conduits.md", "file", "Gas pipe connection map using one-character connection glyphs.");
                    add("logic.oni", "file", "Automation signal wire file. SEARCH/REPLACE can request signal edits.");
                    add("logic.md", "file", "Automation signal connection map using one-character connection glyphs.");
                    add("solid_conveyor.oni", "file", "Conveyor rail file. SEARCH/REPLACE can request rail edits.");
                    add("solid_conveyor.md", "file", "Conveyor rail connection map using one-character connection glyphs.");
                    add("rooms.oni", "file", "Rooms list.");
                }
                else if (relative == "symbols/")
                {
                    add("index.md", "file", "Generated global glyph map index.");
                    add("glyphs.md", "file", "Generated global one-character glyph mapping table.");
                }
                else if (relative == "buildings/")
                {
                    add("index.md", "file", "Completed building parameter file index.");
                    add("instances/", "dir", "Stable per-building editable parameter files.");
                    add("index.oni", "file", "Built buildings.");
                    add("catalog.oni", "file", "Buildable catalog search.");
                    add("plans.oni", "file", "Desired building plan. SEARCH/REPLACE creates blueprints.");
                }
                else if (relative == "buildings/instances/")
                {
                    foreach (var go in LiveCompletedBuildings())
                        add(GetBuildingDetailFileName(go), "file", "Editable parameters for " + go.GetProperName() + ".");
                }
                else if (relative == "orders/")
                    add("orders.oni", "file", "Read-only legacy guide. Use /active/ops/orders.md for executable orders.");
                else if (relative == "resources/")
                {
                    add("inventory.oni", "file", "Resource inventory.");
                    add("food.oni", "file", "Food summary.");
                }
                else if (relative == "dupes/")
                {
                    add("index.oni", "file", "Duplicant status index.");
                    add("index.md", "file", "Duplicant file index. Each duplicant detail file can be edited.");
                    add("reachability.md", "file", "Compact duplicant movement range summary around current positions.");
                    foreach (var dupe in LiveDupes())
                        add(GetDupeDetailFileName(dupe), "file", "Duplicant detail file. Edit Name: to rename " + dupe.GetProperName() + ".");
                }
            else if (relative == "diagnostics/")
            {
                add("logs.md", "file", "Low-token Player.log error and crash stability summary.");
            }
            else if (relative == "screenshots/")
            {
                add("index.md", "file", "How to capture current viewport screenshots, including multiple overlay views.");
            }
            else if (relative == "management/")
 {
 add("index.md", "file", "Management control file index. Lists editable schedule, priority, food, skill, and research files.");
 add("schedule.md", "file", "Editable schedule table. SEARCH/REPLACE applies schedule block and assignment changes.");
 add("priorities.md", "file", "Editable duplicant priority table. SEARCH/REPLACE applies job priority changes.");
 add("food.md", "file", "Editable food permission table. SEARCH/REPLACE applies consumable permission changes.");
 add("skills.md", "file", "Editable duplicant skill tree table. SEARCH/REPLACE learns skills.");
                add("dupes.md", "file", "Editable duplicant rename command table.");
                add("research.md", "file", "Editable research queue/tree file. SEARCH/REPLACE sets or clears research.");
            }
            else if (relative == "ops/")
            {
                add("any.md", "file", "Generic operation file; each line must specify tool=<tool_name>.");
                add("game.md", "file", "Editable operations for game_control.");
                add("colony.md", "file", "Editable operations for colony_control.");
                add("read.md", "file", "Editable operations for read_control.");
                add("search.md", "file", "Editable operations for search_control.");
                add("build.md", "file", "Editable operations for building_control.");
                add("orders.md", "file", "Editable operations for orders_control.");
                add("dupes.md", "file", "Editable operations for dupes_control.");
                add("navigation.md", "file", "Editable operations for navigation_control.");
                add("coordinate.md", "file", "Editable operations for coordinate_control.");
                add("server.md", "file", "Editable operations for server_control.");
                add("tools.md", "file", "Read-only index of operation files and callable game tools.");
                add("facilities.md", "file", "Editable facility, side-screen, door and printing-pod operations.");
                add("storage.md", "file", "Editable storage, filter, dispenser and receptacle operations.");
                add("power.md", "file", "Editable power grid, generator, battery and wire operations.");
                add("automation.md", "file", "Editable automation, logic and sensor operations.");
                add("farming.md", "file", "Editable plant, farm and seed operations.");
                add("ranching.md", "file", "Editable critter, ranching and incubator operations.");
                add("rockets.md", "file", "Editable rocket, space and starmap operations.");
                add("resources.md", "file", "Editable resource, inventory, food and diet operations.");
                add("ui.md", "file", "Editable UI, camera, menu and notification operations.");
                add("medical.md", "file", "Editable medical and doctor operations.");
                add("rooms.md", "file", "Editable room query and room-control operations.");
                add("sandbox.md", "file", "Editable sandbox/debug operations when underlying tools allow them.");
            }
            return entries;
 }

            if (path == "/saves/")
            {
                add("latest/", "dir", "Fixed alias for the overall latest save across all colonies.");
                try
                {
                    var files = SaveLoader.GetAllFiles(sort: true, type: SaveLoader.SaveType.both);
                    if (files != null)
                    {
                        var addedColonies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var file in files)
                        {
                            string colony = GetColonyName(file.path);
                            if (!string.IsNullOrEmpty(colony) && !addedColonies.Contains(colony))
                            {
                                addedColonies.Add(colony);
                                add(colony + "/", "dir", $"Colony folder for: {colony}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OniMcpLog.Warning($"[OniMcp] Failed to read save files in world_editor /saves/: {ex.Message}");
                }
                return entries;
            }

            if (path.StartsWith("/saves/", StringComparison.Ordinal))
            {
                string rest = path.Substring("/saves/".Length);
                if (rest == "latest" || rest == "latest/")
                {
                    add("manifest.oni", "file", "Save metadata and game state.");
                    return entries;
                }

                int firstSlash = rest.IndexOf('/');
                if (firstSlash > 0)
                {
                    string colonyName = rest.Substring(0, firstSlash);
                    string subRest = rest.Substring(firstSlash + 1);

                    if (string.IsNullOrEmpty(subRest))
                    {
                        add("latest/", "dir", $"Fixed alias for the latest save of colony: {colonyName}.");
                        try
                        {
                            var files = SaveLoader.GetAllFiles(sort: true, type: SaveLoader.SaveType.both);
                            if (files != null)
                            {
                                foreach (var file in files)
                                {
                                    if (string.Equals(GetColonyName(file.path), colonyName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        string name = Path.GetFileNameWithoutExtension(file.path);
                                        string virtualSaveName = GetVirtualSaveName(name, colonyName);
                                        string activeSuffix = string.Equals(file.path, SaveLoader.GetActiveSaveFilePath(), StringComparison.OrdinalIgnoreCase) ? " (Active)" : "";
                                        add(virtualSaveName + "/", "dir", $"Save file: {Path.GetFileName(file.path)}{activeSuffix} (Utc: {file.timeStamp:o})");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            OniMcpLog.Warning($"[OniMcp] Failed to read save files in world_editor /saves/{colonyName}/: {ex.Message}");
                        }
                    }
                    else
                    {
                        add("manifest.oni", "file", "Save metadata and game state.");
                    }
                }
            }

            return entries;
        }

        private static bool IsDirectory(string path)
        {
            if (path == "/" || path == "/saves/" || path == "/active/")
                return true;

            if (path.StartsWith("/active/", StringComparison.Ordinal))
            {
                string relative = path.Substring("/active/".Length);
                return string.IsNullOrEmpty(relative)
                    || relative == "colony/"
                    || relative == "map/"
                    || relative == "map/layers/"
                    || relative == "infrastructure/"
                    || relative == "buildings/"
                    || relative == "orders/"
                    || relative == "resources/"
                    || relative == "dupes/"
                    || relative == "symbols/"
                || relative == "diagnostics/"
                  || relative == "screenshots/"
                  || relative == "blueprints/"
                  || relative == "management/"
                    || relative == "ops/";
            }

            if (path.StartsWith("/saves/", StringComparison.Ordinal))
            {
                string rest = path.Substring("/saves/".Length);
                if (rest == "latest" || rest == "latest/")
                    return true;

                int firstSlash = rest.IndexOf('/');
                if (firstSlash < 0)
                    return true;

                string subRest = rest.Substring(firstSlash + 1);
                if (string.IsNullOrEmpty(subRest))
                    return true;

                int secondSlash = subRest.IndexOf('/');
                if (secondSlash < 0)
                    return true;

                string relativeToSave = subRest.Substring(secondSlash + 1);
                if (string.IsNullOrEmpty(relativeToSave))
                    return true;
            }

            return false;
        }

        private static string SaveRelativePath(string path)
        {
            if (path.StartsWith("/active/", StringComparison.Ordinal))
            {
                return path.Substring("/active/".Length);
            }

            if (!path.StartsWith("/saves/", StringComparison.Ordinal))
                return path.TrimStart('/');

            string rest = path.Substring("/saves/".Length);
            if (rest == "latest" || rest == "latest/")
                return string.Empty;
            if (rest.StartsWith("latest/", StringComparison.Ordinal))
                return rest.Substring("latest/".Length);

            int firstSlash = rest.IndexOf('/');
            if (firstSlash < 0)
                return string.Empty;

            string subRest = rest.Substring(firstSlash + 1);
            if (subRest == "latest" || subRest == "latest/")
                return string.Empty;
            if (subRest.StartsWith("latest/", StringComparison.Ordinal))
                return subRest.Substring("latest/".Length);

            int secondSlash = subRest.IndexOf('/');
            if (secondSlash < 0)
                return string.Empty;

            return subRest.Substring(secondSlash + 1);
        }

    }
}
