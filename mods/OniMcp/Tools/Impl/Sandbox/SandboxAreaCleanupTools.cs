using System;
using System.Collections.Generic;
using System.Linq;
using Database;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using TemplateClasses;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SandboxTools
    {
        public static McpTool ClearFloorArea()
        {
            return new McpTool
            {
                Name = "sandbox_clear_floor_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_delete_items_area", "debug_clear_floor_area" },
                Tags = new List<string> { "sandbox", "items", "pickupables", "clear" },
                Description = "兼容入口：沙盒清地面。新调用请使用 game_control domain=sandbox kind=area action=clear_floor。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > MaxSandboxCells)
                        return CallToolResult.Error($"Refusing to clear {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var toDestroy = new List<Pickupable>();
                    foreach (var pickupable in Components.Pickupables.Items)
                    {
                        if (pickupable == null || pickupable.storage != null || IsLiveMinion(pickupable.gameObject))
                            continue;
                        int cell = Grid.PosToCell(pickupable);
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || !CellInRect(cell, rect))
                            continue;
                        toDestroy.Add(pickupable);
                    }

                    var removed = new List<Dictionary<string, object>>();
                    foreach (var pickupable in toDestroy)
                    {
                        var kpid = pickupable.GetComponent<KPrefabID>();
                        removed.Add(new Dictionary<string, object>
                        {
                            ["prefabId"] = kpid?.PrefabTag.ToString() ?? pickupable.name,
                            ["id"] = kpid?.InstanceID ?? -1,
                            ["cell"] = Grid.PosToCell(pickupable)
                        });
                        Util.KDestroyGameObject(pickupable.gameObject);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed.Count,
                        ["items"] = removed,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearCrittersArea()
        {
            return new McpTool
            {
                Name = "sandbox_clear_critters_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_critter_tool", "debug_clear_critters_area" },
                Tags = new List<string> { "sandbox", "critters", "creatures", "clear" },
                Description = "兼容入口：沙盒小动物工具。新调用请使用 game_control domain=sandbox kind=area action=clear_critters。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > MaxSandboxCells)
                        return CallToolResult.Error($"Refusing to scan {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var toDestroy = new List<GameObject>();
                    foreach (var health in Components.Health.Items)
                    {
                        var go = health?.gameObject;
                        var kpid = go?.GetComponent<KPrefabID>();
                        if (go == null || kpid == null || !kpid.HasTag(GameTags.Creature))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId) && CellInRect(cell, rect))
                            toDestroy.Add(go);
                    }

                    var removed = new List<Dictionary<string, object>>();
                    foreach (var go in toDestroy)
                    {
                        var kpid = go.GetComponent<KPrefabID>();
                        int cell = Grid.PosToCell(go);
                        removed.Add(new Dictionary<string, object>
                        {
                            ["id"] = kpid?.InstanceID ?? -1,
                            ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                            ["name"] = ToolUtil.CleanName(go.GetProperName()),
                            ["cell"] = cell
                        });
                        Util.KDestroyGameObject(go);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed.Count,
                        ["critters"] = removed,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }


        public static McpTool StressArea()
        {
            return new McpTool
            {
                Name = "sandbox_stress_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_dupe_stress", "debug_stress_area" },
                Tags = new List<string> { "sandbox", "duplicant", "stress" },
                Description = "兼容入口：沙盒压力工具。新调用请使用 game_control domain=sandbox kind=area action=stress。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；提供后可省略区域", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；提供后可省略区域", Required = false },
                    ["delta"] = new McpToolParameter { Type = "number", Description = "压力变化量，正数增加压力、负数降低压力；例如 -20", Required = true },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    float? delta = ToolUtil.GetFloat(args, "delta");
                    if (!delta.HasValue)
                        return CallToolResult.Error("delta is required");

                    var dupes = new List<MinionIdentity>();
                    var selected = ToolUtil.FindDupe(args);
                    if (selected != null)
                    {
                        dupes.Add(selected);
                    }
                    else
                    {
                        if (!HasRectInput(args))
                            return CallToolResult.Error("id/name or areaId/x1/y1/x2/y2 are required");
                        var rect = ToolUtil.GetRect(args);
                        int cells = RectCellCount(rect);
                        if (cells > MaxSandboxCells)
                            return CallToolResult.Error($"Refusing to scan {cells} cells; max={MaxSandboxCells}");

                        int worldId = ToolUtil.ResolveWorldId(args);
                        foreach (var dupe in Components.LiveMinionIdentities.Items)
                        {
                            if (dupe == null)
                                continue;
                            int cell = Grid.PosToCell(dupe.gameObject);
                            if (Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId) && CellInRect(cell, rect))
                                dupes.Add(dupe);
                        }
                    }

                    var affected = new List<Dictionary<string, object>>();
                    foreach (var dupe in dupes)
                    {
                        var amount = Db.Get().Amounts.Stress.Lookup(dupe.gameObject);
                        float before = amount.value;
                        amount.ApplyDelta(delta.Value);
                        affected.Add(new Dictionary<string, object>
                        {
                            ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                            ["name"] = dupe.GetProperName(),
                            ["before"] = before,
                            ["after"] = amount.value,
                            ["delta"] = delta.Value
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["affected"] = affected.Count,
                        ["dupes"] = affected
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static bool ValidateSandbox(Newtonsoft.Json.Linq.JObject args, out string error)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
            {
                error = "confirm=true is required";
                return false;
            }
            if (Game.Instance == null)
            {
                error = "Game not initialized";
                return false;
            }
            if (!Game.Instance.SandboxModeActive && !ToolUtil.GetBool(args, "force", false))
            {
                error = "Sandbox mode is not active; set force=true to override";
                return false;
            }
            error = null;
            return true;
        }

        private static Element ResolveElement(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            SimHashes hash;
            if (!Enum.TryParse(value.Trim(), true, out hash))
                return null;
            return ElementLoader.FindElementByHash(hash);
        }

        private static byte ResolveDiseaseIndex(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return byte.MaxValue;
            var disease = Db.Get().Diseases.TryGet(value.Trim());
            return disease == null ? byte.MaxValue : Db.Get().Diseases.GetIndex(disease.id);
        }

        private static bool HasRectInput(Newtonsoft.Json.Linq.JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static int RectCellCount(Dictionary<string, int> rect)
        {
            return (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
        }

        private static IEnumerable<int> RectCells(Dictionary<string, int> rect)
        {
            for (int y = rect["y1"]; y <= rect["y2"]; y++)
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                    yield return Grid.XYToCell(x, y);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect)
        {
            Grid.CellToXY(cell, out int x, out int y);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static List<int> CollectFloodCells(int startCell, SimHashes sourceElement, int worldId, int maxCells)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            seen.Add(startCell);
            queue.Enqueue(startCell);

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                    continue;
                if ((Grid.Element[cell]?.id ?? SimHashes.Vacuum) != sourceElement)
                    continue;

                result.Add(cell);
                if (result.Count > maxCells)
                    return null;

                Grid.CellToXY(cell, out int x, out int y);
                EnqueueNeighbor(x - 1, y, seen, queue);
                EnqueueNeighbor(x + 1, y, seen, queue);
                EnqueueNeighbor(x, y - 1, seen, queue);
                EnqueueNeighbor(x, y + 1, seen, queue);
            }

            return result;
        }

        private static void EnqueueNeighbor(int x, int y, HashSet<int> seen, Queue<int> queue)
        {
            if (x < 0 || x >= Grid.WidthInCells || y < 0 || y >= Grid.HeightInCells)
                return;
            int cell = Grid.XYToCell(x, y);
            if (seen.Add(cell))
                queue.Enqueue(cell);
        }

        private static bool IsLiveMinion(GameObject gameObject)
        {
            foreach (var minion in Components.LiveMinionIdentities.Items)
            {
                if (minion != null && minion.gameObject == gameObject)
                    return true;
            }
            return false;
        }

    }
}
