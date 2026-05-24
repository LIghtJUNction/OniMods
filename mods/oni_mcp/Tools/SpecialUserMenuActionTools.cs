using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class SpecialUserMenuActionTools
    {
        public static McpTool ListMutantSeedControls()
        {
            return new McpTool
            {
                Name = "mutant_seed_controls_list",
                Group = "production",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "accept_mutant_seeds_list", "mutated_seed_toggles_list" },
                Tags = new List<string> { "production", "farming", "mutant-seeds", "fish-feeder", "spice-grinder", "fabricator", "user-menu" },
                Description = "列出玩家菜单里的接受/拒收突变种子开关，覆盖 ComplexFabricator、FishFeeder 和 SpiceGrinder",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId 或组件类型筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    var rows = FindMutantSeedTargets(args)
                        .Select(MutantSeedInfo)
                        .Where(info => MatchesQuery(info, args["query"]?.ToString()))
                        .Take(ToolUtil.ClampLimit(args, 100, 500))
                        .ToList();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["controls"] = rows,
                        ["setTool"] = "mutant_seed_control_set"
                    });
                }
            };
        }

        public static McpTool SetMutantSeedControl()
        {
            return new McpTool
            {
                Name = "mutant_seed_control_set",
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "accept_mutant_seeds_set", "mutated_seed_toggle_set" },
                Tags = new List<string> { "production", "farming", "mutant-seeds", "fish-feeder", "spice-grinder", "fabricator", "user-menu" },
                Description = "设置建筑是否接受突变种子。accept=true 接受，accept=false 拒收；覆盖制作站、鱼喂食器和香料研磨器",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["accept"] = new McpToolParameter { Type = "boolean", Description = "true=接受突变种子，false=拒收突变种子", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改玩家菜单开关", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    var go = FindMutantSeedTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target mutant seed control not found");
                    bool accept = ToolUtil.GetBool(args, "accept", true);
                    var before = MutantSeedInfo(go);
                    string error = SetMutantSeedAccept(go, accept);
                    if (error != null)
                        return CallToolResult.Error(error);
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["after"] = MutantSeedInfo(go)
                    });
                }
            };
        }

        public static McpTool ListRocketUsageControls()
        {
            return new McpTool
            {
                Name = "rocket_usage_controls_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "rocket_usage_restrictions_list", "rocket_controlled_buildings_list" },
                Tags = new List<string> { "rocket", "usage", "restriction", "control-station", "user-menu" },
                Description = "列出火箭内部建筑玩家菜单的“受控制台限制/不受限制”状态",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId 或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    var rows = FindRocketUsageTargets(args)
                        .Select(go => RocketUsageInfo(go.GetSMI<RocketUsageRestriction.StatesInstance>()))
                        .Where(info => MatchesQuery(info, args["query"]?.ToString()))
                        .Take(ToolUtil.ClampLimit(args, 100, 500))
                        .ToList();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["controls"] = rows,
                        ["setTool"] = "rocket_usage_control_set"
                    });
                }
            };
        }

        public static McpTool SetRocketUsageControl()
        {
            return new McpTool
            {
                Name = "rocket_usage_control_set",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "rocket_usage_restriction_set", "rocket_controlled_building_set" },
                Tags = new List<string> { "rocket", "usage", "restriction", "control-station", "user-menu" },
                Description = "设置火箭内部建筑是否受 RocketControlStation 限制。controlled=true 受控制台限制，false 不受限制",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["controlled"] = new McpToolParameter { Type = "boolean", Description = "true=受控制台限制，false=不受控制台限制", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改火箭内部建筑限制", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    var go = FindRocketUsageTarget(args);
                    var smi = go?.GetSMI<RocketUsageRestriction.StatesInstance>();
                    if (smi == null)
                        return CallToolResult.Error("Target rocket usage control not found");
                    var before = RocketUsageInfo(smi);
                    smi.isControlled = ToolUtil.GetBool(args, "controlled", true);
                    smi.GoToRestrictionState();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["after"] = RocketUsageInfo(smi)
                    });
                }
            };
        }

        private static IEnumerable<GameObject> FindMutantSeedTargets(JObject args)
        {
            bool hasLookup = HasLookupInput(args);
            if (hasLookup)
            {
                var target = FindMutantSeedTarget(args);
                if (target != null)
                    yield return target;
                yield break;
            }
            foreach (var go in AllCandidateObjects(args))
            {
                if (HasMutantSeedControl(go))
                    yield return go;
            }
        }

        private static GameObject FindMutantSeedTarget(JObject args)
        {
            return AllCandidateObjects(args).FirstOrDefault(HasMutantSeedControl);
        }

        private static bool HasMutantSeedControl(GameObject go)
        {
            return go != null
                   && (go.GetComponent<ComplexFabricator>() != null
                       || go.GetSMI<FishFeeder.Instance>() != null
                       || go.GetSMI<SpiceGrinder.StatesInstance>() != null);
        }

        private static Dictionary<string, object> MutantSeedInfo(GameObject go)
        {
            var result = TargetInfo(go);
            var fabricator = go.GetComponent<ComplexFabricator>();
            var fish = go.GetSMI<FishFeeder.Instance>();
            var spice = go.GetSMI<SpiceGrinder.StatesInstance>();
            if (fabricator != null)
            {
                result["componentType"] = "ComplexFabricator";
                result["acceptMutantSeeds"] = !fabricator.ForbidMutantSeeds;
                result["forbidMutantSeeds"] = fabricator.ForbidMutantSeeds;
            }
            else if (fish != null)
            {
                result["componentType"] = "FishFeeder";
                result["acceptMutantSeeds"] = !fish.ForbidMutantSeeds;
                result["forbidMutantSeeds"] = fish.ForbidMutantSeeds;
            }
            else if (spice != null)
            {
                result["componentType"] = "SpiceGrinder";
                result["acceptMutantSeeds"] = spice.AllowMutantSeeds;
                result["forbidMutantSeeds"] = !spice.AllowMutantSeeds;
            }
            result["dlcRadiationEnabled"] = DlcManager.FeatureRadiationEnabled();
            return result;
        }

        private static string SetMutantSeedAccept(GameObject go, bool accept)
        {
            var fabricator = go.GetComponent<ComplexFabricator>();
            if (fabricator != null)
            {
                fabricator.ForbidMutantSeeds = !accept;
                return null;
            }
            var fish = go.GetSMI<FishFeeder.Instance>();
            if (fish != null)
            {
                fish.ForbidMutantSeeds = !accept;
                return null;
            }
            var spice = go.GetSMI<SpiceGrinder.StatesInstance>();
            if (spice != null)
            {
                spice.AllowMutantSeeds = accept;
                return null;
            }
            return "Target does not support mutant seed controls";
        }

        private static IEnumerable<GameObject> FindRocketUsageTargets(JObject args)
        {
            bool hasLookup = HasLookupInput(args);
            if (hasLookup)
            {
                var target = FindRocketUsageTarget(args);
                if (target != null)
                    yield return target;
                yield break;
            }
            foreach (var go in AllCandidateObjects(args))
            {
                if (go.GetSMI<RocketUsageRestriction.StatesInstance>() != null)
                    yield return go;
            }
        }

        private static GameObject FindRocketUsageTarget(JObject args)
        {
            return AllCandidateObjects(args).FirstOrDefault(go => go.GetSMI<RocketUsageRestriction.StatesInstance>() != null);
        }

        private static Dictionary<string, object> RocketUsageInfo(RocketUsageRestriction.StatesInstance smi)
        {
            var result = TargetInfo(smi.gameObject);
            result["componentType"] = "RocketUsageRestriction";
            result["controlledByRocketStation"] = smi.isControlled;
            result["restrictionApplied"] = smi.isRestrictionApplied;
            result["buildingRestrictionsActive"] = smi.BuildingRestrictionsActive();
            result["operationalAllowed"] = smi.operational == null ? null : (object)smi.operational.GetFlag(RocketUsageRestriction.rocketUsageAllowed);
            result["worldIsRocketInterior"] = smi.master.gameObject.GetMyWorld()?.IsModuleInterior ?? false;
            return result;
        }

        private static IEnumerable<GameObject> AllCandidateObjects(JObject args)
        {
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            string query = args["query"]?.ToString();

            var seen = new HashSet<int>();
            foreach (var kpid in UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsSortMode.None))
            {
                var go = kpid?.gameObject;
                if (go == null || !seen.Add(go.GetInstanceID()))
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                if (id.HasValue && kpid.InstanceID != id.Value)
                    continue;
                if (cell.HasValue && Grid.PosToCell(go) != cell.Value)
                    continue;
                if (rect != null && !CellInRect(Grid.PosToCell(go), rect, worldId))
                    continue;
                if (!string.IsNullOrWhiteSpace(query) && !MatchesQuery(TargetInfo(go), query))
                    continue;
                yield return go;
            }
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            return Grid.IsValidCell(cell)
                   && ToolUtil.CellMatchesWorld(cell, worldId)
                   && Grid.CellColumn(cell) >= rect["x1"]
                   && Grid.CellColumn(cell) <= rect["x2"]
                   && Grid.CellRow(cell) >= rect["y1"]
                   && Grid.CellRow(cell) <= rect["y2"];
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : go.GetComponent<KMonoBehaviour>()?.GetMyWorldId() ?? -1
            };
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                   || JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasLookupInput(JObject args)
        {
            return args["id"] != null || (args["x"] != null && args["y"] != null);
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                   || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄；与 x1/y1/x2/y2 二选一", Required = false };
            parameters["x1"] = new McpToolParameter { Type = "integer", Description = "矩形左下/左上 X", Required = false };
            parameters["y1"] = new McpToolParameter { Type = "integer", Description = "矩形左下/左上 Y", Required = false };
            parameters["x2"] = new McpToolParameter { Type = "integer", Description = "矩形右上/右下 X", Required = false };
            parameters["y2"] = new McpToolParameter { Type = "integer", Description = "矩形右上/右下 Y", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；省略时不限世界", Required = false };
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["id"] = new McpToolParameter { Type = "integer", Description = "目标 KPrefabID.InstanceID；推荐", Required = false };
            parameters["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；未传 id 时使用", Required = false };
            parameters["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；未传 id 时使用", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时建议提供", Required = false };
            parameters["query"] = new McpToolParameter { Type = "string", Description = "目标模糊匹配；不建议在写入时替代 id/坐标", Required = false };
            return parameters;
        }

        private static CallToolResult JsonResult(object payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
