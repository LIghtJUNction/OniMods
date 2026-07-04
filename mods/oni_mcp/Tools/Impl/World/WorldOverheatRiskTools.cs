using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        public static McpTool ScanOverheatRisk()
        {
            return new McpTool
            {
                Name = "thermal_overheat_risk_scan",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "overheat_risk_scan", "thermal_risk_scan" },
                Tags = new List<string> { "thermal", "temperature", "overheat", "buildings", "heat", "温度", "过热" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 read_control domain=world action=thermal_overheat_risk",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；默认当前激活世界，传 -1 扫描全部世界", Required = false },
                    ["marginC"] = new McpToolParameter { Type = "number", Description = "风险温差阈值，低于该值返回；默认 15C", Required = false },
                    ["includeNonOverheatable"] = new McpToolParameter { Type = "boolean", Description = "是否同时返回不可过热但高温的建筑，默认 false", Required = false },
                    ["minTempC"] = new McpToolParameter { Type = "number", Description = "includeNonOverheatable=true 时的最低温度，默认 75C", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 50，最大 200", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int worldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? -1);
                    float marginC = ToolUtil.GetFloat(args, "marginC") ?? 15f;
                    bool includeNonOverheatable = ToolUtil.GetBool(args, "includeNonOverheatable", false);
                    float minTempC = ToolUtil.GetFloat(args, "minTempC") ?? 75f;
                    int limit = ToolUtil.ClampLimit(args, 50, 200);

                    int scanned = 0;
                    int overheatable = 0;
                    int warningCount = 0;
                    int criticalCount = 0;
                    int overheatedCount = 0;
                    var risks = new List<Dictionary<string, object>>();

                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        if (building == null || building.gameObject == null)
                            continue;
                        if (!ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                            continue;

                        int cell = Grid.PosToCell(building.gameObject);
                        if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                            continue;

                        scanned++;
                        var def = building.Def;
                        bool canOverheat = def != null && def.Overheatable;
                        if (canOverheat)
                            overheatable++;

                        float tempK = SafeFloat(Grid.Temperature[cell]);
                        float tempC = tempK - 273.15f;
                        float overheatK = canOverheat ? SafeFloat(def.OverheatTemperature) : 0f;
                        float overheatC = overheatK - 273.15f;
                        float margin = canOverheat ? overheatK - tempK : float.MaxValue;

                        string risk = "none";
                        if (canOverheat)
                        {
                            if (margin <= 0f)
                            {
                                risk = "overheated";
                                overheatedCount++;
                            }
                            else if (margin <= 5f)
                            {
                                risk = "critical";
                                criticalCount++;
                            }
                            else if (margin <= marginC)
                            {
                                risk = "warning";
                                warningCount++;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (!includeNonOverheatable || tempC < minTempC)
                                continue;
                            risk = "hot_non_overheatable";
                        }

                        int x;
                        int y;
                        Grid.CellToXY(cell, out x, out y);
                        var kpid = building.GetComponent<KPrefabID>();
                        risks.Add(new Dictionary<string, object>
                        {
                            ["id"] = kpid?.InstanceID ?? building.gameObject.GetInstanceID(),
                            ["name"] = ToolUtil.CleanName(building.GetProperName()),
                            ["prefabId"] = def?.PrefabID ?? kpid?.PrefabTag.Name ?? building.name,
                            ["cell"] = cell,
                            ["x"] = x,
                            ["y"] = y,
                            ["worldId"] = Grid.WorldIdx[cell],
                            ["temperatureC"] = Math.Round(tempC, 2),
                            ["overheatC"] = canOverheat ? (object)Math.Round(overheatC, 2) : null,
                            ["marginC"] = canOverheat ? (object)Math.Round(margin, 2) : null,
                            ["risk"] = risk,
                            ["operational"] = building.GetComponent<Operational>()?.IsOperational
                        });
                    }

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["marginC"] = marginC,
                        ["scannedBuildings"] = scanned,
                        ["overheatableBuildings"] = overheatable,
                        ["warningCount"] = warningCount,
                        ["criticalCount"] = criticalCount,
                        ["overheatedCount"] = overheatedCount,
                        ["returned"] = Math.Min(limit, risks.Count),
                        ["risks"] = risks
                            .OrderBy(item => item["marginC"] == null ? double.MaxValue : Convert.ToDouble(item["marginC"]))
                            .ThenByDescending(item => Convert.ToDouble(item["temperatureC"]))
                            .Take(limit)
                            .ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

    }
}
