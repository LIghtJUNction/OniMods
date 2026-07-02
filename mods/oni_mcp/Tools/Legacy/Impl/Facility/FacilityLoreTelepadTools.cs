using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using STRINGS;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        public static McpTool ListLoreBearers()
        {
            return new McpTool
            {
                Name = "lore_bearers_list",
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "readable_lore_list", "inspect_lore_list" },
                Tags = new List<string> { "story", "lore", "inspect", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=lore_bearer action=list",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象、prefabId、按钮文本或 tooltip 筛选", Required = false },
                    ["interactableOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回当前可阅读对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    bool interactableOnly = ToolUtil.GetBool(args, "interactableOnly", false);
                    var items = ListObjectTargets(args, go => IsValidLoreBearer(go) && (!interactableOnly || go.GetComponent<LoreBearer>().SidescreenButtonInteractable()), LoreBearerInfo, "loreBearers");
                    return items;
                }
            };
        }

        public static McpTool PressLoreBearer()
        {
            return new McpTool
            {
                Name = "lore_bearer_press",
                Group = "story",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "lore_read", "inspect_lore" },
                Tags = new List<string> { "story", "lore", "inspect", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=lore_bearer action=press",
                Hidden = true,
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认触发阅读/检查", Required = true },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "跳过 interactable 检查，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to press a lore side-screen button");
                    var go = FindObjectTarget(args, IsValidLoreBearer);
                    if (go == null)
                        return CallToolResult.Error("Target LoreBearer not found");
                    var lore = go.GetComponent<LoreBearer>();
                    bool force = ToolUtil.GetBool(args, "force", false);
                    if (!force && !lore.SidescreenButtonInteractable())
                        return CallToolResult.Error("LoreBearer is already inspected or not interactable");
                    var before = LoreBearerInfo(go);
                    lore.OnSidescreenButtonPressed();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["loreBearer"] = LoreBearerInfo(go)
                    });
                }
            };
        }

        public static McpTool ListTelepads()
        {
            return new McpTool
            {
                Name = "telepads_list",
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "printing_pods_list", "immigration_telepads_list" },
                Tags = new List<string> { "story", "telepad", "printing-pod", "immigration", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=telepad action=list",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId、状态或胜利条件筛选", Required = false },
                    ["includeVictory"] = new McpToolParameter { Type = "boolean", Description = "是否包含胜利条件 checklist，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 50，最大 100", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 50, 100);
                    bool includeVictory = ToolUtil.GetBool(args, "includeVictory", true);
                    var items = Components.Telepads.Items
                        .Where(telepad => telepad != null && MatchesTarget(telepad.gameObject, rect, worldId))
                        .Select(telepad => TelepadInfo(telepad, includeVictory))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["telepads"] = items
                    });
                }
            };
        }

        public static McpTool ControlTelepad()
        {
            return new McpTool
            {
                Name = "telepad_control",
                Group = "story",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "printing_pod_control", "telepad_open_screen" },
                Tags = new List<string> { "story", "telepad", "printing-pod", "immigration", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=side_surface surface=facility kind=telepad action=list_rewards/claim/open_immigrants/open_colony_summary/open_skills/open_research",
                Hidden = true,
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list_rewards/status/rewards、claim、open_immigrants、open_colony_summary、open_skills 或 open_research", Required = true, EnumValues = new List<string> { "list_rewards", "rewards", "status", "claim", "open_immigrants", "open_colony_summary", "open_skills", "open_research" } },
                    ["rewardIndex"] = new McpToolParameter { Type = "integer", Description = "action=claim 时领取 rewards[index]，默认 0", Required = false },
                    ["itemId"] = new McpToolParameter { Type = "string", Description = "action=claim 时按奖励 prefab/tag/id 匹配", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "action=claim 时只预览不领取，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "打开 UI 或领取奖励时必须为 true；list_rewards/status 不需要", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTelepadTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target Telepad not found");
                    var telepad = go.GetComponent<Telepad>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = TelepadInfo(telepad, includeVictory: false);

                    if (action == "list_rewards" || action == "rewards" || action == "status")
                    {
                        return JsonResult(new Dictionary<string, object>
                        {
                            ["target"] = TargetInfo(go),
                            ["telepad"] = before,
                            ["printingRewards"] = PrintingRewardStatus(telepad)
                        });
                    }

                    if (action == "claim")
                        return ClaimPrintingReward(args, telepad, before);

                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true required telepad UI actions");

                    if (action == "open_immigrants")
                    {
                        if (Immigration.Instance == null || !Immigration.Instance.ImmigrantsAvailable)
                            return CallToolResult.Error("No immigrants are available right now");
                        ImmigrantScreen.InitializeImmigrantScreen(telepad);
                        Game.Instance.Trigger(288942073);
                    }
                    else if (action == "open_colony_summary")
                    {
                        if (PauseScreen.Instance == null)
                            return CallToolResult.Error("PauseScreen is not available");
                        MainMenu.ActivateRetiredColoniesScreenFromData(PauseScreen.Instance.transform.parent.gameObject, RetireColonyUtility.GetCurrentColonyRetiredColonyData());
                    }
                    else if (action == "open_skills")
                    {
                        if (ManagementMenu.Instance == null)
                            return CallToolResult.Error("ManagementMenu is not available");
                        ManagementMenu.Instance.ToggleSkills();
                    }
                    else if (action == "open_research")
                    {
                        if (ManagementMenu.Instance == null)
                            return CallToolResult.Error("ManagementMenu is not available");
                        if (!ManagementMenu.Instance.CheckHasResearchCenter())
                            return CallToolResult.Error("No research station is available");
                        ManagementMenu.Instance.ToggleResearch();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be open_immigrants, open_colony_summary, open_skills, or open_research");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["action"] = action,
                        ["before"] = before,
                        ["telepad"] = TelepadInfo(telepad, includeVictory: false)
                    });
                }
            };
        }


        private static bool IsValidLoreBearer(GameObject go)
        {
            var lore = go?.GetComponent<LoreBearer>();
            return lore != null;
        }

        private static Dictionary<string, object> LoreBearerInfo(GameObject go)
        {
            var lore = go.GetComponent<LoreBearer>();
            var result = TargetInfo(go);
            result["interactable"] = lore.SidescreenButtonInteractable();
            result["buttonText"] = lore.SidescreenButtonText;
            result["buttonTooltip"] = lore.SidescreenButtonTooltip;
            result["content"] = lore.content;
            result["sortOrder"] = lore.GetSideScreenSortOrder();
            return result;
        }


        private static Dictionary<string, object> TelepadInfo(Telepad telepad, bool includeVictory)
        {
            var result = TargetInfo(telepad.gameObject);
            var operational = telepad.GetComponent<Operational>();
            result["operational"] = operational != null && operational.IsOperational;
            result["gameOver"] = GameFlowManager.Instance != null && GameFlowManager.Instance.IsGameOver();
            result["immigrantsAvailable"] = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable;
            result["timeRemainingCycles"] = Immigration.Instance == null ? (object)null : Math.Round(ToolUtil.SafeFloat(telepad.GetTimeRemaining() / 600f), 3);
            result["timeRemainingSeconds"] = Immigration.Instance == null ? (object)null : Math.Round(ToolUtil.SafeFloat(telepad.GetTimeRemaining()), 1);
            result["researchScreenAvailable"] = ManagementMenu.Instance != null && ManagementMenu.Instance.CheckHasResearchCenter();
            result["hasActiveResearch"] = ManagementMenu.Instance != null && ManagementMenu.Instance.HasActiveResearch;
            result["skillPointsAvailable"] = Components.MinionResumes.Items.Any(resume => resume != null && !resume.HasTag(GameTags.Dead) && resume.TotalSkillPointsGained - resume.SkillsMastered > 0);
            result["newAchievementsQueued"] = SaveGame.Instance?.ColonyAchievementTracker?.achievementsToDisplay?.Count ?? 0;
            if (includeVictory)
                result["victoryConditions"] = VictoryConditionInfos();
            return result;
        }

        private static List<Dictionary<string, object>> VictoryConditionInfos()
        {
            var results = new List<Dictionary<string, object>>();
            if (Db.Get()?.ColonyAchievements?.resources == null)
                return results;
            foreach (var achievement in Db.Get().ColonyAchievements.resources)
            {
                if (!achievement.isVictoryCondition || achievement.Disabled || !achievement.IsValidForSave())
                    continue;
                results.Add(new Dictionary<string, object>
                {
                    ["id"] = achievement.Id,
                    ["name"] = achievement.Name,
                    ["success"] = achievement.requirementChecklist.All(req => req.Success()),
                    ["requirements"] = achievement.requirementChecklist
                        .Select(req => new Dictionary<string, object>
                        {
                            ["type"] = req.GetType().Name,
                            ["success"] = req.Success(),
                            ["progress"] = req.GetProgress(req.Success())
                        })
                        .ToList()
                });
            }
            return results;
        }
    }
}
