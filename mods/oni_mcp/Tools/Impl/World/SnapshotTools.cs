using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
        private static readonly SnapshotDeltaCache DeltaCache = new SnapshotDeltaCache();

        public static McpTool GetColonyStateSnapshot()
        {
            return new McpTool
            {
                Name = "colony_state_snapshot",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "state_snapshot", "colony_snapshot" },
                Tags = new List<string> { "snapshot", "colony", "status", "diagnostics", "dupes", "food", "research", "performance", "快照", "状态" },
                Description = "兼容入口：请优先使用 colony_control domain=snapshot action=get。一次返回低 token 殖民地状态快照，替代 game_control domain=speed action=time + colony_status + colony_diagnostics + colony_alerts + resources_food + dupes_list + research_status 的常规组合调用。默认不扫描全图大气；需要氧气统计时显式 includeAtmosphere=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["profile"] = new McpToolParameter { Type = "string", Description = "minimal/brief/standard/full；minimal 只返回循环、暂停、告警级别、核心指标和一句摘要", Required = false, EnumValues = new List<string> { "minimal", "brief", "standard", "full" } },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界；传 -1 汇总全部可轻量统计项", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否包含复制人摘要，默认 true", Required = false },
                    ["includeFood"] = new McpToolParameter { Type = "boolean", Description = "是否包含食物摘要，默认 true", Required = false },
                    ["includeResearch"] = new McpToolParameter { Type = "boolean", Description = "是否包含研究状态，默认 true", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "是否包含关键建筑计数，默认 true", Required = false },
                    ["includeAlerts"] = new McpToolParameter { Type = "boolean", Description = "是否包含快照告警，默认 true", Required = false },
                    ["includeAtmosphere"] = new McpToolParameter { Type = "boolean", Description = "是否扫描可见大气格子；这是全图扫描，默认 false", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只统计已揭示格子内资源/食物，默认 true；调试可传 false", Required = false },
                    ["dupeLimit"] = new McpToolParameter { Type = "integer", Description = "最多返回复制人明细数量，默认 brief=0 standard=12 full=50", Required = false },
                    ["foodLimit"] = new McpToolParameter { Type = "integer", Description = "最多返回食物类型数量，默认 brief=0 standard=8 full=50", Required = false },
                    ["delta"] = new McpToolParameter { Type = "boolean", Description = "只返回相对同 session/同 deltaKey 上次调用的变化；首次或 resetDelta=true 返回 baseline", Required = false },
                    ["deltaKey"] = new McpToolParameter { Type = "string", Description = "可选 delta 缓存槽；同一 agent 可为不同观察循环保留不同 baseline", Required = false },
                    ["resetDelta"] = new McpToolParameter { Type = "boolean", Description = "清除本次 delta baseline 并把当前结果作为新 baseline", Required = false },
                    ["watch"] = new McpToolParameter { Type = "array", Description = "关注指标数组或逗号字符串，如 stress,food_kcal,red_alert,alerts,oxygen；提供后返回 watch 块", Required = false },
                    ["watchOnly"] = new McpToolParameter { Type = "boolean", Description = "只返回 watch 指标、告警级别和摘要，适合循环轮询", Required = false },
                    ["thresholds"] = new McpToolParameter { Type = "object", Description = "关注阈值，例如 {\"stress\":\">60\",\"food_kcal\":\"<20000\"}；也兼容 threshold", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string profile = NormalizeProfile(args["profile"]?.ToString());
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? 0);
                    bool includeDupes = ToolUtil.GetBool(args, "includeDupes", true);
                    bool includeFood = ToolUtil.GetBool(args, "includeFood", true);
                    bool includeResearch = ToolUtil.GetBool(args, "includeResearch", true);
                    bool includeBuildings = ToolUtil.GetBool(args, "includeBuildings", true);
                    bool includeAlerts = ToolUtil.GetBool(args, "includeAlerts", true);
                    bool includeAtmosphere = ToolUtil.GetBool(args, "includeAtmosphere", false);
                    bool visibleOnly = ToolUtil.GetBool(args, "visibleOnly", true);
                    int dupeLimit = Clamp(ToolUtil.GetInt(args, "dupeLimit") ?? DefaultDupeLimit(profile), 0, 100);
                    int foodLimit = Clamp(ToolUtil.GetInt(args, "foodLimit") ?? DefaultFoodLimit(profile), 0, 100);
                    bool minimal = profile == "minimal";
                    var watchKeys = ParseWatch(args["watch"]);
                    bool watchOnly = ToolUtil.GetBool(args, "watchOnly", false);
                    bool needAtmosphere = includeAtmosphere || WatchNeedsAtmosphere(watchKeys);

                    List<DupeSnapshot> dupes = (includeDupes || includeAlerts || minimal || watchOnly || watchKeys.Count > 0) ? BuildDupes(worldId) : new List<DupeSnapshot>();
                    FoodSnapshot food = (includeFood || includeAlerts || minimal || watchOnly || watchKeys.Count > 0) ? BuildFood(worldId, foodLimit, visibleOnly) : null;
                    BuildingSnapshot buildings = (includeBuildings || includeAlerts || minimal || watchOnly || watchKeys.Count > 0) ? BuildBuildings(worldId) : null;
                    Dictionary<string, object> atmosphere = needAtmosphere ? BuildAtmosphere(worldId) : null;
                    var redAlert = BuildRedAlert(worldId);
                    var alerts = includeAlerts || minimal || watchOnly || watchKeys.Count > 0
                        ? BuildAlerts(dupes, food, buildings, atmosphere, redAlert)
                        : new Dictionary<string, object> { ["count"] = 0, ["items"] = new List<Dictionary<string, object>>() };
                    var metrics = BuildMetrics(dupes, food, buildings, atmosphere, redAlert, alerts);
                    var time = BuildTime();
                    var colony = BuildColony();

                    Dictionary<string, object> snapshot = minimal
                        ? BuildMinimalSnapshot(profile, worldId, time, colony, metrics)
                        : new Dictionary<string, object>
                        {
                            ["v"] = 2,
                            ["profile"] = profile,
                            ["worldId"] = worldId,
                            ["visibleOnly"] = visibleOnly,
                            ["time"] = time,
                            ["colony"] = colony
                        };

                    if (!minimal && includeDupes)
                        snapshot["dupes"] = BuildDupeResult(dupes, dupeLimit, profile);
                    if (!minimal && includeFood && food != null)
                        snapshot["food"] = food.ToDictionary();
                    if (!minimal && includeBuildings && buildings != null)
                        snapshot["buildings"] = buildings.ToDictionary();
                    if (!minimal && includeResearch)
                        snapshot["research"] = BuildResearch();
                    if (!minimal && needAtmosphere)
                        snapshot["atmosphere"] = atmosphere;
                    if (!minimal && includeAlerts)
                        snapshot["alerts"] = alerts;

                    if (watchKeys.Count > 0)
                        snapshot["watch"] = BuildWatch(watchKeys, metrics, args["thresholds"] as JObject ?? args["threshold"] as JObject);
                    if (watchOnly)
                        snapshot = BuildWatchOnlySnapshot(profile, worldId, time, metrics, snapshot.ContainsKey("watch") ? snapshot["watch"] : BuildWatch(DefaultWatchKeys(), metrics, args["thresholds"] as JObject ?? args["threshold"] as JObject));

                    snapshot["cost"] = new Dictionary<string, object>
                    {
                        ["singleTool"] = true,
                        ["deltaAvailable"] = true,
                        ["watchOnly"] = watchOnly,
                        ["replaces"] = new[] { "game_control domain=speed action=time", "colony_control domain=read action=status", "colony_control domain=diagnostic action=diagnostics", "colony_control domain=diagnostic action=alerts", "read_control domain=resources action=food", "colony_control domain=read action=dupes", "colony_control domain=management kind=research action=status" },
                        ["fullGridScan"] = needAtmosphere
                    };
                    if (needAtmosphere)
                        ((Dictionary<string, object>)snapshot["cost"])["atmosphereNote"] = "Atmosphere is a full visible-grid aggregate and can mask local pockets; use world_area_snapshot on suspected rooms for local oxygen/temperature.";

                    if (ToolUtil.GetBool(args, "delta", false))
                    {
                        string deltaKey = BuildDeltaKey(args, profile, worldId, watchKeys, watchOnly);
                        var delta = DeltaCache.Apply(McpHttpServer.CurrentSessionId ?? "global", deltaKey, snapshot, ToolUtil.GetBool(args, "resetDelta", false));
                        return CallToolResult.Text(JsonConvert.SerializeObject(delta, McpJsonUtil.Settings));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(snapshot, McpJsonUtil.Settings));
                }
            };
        }
    }
}
