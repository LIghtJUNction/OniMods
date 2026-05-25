using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class BuildPlanningTools
    {
        public static McpTool SearchBuildables()
        {
            return new McpTool
            {
                Name = "buildings_search_defs",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Description = "搜索可建造建筑定义，返回 prefabId、尺寸、材料类别、可用外观和是否解锁",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "建筑 ID 或名称关键词", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "建造菜单分类/类别关键词，如 oxygen、plumbing、rocketry；大小写不敏感", Required = false },
                    ["includeUnavailable"] = new McpToolParameter { Type = "boolean", Description = "是否包含当前未可用/未解锁的建筑定义，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 30，最大 100", Required = false }
                },
                Handler = args =>
                {
                    string query = args["query"]?.ToString();
                    string category = args["category"]?.ToString();
                    bool includeUnavailable = ToolUtil.GetBool(args, "includeUnavailable", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 30, 100));
                    var defs = Assets.BuildingDefs
                        .Where(def => def != null && (includeUnavailable || def.IsAvailable()))
                        .Where(def => string.IsNullOrWhiteSpace(category) || MatchesCategory(def, category))
                        .Where(def => string.IsNullOrWhiteSpace(query) || Matches(def, query))
                        .OrderBy(def => def.PrefabID)
                        .Take(limit)
                        .Select(BuildingDefToDictionary)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                        ["category"] = string.IsNullOrWhiteSpace(category) ? null : category,
                        ["includeUnavailable"] = includeUnavailable,
                        ["returned"] = defs.Count,
                        ["buildings"] = defs
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListBuildMaterials()
        {
            return new McpTool
            {
                Name = "buildings_materials",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "building_materials", "build_materials" },
                Tags = new List<string> { "buildings", "materials", "inventory", "available", "建造", "材料" },
                Description = "列出指定建筑当前可用建造材料，按库存量排序；用于避免使用 SandStone 等当前星球没有的材料。material=auto 会使用同一选择逻辑。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "建筑 prefabId，例如 Outhouse、Tile、Wire", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界；会包含关联世界库存", Required = false },
                    ["includeUnavailable"] = new McpToolParameter { Type = "boolean", Description = "是否同时返回已发现但当前库存为 0 的候选，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 50，最大 200", Required = false }
                },
                Handler = args =>
                {
                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        return CallToolResult.Error("prefabId is required");
                    var def = Assets.GetBuildingDef(prefabId);
                    if (def == null)
                        return CallToolResult.Error("Building def not found");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool includeUnavailable = ToolUtil.GetBool(args, "includeUnavailable", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 50, 200));
                    var materials = AvailableMaterials(def, worldId, includeUnavailable)
                        .Take(limit)
                        .Select(item => item.ToDictionary())
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["prefabId"] = def.PrefabID,
                        ["name"] = ToolUtil.CleanName(def.Name),
                        ["worldId"] = worldId,
                        ["materialCategories"] = def.MaterialCategory,
                        ["resolvedMaterialCategories"] = MaterialCategoryTags(def).Select(tag => tag.Name).ToList(),
                        ["defaultMaterials"] = DefaultBuildElements(def).Select(tag => tag.Name).ToList(),
                        ["autoMaterial"] = materials.Count > 0 ? materials[0]["tag"] : null,
                        ["returned"] = materials.Count,
                        ["materials"] = materials
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static CallToolResult PlanAtPointer(JObject args)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required");

            string prefabId = args["prefabId"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabId))
                return CallToolResult.Error("prefabId is required");

            var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, args["agentId"]?.ToString());
            if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                return CallToolResult.Error("Pointer is not aimed at a valid cell; call agent_pointer_aim_cell first");

            Grid.CellToXY(pointer.Cell, out int x, out int y);
            if (args["worldId"] == null && pointer.WorldId >= 0)
                args["worldId"] = pointer.WorldId;
            var result = TryPlanOne(prefabId, x, y, args);
            result["pointer"] = pointer.ToDictionary();
            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        internal static CallToolResult DragLineFromPointer(JObject args)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required");

            var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, args["agentId"]?.ToString());
            if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                return CallToolResult.Error("Pointer is not aimed at a valid cell; call agent_pointer_aim_cell first");

            int? requestedLength = ToolUtil.GetInt(args, "length");
            if (!requestedLength.HasValue || requestedLength.Value <= 0)
                return CallToolResult.Error("length must be a positive integer");
            int length = Math.Max(1, Math.Min(requestedLength.Value, 200));

            string direction = (args["direction"]?.ToString() ?? "").Trim().ToLowerInvariant();
            int dx;
            int dy;
            if (!TryDirection(direction, out dx, out dy))
                return CallToolResult.Error("direction must be right, left, up or down");

            string prefabId = string.IsNullOrWhiteSpace(args["prefabId"]?.ToString()) ? "Wire" : args["prefabId"].ToString();
            var def = Assets.GetBuildingDef(prefabId);
            if (def == null)
                return CallToolResult.Error("Building def not found");

            var dragPolicy = BuildDragPolicy(def, args);
            if (!dragPolicy.Allowed)
                return CallToolResult.Error(JsonConvert.SerializeObject(dragPolicy.ToDictionary(), McpJsonUtil.Settings));

            Grid.CellToXY(pointer.Cell, out int startX, out int startY);
            int endX = startX + dx * (length - 1);
            int endY = startY + dy * (length - 1);
            int worldId = pointer.WorldId >= 0 ? pointer.WorldId : ToolUtil.ResolveWorldId(args);
            int endCell = Grid.XYToCell(endX, endY);
            if (!Grid.IsValidCell(endCell))
                return CallToolResult.Error("Drag end cell is outside the grid");
            if (!ToolUtil.CellMatchesWorld(endCell, worldId))
                return CallToolResult.Error($"Drag end cell is not in worldId={worldId}");
            if (args["worldId"] == null && worldId >= 0)
                args["worldId"] = worldId;

            var results = new List<Dictionary<string, object>>();
            var errors = new List<Dictionary<string, object>>();
            var plannedSupportCells = new HashSet<int>();
            AgentPointerRegistry.BeginDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), worldId, pointer.Cell, prefabId);

            int planned = 0;
            int valid = 0;
            foreach (var cell in StraightLineCells(startX, startY, endX, endY))
            {
                AgentPointerRegistry.UpdateDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), Grid.XYToCell(cell.x, cell.y));
                var result = TryPlanOne(prefabId, cell.x, cell.y, args, plannedSupportCells);
                bool ok = result.ContainsKey("planned") && (bool)result["planned"];
                bool validPlacement = result.ContainsKey("valid") && (bool)result["valid"];
                if (ok || (IsDryRun(args) && validPlacement))
                {
                    valid++;
                    if (ok)
                        planned++;
                    RegisterSupportBlueprint(prefabId, cell.x, cell.y, plannedSupportCells);
                }
                else
                {
                    errors.Add(result);
                }
                results.Add(result);
            }

            var finalPointer = AgentPointerRegistry.EndDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), endCell);
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["prefabId"] = prefabId,
                ["dragPolicy"] = dragPolicy.ToDictionary(),
                ["dryRun"] = IsDryRun(args),
                ["drag"] = new Dictionary<string, object>
                {
                    ["from"] = new { x = startX, y = startY },
                    ["to"] = new { x = endX, y = endY },
                    ["direction"] = direction,
                    ["length"] = length,
                    ["mouseButton"] = "left",
                    ["gesture"] = "long_press_drag_line"
                },
                ["valid"] = valid,
                ["planned"] = planned,
                ["failed"] = errors.Count,
                ["errors"] = errors.Take(50).ToList(),
                ["pointer"] = finalPointer.ToDictionary(),
                ["results"] = results
            }, McpJsonUtil.Settings));
        }

        private static Dictionary<string, object> TryPlanOne(string prefabId, int x, int y, JObject args, HashSet<int> plannedSupportCells = null)
        {
            var def = Assets.GetBuildingDef(prefabId);
            if (def == null)
                return ErrorResult(prefabId, x, y, "Building def not found");

            int cell = Grid.XYToCell(x, y);
            if (!Grid.IsValidBuildingCell(cell) || !Grid.IsVisible(cell))
                return ErrorResult(prefabId, x, y, "Invalid or not visible cell");

            int worldId = ToolUtil.ResolveWorldId(args);
            if (!ToolUtil.CellMatchesWorld(cell, worldId))
                return ErrorResult(prefabId, x, y, $"Cell is not in worldId={worldId}");

            var orientation = ParseOrientation(args["orientation"]?.ToString());
            var materialResult = SelectElements(def, args["material"]?.ToString(), worldId);
            if (!materialResult.Valid)
                return ErrorResult(prefabId, x, y, materialResult.Error, materialResult.ToDictionary());

            var facadeResult = ResolveFacade(def, args["facade"]?.ToString() ?? args["facadeId"]?.ToString());
            if (!facadeResult.Valid)
                return ErrorResult(prefabId, x, y, facadeResult.Error);

            var supportResult = ValidateSupport(def, x, y, ToolUtil.GetBool(args, "allowUnsupported", false), plannedSupportCells);
            if (!supportResult.Valid)
                return ErrorResult(prefabId, x, y, supportResult.Error, supportResult.ToDictionary());

            var placement = BuildPlacementDetails(def, x, y, worldId);
            var footprintResult = ValidateFootprint(placement);
            if (!footprintResult.Valid)
                return ErrorResult(prefabId, x, y, footprintResult.Error, footprintResult.ToDictionary(placement));

            if (IsDryRun(args))
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["valid"] = true,
                    ["dryRun"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                    ["materialSelection"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId
                };
            }

            var pos = BuildPlacementPosition(cell, def);
            var go = def.TryPlace(null, pos, orientation, materialResult.Elements, facadeResult.TryPlaceId);
            if (go == null)
                return ErrorResult(prefabId, x, y, "Placement failed", materialResult.ToDictionary());

            SetPriority(go, ToolUtil.GetInt(args, "priority") ?? 5);
            RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
            var actualPlacement = ActualPlacementDetails(go, def);
            return new Dictionary<string, object>
            {
                ["planned"] = true,
                ["valid"] = true,
                ["prefabId"] = prefabId,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = worldId,
                ["placement"] = placement.ToDictionary(),
                ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                ["actualPlacement"] = actualPlacement,
                ["placementCheck"] = ComparePlacement(placement, actualPlacement),
                ["support"] = supportResult.ToDictionary(),
                ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                ["materialSelection"] = materialResult.ToDictionary(),
                ["facade"] = facadeResult.ResponseId,
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1
            };
        }

        private static IEnumerable<CellCoord> LineCells(int x1, int y1, int x2, int y2, HashSet<string> seen)
        {
            int dx = Math.Sign(x2 - x1);
            int dy = Math.Sign(y2 - y1);
            int x = x1;
            int y = y1;
            while (true)
            {
                string key = x + "," + y;
                if (seen.Add(key))
                    yield return new CellCoord(x, y);
                if (x == x2 && y == y2)
                    yield break;
                if (x != x2)
                    x += dx;
                else if (y != y2)
                    y += dy;
            }
        }

        private static IEnumerable<CellCoord> StraightLineCells(int x1, int y1, int x2, int y2)
        {
            var seen = new HashSet<string>();
            foreach (var cell in LineCells(x1, y1, x2, y2, seen))
                yield return cell;
        }

        private static bool TryDirection(string direction, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            switch ((direction ?? "").Trim().ToLowerInvariant())
            {
                case "right":
                case "east":
                case "e":
                    dx = 1;
                    return true;
                case "left":
                case "west":
                case "w":
                    dx = -1;
                    return true;
                case "up":
                case "north":
                case "n":
                    dy = 1;
                    return true;
                case "down":
                case "south":
                case "s":
                    dy = -1;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDryRun(JObject args)
        {
            return ToolUtil.GetBool(args, "dryRun", false) || ToolUtil.GetBool(args, "validateOnly", false);
        }

        private static Vector3 BuildPlacementPosition(int cell, BuildingDef def)
        {
            var pos = Grid.CellToPosCBC(cell, def.SceneLayer);
            pos.x += (Math.Max(1, def.WidthInCells) - 1) * 0.5f;
            pos.y += (Math.Max(1, def.HeightInCells) - 1) * 0.5f;
            return pos;
        }

        private static Dictionary<string, object> BuildDefPlacementToDictionary(BuildingDef def)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            return new Dictionary<string, object>
            {
                ["anchor"] = "lowerLeftCell",
                ["anchorDescription"] = "agent_pointer cell is treated as the lower-left footprint cell, not the visual center",
                ["width"] = width,
                ["height"] = height,
                ["footprintCells"] = width * height,
                ["singleCellDragSafe"] = width == 1 && height == 1,
                ["dragGuidance"] = width == 1 && height == 1
                    ? "May be placed with agent_pointer_hold_left for straight lines."
                    : "Use agent_pointer_left_click once per anchor cell; drag is rejected by default to avoid shifted furniture or machines."
            };
        }

        private static PlacementDetails BuildPlacementDetails(BuildingDef def, int x, int y, int worldId)
        {
            int cell = Grid.XYToCell(x, y);
            return new PlacementDetails
            {
                PrefabId = def.PrefabID,
                AnchorX = x,
                AnchorY = y,
                WorldId = worldId,
                Width = Math.Max(1, def.WidthInCells),
                Height = Math.Max(1, def.HeightInCells),
                PlacementPoint = BuildPlacementPosition(cell, def),
                Footprint = FootprintCells(def, x, y, worldId).ToList()
            };
        }

        private static IEnumerable<FootprintCell> FootprintCells(BuildingDef def, int x, int y, int worldId)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int fx = x + dx;
                    int fy = y + dy;
                    int cell = Grid.XYToCell(fx, fy);
                    yield return new FootprintCell
                    {
                        X = fx,
                        Y = fy,
                        Cell = cell,
                        WorldId = worldId,
                        Valid = Grid.IsValidCell(cell),
                        Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell),
                        InWorld = Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId)
                    };
                }
            }
        }

        private static FootprintValidation ValidateFootprint(PlacementDetails placement)
        {
            var invalid = placement.Footprint
                .Where(cell => !cell.Valid || !cell.Visible || !cell.InWorld)
                .Select(cell => cell.ToDictionary())
                .ToList();

            if (invalid.Count == 0)
                return FootprintValidation.Success();

            return FootprintValidation.Invalid("Invalid footprint: every occupied cell must be visible, valid, and inside the selected world", invalid);
        }

        private static Dictionary<string, object> ActualPlacementDetails(GameObject go, BuildingDef def)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            int cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
            int originX = x >= 0 ? x - width / 2 : -1;
            int originY = y >= 0 ? y - height / 2 : -1;
            int worldId = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1;

            return new Dictionary<string, object>
            {
                ["objectCell"] = cell,
                ["objectX"] = x,
                ["objectY"] = y,
                ["derivedAnchorX"] = originX,
                ["derivedAnchorY"] = originY,
                ["worldId"] = worldId,
                ["note"] = "objectX/objectY may be the visual center cell for multi-cell buildings; compare derivedAnchor to requested anchor"
            };
        }

        private static Dictionary<string, object> ComparePlacement(PlacementDetails expected, Dictionary<string, object> actual)
        {
            int actualX = actual.ContainsKey("derivedAnchorX") ? Convert.ToInt32(actual["derivedAnchorX"]) : -1;
            int actualY = actual.ContainsKey("derivedAnchorY") ? Convert.ToInt32(actual["derivedAnchorY"]) : -1;
            int actualWorld = actual.ContainsKey("worldId") ? Convert.ToInt32(actual["worldId"]) : -1;
            bool anchorMatches = actualX == expected.AnchorX && actualY == expected.AnchorY;
            bool worldMatches = actualWorld < 0 || expected.WorldId < 0 || actualWorld == expected.WorldId;
            return new Dictionary<string, object>
            {
                ["valid"] = anchorMatches && worldMatches,
                ["anchorMatches"] = anchorMatches,
                ["worldMatches"] = worldMatches,
                ["expectedAnchor"] = new { x = expected.AnchorX, y = expected.AnchorY },
                ["actualDerivedAnchor"] = new { x = actualX, y = actualY },
                ["expectedWorldId"] = expected.WorldId,
                ["actualWorldId"] = actualWorld,
                ["next"] = anchorMatches && worldMatches
                    ? "Verify with world_area_snapshot/world_text_map before placing the next footprint batch."
                    : "Cancel the misplaced blueprint before retrying from the expected anchor."
            };
        }

        private static BuildDragPolicyResult BuildDragPolicy(BuildingDef def, JObject args)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            bool singleCell = width == 1 && height == 1;
            bool allowFootprintDrag = ToolUtil.GetBool(args, "allowFootprintDrag", false);
            if (singleCell || allowFootprintDrag)
                return BuildDragPolicyResult.Allow(def.PrefabID, width, height, singleCell, allowFootprintDrag);
            return BuildDragPolicyResult.Reject(def.PrefabID, width, height);
        }

        private static void RegisterSupportBlueprint(string prefabId, int x, int y, HashSet<int> plannedSupportCells)
        {
            if (plannedSupportCells == null || !IsSupportPrefab(prefabId))
                return;

            int cell = Grid.XYToCell(x, y);
            if (Grid.IsValidCell(cell))
                plannedSupportCells.Add(cell);
        }

        private static bool IsSupportPrefab(string prefabId)
        {
            if (string.IsNullOrWhiteSpace(prefabId))
                return false;

            return EqualsIgnoreCase(prefabId, "Tile")
                || EqualsIgnoreCase(prefabId, "MeshTile")
                || EqualsIgnoreCase(prefabId, "GasPermeableMembrane")
                || EqualsIgnoreCase(prefabId, "AirflowTile")
                || EqualsIgnoreCase(prefabId, "BunkerTile")
                || EqualsIgnoreCase(prefabId, "GlassTile")
                || EqualsIgnoreCase(prefabId, "InsulationTile")
                || EqualsIgnoreCase(prefabId, "PlasticTile")
                || EqualsIgnoreCase(prefabId, "MetalTile")
                || EqualsIgnoreCase(prefabId, "CarpetTile");
        }

        private static SupportValidation ValidateSupport(BuildingDef def, int x, int y, bool allowUnsupported, HashSet<int> plannedSupportCells)
        {
            if (def == null)
                return SupportValidation.Success("unknown", null);

            string rule = def.BuildLocationRule.ToString();
            if (!EqualsIgnoreCase(rule, "OnFloor"))
                return SupportValidation.Success(rule, null);

            var missing = new List<Dictionary<string, object>>();
            foreach (var supportCell in FloorSupportCells(def, x, y))
            {
                bool supported = Grid.IsValidCell(supportCell.Cell)
                    && (Grid.Solid[supportCell.Cell]
                        || HasSupportBlueprint(supportCell.Cell)
                        || (plannedSupportCells != null && plannedSupportCells.Contains(supportCell.Cell)));
                if (!supported)
                    missing.Add(new Dictionary<string, object>
                    {
                        ["x"] = supportCell.X,
                        ["y"] = supportCell.Y
                    });
            }

            if (missing.Count == 0)
                return SupportValidation.Success(rule, null);

            string error = $"Unsupported OnFloor building: place floor/support tiles below {def.PrefabID} first, or set allowUnsupported=true";
            return allowUnsupported
                ? SupportValidation.Warning(rule, missing, error)
                : SupportValidation.Invalid(rule, missing, error);
        }

        private static IEnumerable<SupportCell> FloorSupportCells(BuildingDef def, int x, int y)
        {
            int width = Math.Max(1, def.WidthInCells);
            int supportY = y - 1;
            for (int dx = 0; dx < width; dx++)
            {
                int sx = x + dx;
                yield return new SupportCell(sx, supportY, Grid.XYToCell(sx, supportY));
            }
        }

        private static bool HasSupportBlueprint(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return false;

            for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
            {
                var go = Grid.Objects[cell, layer];
                if (go == null)
                    continue;

                var building = go.GetComponent<Building>();
                if (building != null && building.Def != null && IsSupportPrefab(building.Def.PrefabID))
                    return true;

                var prefabId = go.GetComponent<KPrefabID>()?.PrefabTag.Name;
                if (IsSupportPrefab(prefabId))
                    return true;
            }

            return false;
        }

        private struct CellCoord
        {
            public readonly int x;
            public readonly int y;

            public CellCoord(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        private struct SupportCell
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Cell;

            public SupportCell(int x, int y, int cell)
            {
                X = x;
                Y = y;
                Cell = cell;
            }
        }

        private static MaterialSelection SelectElements(BuildingDef def, string material, int worldId)
        {
            string requested = material?.Trim();
            bool auto = string.IsNullOrWhiteSpace(requested)
                || requested.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("default", StringComparison.OrdinalIgnoreCase);

            var available = AvailableMaterials(def, worldId, includeUnavailable: false).ToList();
            if (auto)
            {
                var selected = available.FirstOrDefault();
                if (selected != null)
                    return MaterialSelection.Success(new List<Tag> { selected.Tag }, "auto", requested, selected, available);

                var defaults = DefaultBuildElements(def);
                if (defaults.Count > 0 && IsFreeBuildContext())
                    return MaterialSelection.Success(defaults, "default_no_inventory_in_debug", requested, null, available);

                return MaterialSelection.Invalid(
                    "No available build material in current world inventory",
                    requested,
                    available,
                    AvailableMaterials(def, worldId, includeUnavailable: true).Take(20).ToList());
            }

            var match = AvailableMaterials(def, worldId, includeUnavailable: true)
                .FirstOrDefault(item => EqualsIgnoreCase(item.Tag.Name, requested)
                    || EqualsIgnoreCase(item.Name, requested)
                    || Contains(item.Tag.Name, requested)
                    || Contains(item.Name, requested));
            var candidates = AvailableMaterials(def, worldId, includeUnavailable: true).Take(20).ToList();
            if (match == null || !match.ValidForBuilding)
            {
                return MaterialSelection.Invalid(
                    $"Material '{requested}' is not valid for {def.PrefabID}",
                    requested,
                    available,
                    candidates);
            }

            if (match.AvailableKg <= 0f && !IsFreeBuildContext())
            {
                return MaterialSelection.Invalid(
                    $"Material '{match.Tag.Name}' is valid for {def.PrefabID}, but none is currently available",
                    requested,
                    available,
                    candidates);
            }

            return MaterialSelection.Success(new List<Tag> { match.Tag }, "explicit", requested, match, available);
        }

        private static List<BuildMaterialInfo> AvailableMaterials(BuildingDef def, int worldId, bool includeUnavailable)
        {
            var categories = MaterialCategoryTags(def).ToList();
            var candidates = CandidateMaterialTags(def, worldId)
                .Where(tag => tag.IsValid)
                .Distinct()
                .Select(tag =>
                {
                    var matches = categories.Where(category => MaterialMatchesCategory(tag, category)).ToList();
                    return new BuildMaterialInfo
                    {
                        Tag = tag,
                        Name = tag.ProperNameStripLink(),
                        AvailableKg = AvailableAmount(worldId, tag),
                        ValidForBuilding = categories.Count == 0 || matches.Count > 0,
                        Categories = matches
                    };
                })
                .Where(item => item.ValidForBuilding)
                .Where(item => includeUnavailable || item.AvailableKg > 0f || IsFreeBuildContext())
                .OrderByDescending(item => item.AvailableKg)
                .ThenBy(item => item.Tag.Name)
                .ToList();

            return candidates;
        }

        private static IEnumerable<Tag> CandidateMaterialTags(BuildingDef def, int worldId)
        {
            foreach (var tag in DefaultBuildElements(def))
                yield return tag;

            foreach (var category in MaterialCategoryTags(def))
            {
                if (DiscoveredResources.Instance != null)
                {
                    IEnumerable<Tag> discovered = null;
                    try
                    {
                        discovered = DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category);
                    }
                    catch
                    {
                        discovered = null;
                    }

                    if (discovered != null)
                    {
                        foreach (var tag in discovered)
                            yield return tag;
                    }
                }
            }

            foreach (var tag in InventoryMaterialTags(def, worldId))
                yield return tag;
        }

        private static IEnumerable<Tag> InventoryMaterialTags(BuildingDef def, int worldId)
        {
            var categories = MaterialCategoryTags(def).ToList();
            if (categories.Count == 0)
                yield break;

            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;

                int itemWorldId = PickupableWorldId(pickupable);
                if (worldId >= 0 && itemWorldId != worldId)
                    continue;

                var kpid = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                Tag prefabTag = kpid?.PrefabTag ?? Tag.Invalid;
                Tag elementTag = primary != null ? new Tag(primary.ElementID.ToString()) : Tag.Invalid;

                if (MaterialMatchesAnyCategory(prefabTag, categories))
                    yield return prefabTag;
                if (MaterialMatchesAnyCategory(elementTag, categories))
                    yield return elementTag;
                if (kpid != null && categories.Any(category => kpid.HasTag(category)))
                {
                    if (elementTag.IsValid)
                        yield return elementTag;
                    if (prefabTag.IsValid)
                        yield return prefabTag;
                }
            }
        }

        private static IEnumerable<Tag> MatchingMaterialCategories(BuildingDef def, Tag material)
        {
            var categories = MaterialCategoryTags(def).ToList();
            foreach (var category in categories)
            {
                if (MaterialMatchesCategory(material, category))
                    yield return category;
            }
        }

        private static IEnumerable<Tag> MaterialCategoryTags(BuildingDef def)
        {
            if (def.MaterialCategory == null)
                yield break;
            foreach (string categoryName in def.MaterialCategory)
            {
                foreach (var category in ParseMaterialCategoryExpression(categoryName))
                    yield return category;
            }
        }

        private static IEnumerable<Tag> ParseMaterialCategoryExpression(string categoryExpression)
        {
            if (string.IsNullOrWhiteSpace(categoryExpression))
                yield break;

            char[] separators = { '&', '|', ',', ';' };
            foreach (var part in categoryExpression.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var category = new Tag(part.Trim());
                if (category.IsValid)
                    yield return category;
            }
        }

        private static List<Tag> DefaultBuildElements(BuildingDef def)
        {
            var defaults = def.DefaultElements() ?? new List<Tag>();
            var categories = MaterialCategoryTags(def).ToList();
            if (categories.Count == 0)
                return defaults.Where(tag => tag.IsValid).Distinct().ToList();

            return defaults
                .Where(tag => MaterialMatchesAnyCategory(tag, categories))
                .Distinct()
                .ToList();
        }

        private static bool MaterialMatchesAnyCategory(Tag material, List<Tag> categories)
        {
            return material.IsValid && categories.Any(category => MaterialMatchesCategory(material, category));
        }

        private static bool MaterialMatchesCategory(Tag material, Tag category)
        {
            if (!material.IsValid || !category.IsValid)
                return false;
            if (material == category)
                return true;

            var element = ElementLoader.GetElement(material);
            if (element != null && (element.GetMaterialCategoryTag() == category || element.HasTag(category)))
                return true;

            var prefab = Assets.GetPrefab(material);
            var kpid = prefab != null ? prefab.GetComponent<KPrefabID>() : null;
            return kpid != null && kpid.HasTag(category);
        }

        private static int PickupableWorldId(Pickupable pickupable)
        {
            int cell = pickupable.cachedCell;
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell];
            return pickupable.GetMyWorldId();
        }

        private static float AvailableAmount(int worldId, Tag tag)
        {
            if (!tag.IsValid || ClusterManager.Instance == null)
                return 0f;
            var world = ClusterManager.Instance.GetWorld(worldId >= 0 ? worldId : ClusterManager.Instance.activeWorldId);
            if (world == null || world.worldInventory == null)
                return 0f;
            return ToolUtil.SafeFloat(world.worldInventory.GetTotalAmount(tag, includeRelatedWorlds: true));
        }

        private static bool IsFreeBuildContext()
        {
            return DebugHandler.InstantBuildMode || (Game.Instance != null && Game.Instance.SandboxModeActive);
        }

        private static FacadeSelection ResolveFacade(BuildingDef def, string facade)
        {
            if (string.IsNullOrWhiteSpace(facade))
                return FacadeSelection.Default();

            var facadeId = facade.Trim();
            if (facadeId.Equals("default", StringComparison.OrdinalIgnoreCase) || facadeId == "DEFAULT_FACADE")
                return FacadeSelection.Default();

            if (def.AvailableFacades == null || !def.AvailableFacades.Contains(facadeId))
                return FacadeSelection.Invalid($"Facade '{facadeId}' is not available for {def.PrefabID}");

            var permit = Db.Get().Permits.TryGet(facadeId);
            if (permit == null)
                return FacadeSelection.Invalid($"Facade '{facadeId}' has no permit resource");

            if (!permit.IsUnlocked())
                return FacadeSelection.Invalid($"Facade '{facadeId}' is locked");

            return FacadeSelection.Custom(facadeId);
        }

        private static void SetPriority(GameObject go, int priority)
        {
            var prioritizable = go.GetComponent<Prioritizable>();
            if (prioritizable == null)
                return;

            int clamped = Math.Max(1, Math.Min(priority, 9));
            prioritizable.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, clamped));
        }

        private static Orientation ParseOrientation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Orientation.Neutral;

            Orientation orientation;
            return Enum.TryParse(value, true, out orientation) ? orientation : Orientation.Neutral;
        }

        private static bool Matches(BuildingDef def, string query)
        {
            string q = query.Trim();
            return Contains(def.PrefabID, q)
                || Contains(def.Name, q)
                || Contains(def.Desc, q)
                || BuildingCategories(def).Any(category => Contains(category, q))
                || def.SearchTerms.Any(term => Contains(term, q));
        }

        private static bool MatchesCategory(BuildingDef def, string category)
        {
            string q = category.Trim();
            return BuildingCategories(def).Any(value => Contains(value, q));
        }

        private static Dictionary<string, object> BuildingDefToDictionary(BuildingDef def)
        {
            return new Dictionary<string, object>
            {
                ["prefabId"] = def.PrefabID,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["width"] = def.WidthInCells,
                ["height"] = def.HeightInCells,
                ["buildLocationRule"] = def.BuildLocationRule.ToString(),
                ["placement"] = BuildDefPlacementToDictionary(def),
                ["categories"] = BuildingCategories(def),
                ["materialCategories"] = def.MaterialCategory,
                ["resolvedMaterialCategories"] = MaterialCategoryTags(def).Select(tag => tag.Name).ToList(),
                ["defaultMaterials"] = DefaultBuildElements(def).Select(tag => tag.Name).ToList(),
                ["availableMaterials"] = AvailableMaterials(def, ClusterManager.Instance?.activeWorldId ?? -1, includeUnavailable: false).Take(20).Select(item => item.ToDictionary()).ToList(),
                ["autoMaterial"] = AvailableMaterials(def, ClusterManager.Instance?.activeWorldId ?? -1, includeUnavailable: false).FirstOrDefault()?.Tag.Name,
                ["facades"] = BuildingFacades(def),
                ["requiresPower"] = def.RequiresPowerInput,
                ["powerWatts"] = Math.Round(def.EnergyConsumptionWhenActive, 1),
                ["unlocked"] = Db.Get().Techs.IsTechItemComplete(def.PrefabID)
            };
        }

        private static List<string> BuildingCategories(BuildingDef def)
        {
            var categories = new List<string>();
            AddCategory(categories, ReadMemberString(def, "Category"));
            AddCategory(categories, ReadMemberString(def, "BuildMenuCategory"));
            AddCategory(categories, ReadMemberString(def, "MenuCategory"));
            AddCategory(categories, ReadMemberString(def, "PlanScreenCategory"));
            AddCategory(categories, ReadMemberString(def, "Subcategory"));
            AddCategory(categories, ReadMemberString(def, "BuildMenuSubcategory"));
            AddCategory(categories, ReadMemberString(def, "TechCategory"));

            if (def.MaterialCategory != null)
                foreach (var category in def.MaterialCategory)
                    AddCategory(categories, category);

            return categories.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
        }

        private static string ReadMemberString(BuildingDef def, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = def.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null)
                return MemberValueToString(property.GetValue(def, null));

            var field = type.GetField(name, flags);
            return field == null ? null : MemberValueToString(field.GetValue(def));
        }

        private static string MemberValueToString(object value)
        {
            if (value == null)
                return null;
            var tag = value as Tag?;
            if (tag.HasValue)
                return tag.Value.Name;
            return value.ToString();
        }

        private static void AddCategory(List<string> categories, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                categories.Add(value.Trim());
        }

        private static List<Dictionary<string, object>> BuildingFacades(BuildingDef def)
        {
            var facades = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["id"] = "DEFAULT_FACADE",
                    ["name"] = "Default",
                    ["unlocked"] = true,
                    ["default"] = true
                }
            };

            if (def.AvailableFacades == null)
                return facades;

            foreach (var facadeId in def.AvailableFacades)
            {
                var permit = Db.Get().Permits.TryGet(facadeId);
                bool unlocked = permit != null && permit.IsUnlocked();
                if (!unlocked)
                    continue;

                var facade = Db.GetBuildingFacades().TryGet(facadeId);
                facades.Add(new Dictionary<string, object>
                {
                    ["id"] = facadeId,
                    ["name"] = facade != null ? ToolUtil.CleanName(facade.Name) : facadeId,
                    ["unlocked"] = true,
                    ["default"] = false
                });
            }

            return facades;
        }

        private static Dictionary<string, object> ErrorResult(string prefabId, int x, int y, string error, Dictionary<string, object> details = null)
        {
            var result = new Dictionary<string, object>
            {
                ["planned"] = false,
                ["valid"] = false,
                ["prefabId"] = prefabId,
                ["x"] = x,
                ["y"] = y,
                ["error"] = error
            };
            if (details != null)
                result["details"] = details;
            return result;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string value, string query)
        {
            return string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class PlacementDetails
        {
            public string PrefabId;
            public int AnchorX;
            public int AnchorY;
            public int WorldId;
            public int Width;
            public int Height;
            public Vector3 PlacementPoint;
            public List<FootprintCell> Footprint = new List<FootprintCell>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["prefabId"] = PrefabId,
                    ["anchor"] = "lowerLeftCell",
                    ["anchorX"] = AnchorX,
                    ["anchorY"] = AnchorY,
                    ["worldId"] = WorldId,
                    ["width"] = Width,
                    ["height"] = Height,
                    ["footprintCells"] = Width * Height,
                    ["placementPoint"] = new
                    {
                        x = Math.Round(PlacementPoint.x, 3),
                        y = Math.Round(PlacementPoint.y, 3),
                        z = Math.Round(PlacementPoint.z, 3)
                    },
                    ["guidance"] = Width == 1 && Height == 1
                        ? "This is a single-cell footprint and can be line-dragged."
                        : "This is a multi-cell footprint; place each anchor with a separate left click and verify before continuing."
                };
            }
        }

        private sealed class FootprintCell
        {
            public int X;
            public int Y;
            public int Cell;
            public int WorldId;
            public bool Valid;
            public bool Visible;
            public bool InWorld;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["x"] = X,
                    ["y"] = Y,
                    ["cell"] = Cell,
                    ["worldId"] = WorldId,
                    ["valid"] = Valid,
                    ["visible"] = Visible,
                    ["inWorld"] = InWorld
                };
            }
        }

        private sealed class FootprintValidation
        {
            public bool Valid;
            public string Error;
            public List<Dictionary<string, object>> InvalidCells = new List<Dictionary<string, object>>();

            public static FootprintValidation Success()
            {
                return new FootprintValidation { Valid = true };
            }

            public static FootprintValidation Invalid(string error, List<Dictionary<string, object>> invalidCells)
            {
                return new FootprintValidation
                {
                    Valid = false,
                    Error = error,
                    InvalidCells = invalidCells ?? new List<Dictionary<string, object>>()
                };
            }

            public Dictionary<string, object> ToDictionary(PlacementDetails placement)
            {
                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["error"] = Error,
                    ["placement"] = placement.ToDictionary(),
                    ["invalidCells"] = InvalidCells
                };
            }
        }

        private sealed class BuildDragPolicyResult
        {
            public bool Allowed;
            public string PrefabId;
            public int Width;
            public int Height;
            public bool SingleCell;
            public bool AllowFootprintDrag;
            public string Reason;

            public static BuildDragPolicyResult Allow(string prefabId, int width, int height, bool singleCell, bool allowFootprintDrag)
            {
                return new BuildDragPolicyResult
                {
                    Allowed = true,
                    PrefabId = prefabId,
                    Width = width,
                    Height = height,
                    SingleCell = singleCell,
                    AllowFootprintDrag = allowFootprintDrag,
                    Reason = singleCell ? "single-cell footprint" : "allowFootprintDrag=true"
                };
            }

            public static BuildDragPolicyResult Reject(string prefabId, int width, int height)
            {
                return new BuildDragPolicyResult
                {
                    Allowed = false,
                    PrefabId = prefabId,
                    Width = width,
                    Height = height,
                    SingleCell = false,
                    AllowFootprintDrag = false,
                    Reason = "Multi-cell buildings must be placed one anchor click at a time to avoid shifted furniture or machines."
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["allowed"] = Allowed,
                    ["prefabId"] = PrefabId,
                    ["width"] = Width,
                    ["height"] = Height,
                    ["singleCell"] = SingleCell,
                    ["allowFootprintDrag"] = AllowFootprintDrag,
                    ["reason"] = Reason,
                    ["next"] = Allowed ? null : "Use agent_pointer_left_click for each lower-left anchor cell, or retry with allowFootprintDrag=true if this repeated footprint is intentional."
                };
            }
        }

        private struct FacadeSelection
        {
            public readonly bool Valid;
            public readonly string TryPlaceId;
            public readonly string ResponseId;
            public readonly string Error;

            private FacadeSelection(bool valid, string tryPlaceId, string responseId, string error)
            {
                Valid = valid;
                TryPlaceId = tryPlaceId;
                ResponseId = responseId;
                Error = error;
            }

            public static FacadeSelection Default()
            {
                return new FacadeSelection(true, null, "DEFAULT_FACADE", null);
            }

            public static FacadeSelection Custom(string facadeId)
            {
                return new FacadeSelection(true, facadeId, facadeId, null);
            }

            public static FacadeSelection Invalid(string error)
            {
                return new FacadeSelection(false, null, null, error);
            }
        }

        private sealed class BuildMaterialInfo
        {
            public Tag Tag;
            public string Name;
            public float AvailableKg;
            public bool ValidForBuilding;
            public List<Tag> Categories = new List<Tag>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["tag"] = Tag.Name,
                    ["name"] = Name,
                    ["availableKg"] = Math.Round(ToolUtil.SafeFloat(AvailableKg), 3),
                    ["validForBuilding"] = ValidForBuilding,
                    ["categories"] = Categories.Select(tag => tag.Name).OrderBy(name => name).ToList()
                };
            }
        }

        private sealed class MaterialSelection
        {
            public bool Valid;
            public string Mode;
            public string Requested;
            public List<Tag> Elements = new List<Tag>();
            public BuildMaterialInfo Selected;
            public List<BuildMaterialInfo> Available = new List<BuildMaterialInfo>();
            public List<BuildMaterialInfo> Candidates = new List<BuildMaterialInfo>();
            public string Error;

            public static MaterialSelection Success(List<Tag> elements, string mode, string requested, BuildMaterialInfo selected, List<BuildMaterialInfo> available)
            {
                return new MaterialSelection
                {
                    Valid = true,
                    Mode = mode,
                    Requested = string.IsNullOrWhiteSpace(requested) ? "auto" : requested,
                    Elements = elements ?? new List<Tag>(),
                    Selected = selected,
                    Available = available ?? new List<BuildMaterialInfo>()
                };
            }

            public static MaterialSelection Invalid(string error, string requested, List<BuildMaterialInfo> available, List<BuildMaterialInfo> candidates)
            {
                return new MaterialSelection
                {
                    Valid = false,
                    Mode = "invalid",
                    Requested = string.IsNullOrWhiteSpace(requested) ? "auto" : requested,
                    Error = error,
                    Available = available ?? new List<BuildMaterialInfo>(),
                    Candidates = candidates ?? new List<BuildMaterialInfo>()
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["mode"] = Mode,
                    ["requested"] = Requested,
                    ["selected"] = Selected != null ? Selected.ToDictionary() : null,
                    ["elements"] = Elements.Select(tag => tag.Name).ToList(),
                    ["availableMaterials"] = Available.Take(20).Select(item => item.ToDictionary()).ToList(),
                    ["candidateMaterials"] = Candidates.Take(20).Select(item => item.ToDictionary()).ToList(),
                    ["suggestion"] = Available.Count > 0 ? "Use material=auto or material=" + Available[0].Tag.Name : "No available material; inspect resources_inventory/buildings_materials",
                    ["error"] = Error
                };
            }
        }

        private sealed class SupportValidation
        {
            public bool Valid;
            public bool WarningOnly;
            public string Rule;
            public List<Dictionary<string, object>> MissingSupportCells = new List<Dictionary<string, object>>();
            public string Error;

            public static SupportValidation Success(string rule, List<Dictionary<string, object>> missing)
            {
                return new SupportValidation
                {
                    Valid = true,
                    WarningOnly = false,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>()
                };
            }

            public static SupportValidation Warning(string rule, List<Dictionary<string, object>> missing, string error)
            {
                return new SupportValidation
                {
                    Valid = true,
                    WarningOnly = true,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>(),
                    Error = error
                };
            }

            public static SupportValidation Invalid(string rule, List<Dictionary<string, object>> missing, string error)
            {
                return new SupportValidation
                {
                    Valid = false,
                    WarningOnly = false,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>(),
                    Error = error
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["warningOnly"] = WarningOnly,
                    ["buildLocationRule"] = Rule,
                    ["missingSupportCells"] = MissingSupportCells,
                    ["error"] = Error
                };
            }
        }
    }
}
