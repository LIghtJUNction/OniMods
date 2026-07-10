using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using TUNING;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SpaceStoryTools
    {
        private static Dictionary<string, object> WarpPortalInfo(WarpPortal portal)
        {
            var result = TargetInfo(portal.gameObject);
            result["readyToWarp"] = portal.ReadyToWarp;
            result["isWorking"] = portal.IsWorking;
            result["isConsumed"] = portal.IsConsumed;
            result["rechargeProgress"] = Math.Round(ToolUtil.SafeFloat(portal.rechargeProgress / 3000f), 4);
            result["rechargeSecondsRemaining"] = Math.Round(Math.Max(0f, 3000f - ToolUtil.SafeFloat(portal.rechargeProgress)), 1);
            result["assignee"] = portal.assignable?.assignee?.GetProperName();
            return result;
        }

        private static Dictionary<string, object> TelescopeInfo(GameObject go)
        {
            var result = TargetInfo(go);
            result["analysis"] = AnalysisSummary(includeDestinations: false);
            return result;
        }

        private static Dictionary<string, object> AnalysisSummary(bool includeDestinations)
        {
            var result = new Dictionary<string, object>
            {
                ["available"] = SpacecraftManager.instance != null,
                ["hasTarget"] = SpacecraftManager.instance != null && SpacecraftManager.instance.HasAnalysisTarget(),
                ["targetId"] = SpacecraftManager.instance != null ? SpacecraftManager.instance.GetStarmapAnalysisDestinationID() : -1,
                ["completePoints"] = ROCKETRY.DESTINATION_ANALYSIS.COMPLETE,
                ["discoveredPoints"] = ROCKETRY.DESTINATION_ANALYSIS.DISCOVERED
            };
            if (SpacecraftManager.instance != null && SpacecraftManager.instance.HasAnalysisTarget())
            {
                var destination = SpacecraftManager.instance.GetDestination(SpacecraftManager.instance.GetStarmapAnalysisDestinationID());
                result["target"] = destination == null ? null : DestinationInfo(destination);
            }
            if (includeDestinations)
                result["destinations"] = AnalysisDestinations();
            return result;
        }

        private static List<Dictionary<string, object>> AnalysisDestinations()
        {
            if (SpacecraftManager.instance?.destinations == null)
                return new List<Dictionary<string, object>>();
            return SpacecraftManager.instance.destinations.Select(DestinationInfo).ToList();
        }

        private static Dictionary<string, object> DestinationInfo(SpaceDestination destination)
        {
            var type = destination.GetDestinationType();
            float score = ToolUtil.SafeFloat(SpacecraftManager.instance.GetDestinationAnalysisScore(destination.id));
            var state = SpacecraftManager.instance.GetDestinationAnalysisState(destination);
            return new Dictionary<string, object>
            {
                ["id"] = destination.id,
                ["typeId"] = destination.type,
                ["name"] = ToolUtil.CleanName(type?.typeName ?? destination.type),
                ["distance"] = destination.distance,
                ["oneBasedDistance"] = destination.OneBasedDistance,
                ["analysisScore"] = Math.Round(score, 2),
                ["analysisProgress"] = Math.Round(score / Math.Max(1f, (float)ROCKETRY.DESTINATION_ANALYSIS.COMPLETE), 4),
                ["analysisState"] = state.ToString(),
                ["selected"] = SpacecraftManager.instance.GetStarmapAnalysisDestinationID() == destination.id,
                ["visitable"] = type?.visitable ?? false,
                ["availableMassKg"] = Math.Round(ToolUtil.SafeFloat(destination.AvailableMass), 3),
                ["currentMassKg"] = Math.Round(ToolUtil.SafeFloat(destination.CurrentMass), 3),
                ["researchOpportunities"] = destination.researchOpportunities == null ? 0 : destination.researchOpportunities.Count,
                ["completedResearchOpportunities"] = destination.researchOpportunities == null ? 0 : destination.researchOpportunities.Count(item => item.completed)
            };
        }

        private static SpaceDestination ResolveDestination(JObject args)
        {
            if (SpacecraftManager.instance?.destinations == null)
                return null;
            int? id = ToolUtil.GetInt(args, "destinationId");
            if (id.HasValue)
                return SpacecraftManager.instance.destinations.FirstOrDefault(destination => destination.id == id.Value);
            string query = args["query"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                return null;
            var matches = SpacecraftManager.instance.destinations
                .Where(destination => MatchesQuery(DestinationInfo(destination), query))
                .Take(2)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static TemporalTear GetTemporalTear()
        {
            return ClusterManager.Instance?.GetComponent<ClusterPOIManager>()?.GetTemporalTear();
        }

        private static Dictionary<string, object> TemporalTearInfo(TemporalTear tear)
        {
            return new Dictionary<string, object>
            {
                ["name"] = ToolUtil.CleanName(tear.Name),
                ["location"] = AxialToDictionary(tear.Location),
                ["isOpen"] = tear.IsOpen(),
                ["hasConsumedCraft"] = tear.HasConsumedCraft(),
                ["isRevealed"] = ClusterManager.Instance?.GetComponent<ClusterPOIManager>()?.IsTemporalTearRevealed() ?? false
            };
        }

        private static IEnumerable<Clustercraft> TemporalTearCandidates(TemporalTear tear)
        {
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft != null && craft.Location == tear.Location)
                    yield return craft;
            }
        }

        private static Dictionary<string, object> ClustercraftInfo(Clustercraft craft)
        {
            var result = ClusterEntityInfo(craft);
            result["status"] = craft.Status.ToString();
            result["destination"] = AxialToDictionary(craft.Destination);
            result["isFlightInProgress"] = craft.IsFlightInProgress();
            result["interiorWorldId"] = craft.ModuleInterface?.GetInteriorWorld()?.id ?? -1;
            result["onboardDupes"] = CountDupesInCraft(craft);
            return result;
        }

        private static int CountDupesInCraft(Clustercraft craft)
        {
            int worldId = craft.ModuleInterface?.GetInteriorWorld()?.id ?? -1;
            if (worldId < 0)
                return 0;
            return Components.MinionIdentities.Items.Count(minion => minion != null && minion.GetMyWorldId() == worldId);
        }

        private static Clustercraft FindClustercraft(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "craftId");
            string name = args["craftName"]?.ToString();
            var tear = GetTemporalTear();
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null)
                    continue;
                if (tear != null && craft.Location != tear.Location)
                    continue;
                var kpid = craft.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return craft;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(ToolUtil.CleanName(craft.Name), name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return craft;
            }
            return null;
        }

        private static Dictionary<string, object> ProcessConditionSetInfo(GameObject go, ProcessCondition.ProcessConditionType type, bool showHidden)
        {
            var conditionSet = go.GetComponent<IProcessConditionSet>();
            if (conditionSet == null)
                return null;
            var conditions = new List<Dictionary<string, object>>();
            var raw = new List<ProcessCondition>();
            conditionSet.PopulateConditionSet(type, raw);
            foreach (var condition in raw)
            {
                if (condition == null || (!showHidden && !condition.ShowInUI()))
                    continue;
                var status = condition.EvaluateCondition();
                conditions.Add(new Dictionary<string, object>
                {
                    ["status"] = status.ToString(),
                    ["message"] = ToolUtil.CleanName(condition.GetStatusMessage(status)),
                    ["tooltip"] = ToolUtil.CleanName(condition.GetStatusTooltip(status)),
                    ["showInUi"] = condition.ShowInUI()
                });
            }

            var result = TargetOrClusterInfo(go);
            result["conditionCount"] = conditions.Count;
            result["failureCount"] = conditions.Count(condition => (string)condition["status"] == ProcessCondition.Status.Failure.ToString());
            result["warningCount"] = conditions.Count(condition => (string)condition["status"] == ProcessCondition.Status.Warning.ToString());
            result["readyCount"] = conditions.Count(condition => (string)condition["status"] == ProcessCondition.Status.Ready.ToString());
            result["conditions"] = conditions;
            return result;
        }

        private static ProcessCondition.ProcessConditionType ParseConditionType(string raw)
        {
            ProcessCondition.ProcessConditionType type;
            return Enum.TryParse(raw ?? "All", true, out type) ? type : ProcessCondition.ProcessConditionType.All;
        }

        private static IEnumerable<GameObject> ConditionTargets()
        {
            var seen = new HashSet<int>();
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go != null && go.GetComponent<IProcessConditionSet>() != null && seen.Add(go.GetInstanceID()))
                    yield return go;
            }
            foreach (var craft in Components.Clustercrafts.Items)
            {
                var go = craft?.gameObject;
                if (go != null && go.GetComponent<IProcessConditionSet>() != null && seen.Add(go.GetInstanceID()))
                    yield return go;
            }
        }

        private static GameObject FindConditionTarget(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            bool hasLookup = id.HasValue || (x.HasValue && y.HasValue);
            if (!hasLookup)
                return null;
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var go in ConditionTargets())
            {
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && ToolUtil.GameObjectMatchesWorld(go, worldId) && Grid.PosToCell(go) == cell.Value)
                    return go;
            }
            return null;
        }

        private static CallToolResult ListBuildings(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["query"]?.ToString();
            int limit = ToolUtil.ClampLimit(args, 100, 500);
            var items = Components.BuildingCompletes.Items
                .Select(building => building?.gameObject)
                .Where(go => MatchesTarget(go, rect, worldId))
                .Where(predicate)
                .Select(selector)
                .Where(info => MatchesQuery(info, query))
                .OrderBy(info => info["name"].ToString())
                .Take(limit)
                .ToList();
            return JsonResult(new Dictionary<string, object>
            {
                ["returned"] = items.Count,
                ["worldId"] = worldId >= 0 ? (object)worldId : null,
                [payloadKey] = items
            });
        }

        private static GameObject FindBuilding(JObject args, Func<GameObject, bool> predicate)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId) || !predicate(go))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }
            return null;
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            int cell = Grid.PosToCell(go);
            return rect == null || CellInRect(cell, rect, worldId);
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> TargetOrClusterInfo(GameObject go)
        {
            var cluster = go.GetComponent<ClusterGridEntity>();
            return cluster != null ? ClusterEntityInfo(cluster) : TargetInfo(go);
        }

        private static Dictionary<string, object> ClusterEntityInfo(ClusterGridEntity entity)
        {
            var kpid = entity.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? entity.gameObject.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? entity.gameObject.name,
                ["name"] = ToolUtil.CleanName(entity.Name),
                ["location"] = AxialToDictionary(entity.Location),
                ["layer"] = entity.Layer.ToString()
            };
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
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static Dictionary<string, object> AxialToDictionary(AxialI location)
        {
            return new Dictionary<string, object>
            {
                ["q"] = location.Q,
                ["r"] = location.R
            };
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> SpaceStoryControlParams()
        {
            return LookupParams(RectParams(new Dictionary<string, McpToolParameter>
            {
                ["kind"] = new McpToolParameter { Type = "string", Description = "warp_portal、telescope、starmap_analysis、temporal_tear 或 process_conditions", Required = true },
                ["action"] = new McpToolParameter { Type = "string", Description = "list/status 或对应操作：start_warp、cancel_assignment、open_starmap、set/clear、consume_craft", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "按名称、prefabId、状态、目的地、火箭或条件筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "list 返回上限", Required = false },
                ["includeTargets"] = new McpToolParameter { Type = "boolean", Description = "kind=telescope action=list 时是否附带分析目标摘要", Required = false },
                ["includeComplete"] = new McpToolParameter { Type = "boolean", Description = "kind=starmap_analysis action=list 时是否包含已完成目标", Required = false },
                ["destinationId"] = new McpToolParameter { Type = "integer", Description = "kind=starmap_analysis action=set 时 SpaceDestination id", Required = false },
                ["clear"] = new McpToolParameter { Type = "boolean", Description = "kind=starmap_analysis 时 true=清除当前分析目标", Required = false },
                ["allowComplete"] = new McpToolParameter { Type = "boolean", Description = "kind=starmap_analysis action=set 时允许选择已完成目标", Required = false },
                ["craftId"] = new McpToolParameter { Type = "integer", Description = "kind=temporal_tear action=consume_craft 时目标 Clustercraft InstanceID", Required = false },
                ["craftName"] = new McpToolParameter { Type = "string", Description = "kind=temporal_tear action=consume_craft 时目标火箭名称", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写入、传送、分析目标变更和危险操作按旧工具要求传 true", Required = false },
                ["confirmDestructive"] = new McpToolParameter { Type = "string", Description = "kind=temporal_tear action=consume_craft 时必须精确填写 consume craft", Required = false },
                ["conditionType"] = new McpToolParameter { Type = "string", Description = "kind=process_conditions 时：All/RocketFlight/RocketPrep/RocketStorage/RocketBoard", Required = false },
                ["showHidden"] = new McpToolParameter { Type = "boolean", Description = "kind=process_conditions 时是否包含 ShowInUI=false 的条件", Required = false }
            }));
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X；使用 areaId 时可省略", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y；使用 areaId 时可省略", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X；使用 areaId 时可省略", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y；使用 areaId 时可省略", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认 areaId 绑定世界或当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                   || ToolUtil.GetInt(args, "x1").HasValue
                   || ToolUtil.GetInt(args, "y1").HasValue
                   || ToolUtil.GetInt(args, "x2").HasValue
                   || ToolUtil.GetInt(args, "y2").HasValue;
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

        private static CallToolResult JsonResult(Dictionary<string, object> payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
