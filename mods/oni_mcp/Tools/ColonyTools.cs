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
    /// <summary>
    /// 殖民地信息相关 MCP Tools
    /// </summary>
    public static class ColonyTools
    {
        public static McpTool GetColonyInfo()
        {
            return new McpTool
            {
                Name = "colony_status",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_colony_info" },
                Description = "获取殖民地基本信息，包括周期、复制人数量、世界数量等",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var cycle = GameUtil.GetCurrentCycle();
                    var duplicantCount = Components.LiveMinionIdentities.Count;
                    var worldCount = ClusterManager.Instance?.worldCount ?? 0;
                    var activeWorldId = ClusterManager.Instance?.activeWorldId ?? -1;

                    var info = new Dictionary<string, object>
                    {
                        ["cycle"] = cycle,
                        ["duplicantCount"] = duplicantCount,
                        ["worldCount"] = worldCount,
                        ["activeWorldId"] = activeWorldId,
                        ["gameSpeed"] = Time.timeScale,
                        ["isPaused"] = Time.timeScale == 0f
                    };

                    // 尝试获取存档名
                    try
                    {
                        if (SaveLoader.Instance?.GameInfo != null)
                        {
                            info["saveName"] = SaveLoader.Instance.GameInfo.baseName;
                        }
                    }
                    catch { }

                    return CallToolResult.Text(JsonConvert.SerializeObject(info, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetDuplicants()
        {
            return new McpTool
            {
                Name = "dupes_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_duplicants" },
                Description = "获取所有复制人（Duplicants）的基本信息",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var minions = new List<Dictionary<string, object>>();

                    foreach (var minion in Components.LiveMinionIdentities.Items)
                    {
                        if (minion == null) continue;

                        var name = minion.GetProperName();
                        var resume = minion.GetComponent<MinionResume>();
                        var skills = resume != null
                            ? resume.MasteryBySkillID.Where(kv => kv.Value).Select(kv => kv.Key).ToList()
                            : new List<string>();

                        var amounts = new Dictionary<string, float>();
                        var amountInstance = minion.GetComponent<Klei.AI.Amounts>();
                        if (amountInstance != null)
                        {
                            foreach (var amount in amountInstance.ModifierList)
                            {
                                if (amount != null)
                                    amounts[amount.amount.Name] = amount.value;
                            }
                        }

                        minions.Add(new Dictionary<string, object>
                        {
                            ["id"] = minion.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                            ["name"] = name,
                            ["skillsMastered"] = skills,
                            ["availableSkillPoints"] = resume?.AvailableSkillpoints ?? 0,
                            ["amounts"] = amounts,
                            ["worldId"] = minion.GetMyWorldId()
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(minions, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetWorlds()
        {
            return new McpTool
            {
                Name = "world_list",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_worlds" },
                Description = "获取所有世界（小行星）的信息",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (ClusterManager.Instance == null)
                        return CallToolResult.Error("ClusterManager not initialized");

                    var worlds = new List<Dictionary<string, object>>();

                    foreach (var world in ClusterManager.Instance.WorldContainers)
                    {
                        if (world == null) continue;

                        worlds.Add(new Dictionary<string, object>
                        {
                            ["id"] = world.id,
                            ["name"] = world.GetProperName(),
                            ["isActive"] = world.id == ClusterManager.Instance.activeWorldId,
                            ["width"] = world.Width,
                            ["height"] = world.Height,
                            ["asteroidType"] = world.worldName
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(worlds, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetResources()
        {
            return new McpTool
            {
                Name = "resources_discovered",
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_resources" },
                Description = "获取殖民地已发现的资源列表",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (DiscoveredResources.Instance == null)
                        return CallToolResult.Error("DiscoveredResources not initialized");

                    var discovered = DiscoveredResources.Instance.GetDiscovered();
                    var resources = discovered.Select(tag => tag.Name).OrderBy(n => n).ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["count"] = resources.Count,
                        ["resources"] = resources
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }
    }
}
