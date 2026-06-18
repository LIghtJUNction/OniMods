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
    public static class FacilitySideScreenTools
    {
        public static McpTool ListDispensers()
        {
            return new McpTool
            {
                Name = "dispensers_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "dispenser_side_screens_list", "pajama_dispensers_list" },
                Tags = new List<string> { "building", "dispenser", "side-screen", "pajamas" },
                Description = "列出 DispenserSideScreen / IDispenser 对象、当前选中物品、可分发物品和是否已有分发请求",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId、物品名或 itemId 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListBuildingTargets(args, go => go.GetComponent<IDispenser>() != null, go => DispenserInfo(go), "dispensers")
            };
        }

        public static McpTool ControlDispenser()
        {
            return new McpTool
            {
                Name = "dispenser_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "dispenser_order", "pajama_dispenser_control" },
                Tags = new List<string> { "building", "dispenser", "side-screen", "pajamas" },
                Description = "执行 DispenserSideScreen 操作：select_item 选择分发物品，order 创建分发请求，cancel 取消请求",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "select_item、order 或 cancel", Required = true, EnumValues = new List<string> { "select_item", "order", "cancel" } },
                    ["itemId"] = new McpToolParameter { Type = "string", Description = "action=select_item 时的目标 Tag/prefab id；可省略并用 itemIndex", Required = false },
                    ["itemIndex"] = new McpToolParameter { Type = "integer", Description = "action=select_item 时 DispensedItems() 的序号", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, target => target.GetComponent<IDispenser>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target dispenser not found");
                    var dispenser = go.GetComponent<IDispenser>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = DispenserInfo(go);

                    if (action == "select_item")
                    {
                        Tag item = ResolveDispensedItem(args, dispenser);
                        if (!item.IsValid)
                            return CallToolResult.Error("itemId or itemIndex must match an available dispensed item");
                        dispenser.SelectItem(item);
                    }
                    else if (action == "order")
                    {
                        if (!dispenser.HasOpenChore())
                            dispenser.OnOrderDispense();
                    }
                    else if (action == "cancel")
                    {
                        if (dispenser.HasOpenChore())
                            dispenser.OnCancelDispense();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be select_item, order, or cancel");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["action"] = action,
                        ["before"] = before,
                        ["dispenser"] = DispenserInfo(go)
                    });
                }
            };
        }

        public static McpTool ListSuitLockers()
        {
            return new McpTool
            {
                Name = "suit_lockers_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "suit_locker_side_screens_list", "atmo_suit_lockers_list" },
                Tags = new List<string> { "building", "suit", "checkpoint", "side-screen" },
                Description = "列出 SuitLockerSideScreen 状态：是否配置、是否请求太空服、存储装备、氧气/电量和可执行操作",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId、服装类型或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListBuildingTargets(args, go => go.GetComponent<SuitLocker>() != null, go => SuitLockerInfo(go.GetComponent<SuitLocker>()), "lockers")
            };
        }

        public static McpTool ControlSuitLocker()
        {
            return new McpTool
            {
                Name = "suit_locker_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "suit_locker_config", "suit_locker_drop_suit" },
                Tags = new List<string> { "building", "suit", "checkpoint", "side-screen" },
                Description = "执行 SuitLockerSideScreen 操作：request_suit 请求送服，no_suit 设为无需服装，drop_suit 掉出已存装备",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "request_suit、no_suit 或 drop_suit", Required = true, EnumValues = new List<string> { "request_suit", "no_suit", "drop_suit" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=drop_suit 必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, target => target.GetComponent<SuitLocker>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target SuitLocker not found");
                    var locker = go.GetComponent<SuitLocker>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = SuitLockerInfo(locker);

                    if (action == "request_suit")
                    {
                        locker.ConfigRequestSuit();
                    }
                    else if (action == "no_suit")
                    {
                        locker.ConfigNoSuit();
                    }
                    else if (action == "drop_suit")
                    {
                        if (!ToolUtil.GetBool(args, "confirm", false))
                            return CallToolResult.Error("confirm=true is required to drop a stored suit");
                        if (locker.GetStoredOutfit() == null)
                            return CallToolResult.Error("SuitLocker has no stored suit to drop");
                        locker.DropSuit();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be request_suit, no_suit, or drop_suit");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["action"] = action,
                        ["before"] = before,
                        ["locker"] = SuitLockerInfo(locker)
                    });
                }
            };
        }

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
                Description = "列出 LoreBearerSideScreen 可阅读/已阅读对象、按钮文本、tooltip 和排序",
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
                Description = "按下 LoreBearerSideScreen 的阅读/检查按钮；会打开弹窗并可能产生 databank，需 confirm=true",
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
                Description = "列出 TelepadSideScreen 状态：移民是否可用、剩余时间、传送门运行状态、研究/技能提示和胜利条件进度",
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
                Description = "执行 TelepadSideScreen 导航按钮：open_immigrants、open_colony_summary、open_skills、open_research",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "open_immigrants、open_colony_summary、open_skills 或 open_research", Required = true, EnumValues = new List<string> { "open_immigrants", "open_colony_summary", "open_skills", "open_research" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认打开游戏 UI", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for telepad UI actions");
                    var go = FindBuildingTarget(args, target => target.GetComponent<Telepad>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target Telepad not found");
                    var telepad = go.GetComponent<Telepad>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = TelepadInfo(telepad, includeVictory: false);

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

        public static McpTool ListBionicUpgrades()
        {
            return new McpTool
            {
                Name = "bionic_upgrades_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "bionic_slots_list", "bionic_side_screen_list" },
                Tags = new List<string> { "dupe", "bionic", "upgrade", "assignable", "side-screen" },
                Description = "列出 BionicSideScreen 升级槽：锁定/空/已分配/已安装状态、升级组件和功耗；槽位分配/清空使用 assignable_slot_item_set",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "可选仿生复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "可选仿生复制人名称", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按复制人、升级名、prefabId 或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    MinionIdentity specific = ToolUtil.FindDupe(args);
                    var dupes = Components.LiveMinionIdentities.Items
                        .Where(minion => minion != null && minion.GetSMI<BionicUpgradesMonitor.Instance>() != null)
                        .Where(minion => specific == null || minion == specific)
                        .Select(BionicInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = dupes.Count,
                        ["assignmentTool"] = "assignable_slot_item_set",
                        ["bionics"] = dupes
                    });
                }
            };
        }

        public static McpTool ListMinionTodos()
        {
            return new McpTool
            {
                Name = "minion_todos_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "dupe_todos_list", "minion_chore_queue_list" },
                Tags = new List<string> { "dupe", "todo", "chore", "priority", "side-screen" },
                Description = "读取 MinionTodoSideScreen 数据：当前差事、可执行差事、阻塞差事、优先级和目标位置",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "可选复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "可选复制人名称", Required = false },
                    ["includeBlocked"] = new McpToolParameter { Type = "boolean", Description = "是否包含失败/阻塞差事，默认 true", Required = false },
                    ["includePotentialOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回 IsPotentialSuccess 的阻塞差事，默认 true", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按复制人、差事、目标、组或阻塞原因筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回复制人数，默认 50，最大 200", Required = false },
                    ["taskLimit"] = new McpToolParameter { Type = "integer", Description = "每个复制人最多返回差事数，默认 30，最大 100", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    MinionIdentity specific = ToolUtil.FindDupe(args);
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 50, 200);
                    int taskLimit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "taskLimit") ?? 30, 100));
                    bool includeBlocked = ToolUtil.GetBool(args, "includeBlocked", true);
                    bool potentialOnly = ToolUtil.GetBool(args, "includePotentialOnly", true);
                    var dupes = Components.LiveMinionIdentities.Items
                        .Where(minion => minion != null && !minion.HasTag(GameTags.Dead))
                        .Where(minion => specific == null || minion == specific)
                        .Select(minion => MinionTodoInfo(minion, includeBlocked, potentialOnly, taskLimit))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = dupes.Count,
                        ["dupes"] = dupes
                    });
                }
            };
        }

        public static McpTool ListArtifacts()
        {
            return new McpTool
            {
                Name = "artifacts_list",
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "analyzed_artifacts_list", "artifact_analysis_list" },
                Tags = new List<string> { "story", "artifact", "analysis", "space" },
                Description = "列出 ArtifactAnalysisSideScreen 数据：已分析 artifact、场上 artifact、分析站存储/可工作状态",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 artifact id、名称、类型或分析站筛选", Required = false },
                    ["includeStations"] = new McpToolParameter { Type = "boolean", Description = "是否包含分析站状态，默认 true", Required = false },
                    ["includeWorldArtifacts"] = new McpToolParameter { Type = "boolean", Description = "是否包含场上 artifact 实例，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回 artifact 数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var analyzed = AnalyzedArtifactInfos()
                        .Where(info => MatchesQuery(info, query))
                        .Take(limit)
                        .ToList();
                    var payload = new Dictionary<string, object>
                    {
                        ["analyzedCount"] = ArtifactSelector.Instance?.AnalyzedArtifactCount ?? 0,
                        ["analyzedSpaceCount"] = ArtifactSelector.Instance?.AnalyzedSpaceArtifactCount ?? 0,
                        ["analyzedArtifacts"] = analyzed
                    };
                    if (ToolUtil.GetBool(args, "includeWorldArtifacts", true))
                        payload["worldArtifacts"] = WorldArtifactInfos(args).Where(info => MatchesQuery(info, query)).Take(limit).ToList();
                    if (ToolUtil.GetBool(args, "includeStations", true))
                        payload["stations"] = BuildingTargets(args, go => go.GetSMI<ArtifactAnalysisStation.StatesInstance>() != null, go => ArtifactStationInfo(go.GetSMI<ArtifactAnalysisStation.StatesInstance>()))
                            .Where(info => MatchesQuery(info, query))
                            .Take(limit)
                            .ToList();
                    return JsonResult(payload);
                }
            };
        }

        public static McpTool OpenArtifactReveal()
        {
            return new McpTool
            {
                Name = "artifact_reveal_open",
                Group = "story",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "artifact_lore_open", "artifact_analysis_reveal_open" },
                Tags = new List<string> { "story", "artifact", "analysis", "popup" },
                Description = "打开 ArtifactAnalysisSideScreen 已分析 artifact 的 reveal/lore 弹窗",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["artifactId"] = new McpToolParameter { Type = "string", Description = "已分析 artifact prefab id，例如 artifact_officemug", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认打开游戏内弹窗", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to open an artifact reveal popup");
                    string artifactId = args["artifactId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(artifactId))
                        return CallToolResult.Error("artifactId is required");
                    if (ArtifactSelector.Instance == null || !ArtifactSelector.Instance.GetAnalyzedArtifactIDs().Contains(artifactId))
                        return CallToolResult.Error("artifactId has not been analyzed in this save");
                    var prefab = Assets.GetPrefab(artifactId);
                    if (prefab == null)
                        return CallToolResult.Error("artifactId does not resolve to an artifact prefab");
                    var before = ArtifactInfo(artifactId);
                    var evt = GameplayEventManager.Instance.StartNewEvent(Db.Get().GameplayEvents.ArtifactReveal);
                    var statesInstance = evt.smi as SimpleEvent.StatesInstance;
                    if (statesInstance == null)
                        return CallToolResult.Error("ArtifactReveal event did not return a SimpleEvent instance");
                    statesInstance.artifact = prefab;
                    string text = prefab.PrefabID().Name.ToUpperInvariant().Replace("ARTIFACT_", "");
                    string key = "STRINGS.UI.SPACEARTIFACTS." + text + ".ARTIFACT";
                    string desc = $"<b>{prefab.GetProperName()}</b>";
                    if (Strings.TryGet(key, out var result) && result != null && !result.String.IsNullOrWhiteSpace())
                        desc = desc + "\n\n" + result.String;
                    if (!desc.IsNullOrWhiteSpace())
                        statesInstance.SetTextParameter("desc", desc);
                    statesInstance.ShowEventPopup();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["artifact"] = before,
                        ["opened"] = true
                    });
                }
            };
        }

        private static Dictionary<string, object> DispenserInfo(GameObject go)
        {
            var dispenser = go.GetComponent<IDispenser>();
            var result = TargetInfo(go);
            Tag selected = dispenser.SelectedItem();
            result["selectedItemId"] = selected.IsValid ? selected.Name : null;
            result["hasOpenChore"] = dispenser.HasOpenChore();
            result["items"] = dispenser.DispensedItems().Select((tag, index) => ItemInfo(tag, index, tag == selected)).ToList();
            return result;
        }

        private static Dictionary<string, object> ItemInfo(Tag tag, int index, bool selected)
        {
            var prefab = Assets.GetPrefab(tag);
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["itemId"] = tag.Name,
                ["name"] = prefab != null ? ToolUtil.CleanName(prefab.GetProperName()) : tag.ProperName(),
                ["selected"] = selected
            };
        }

        private static Tag ResolveDispensedItem(JObject args, IDispenser dispenser)
        {
            var items = dispenser.DispensedItems();
            int? index = ToolUtil.GetInt(args, "itemIndex");
            if (index.HasValue && index.Value >= 0 && index.Value < items.Count)
                return items[index.Value];
            string itemId = args["itemId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(itemId))
            {
                foreach (var item in items)
                {
                    if (string.Equals(item.Name, itemId.Trim(), StringComparison.OrdinalIgnoreCase))
                        return item;
                }
            }
            return Tag.Invalid;
        }

        private static Dictionary<string, object> SuitLockerInfo(SuitLocker locker)
        {
            var result = TargetInfo(locker.gameObject);
            var smi = locker.smi;
            var stored = locker.GetStoredOutfit();
            result["configured"] = smi != null && smi.sm.isConfigured.Get(smi);
            result["waitingForSuit"] = smi != null && smi.sm.isWaitingForSuit.Get(smi);
            result["hasStoredSuit"] = stored != null;
            result["storedSuit"] = stored == null ? null : StoredSuitInfo(stored);
            result["outfitTags"] = locker.OutfitTags?.Select(tag => tag.Name).ToList() ?? new List<string>();
            result["oxygenAvailable"] = stored == null ? null : TankPercent(stored.GetComponent<SuitTank>());
            result["batteryAvailable"] = stored == null ? null : TankPercent(stored.GetComponent<LeadSuitTank>());
            result["canDropSuit"] = stored != null;
            result["canRequestSuit"] = stored == null;
            result["canSetNoSuit"] = true;
            return result;
        }

        private static Dictionary<string, object> StoredSuitInfo(KPrefabID suit)
        {
            var tank = suit.GetComponent<SuitTank>();
            var jetTank = suit.GetComponent<JetSuitTank>();
            var leadTank = suit.GetComponent<LeadSuitTank>();
            return new Dictionary<string, object>
            {
                ["id"] = suit.InstanceID,
                ["prefabId"] = suit.PrefabTag.Name,
                ["name"] = ToolUtil.CleanName(suit.GetProperName()),
                ["oxygen"] = TankPercent(tank),
                ["jetSuitFuel"] = TankPercent(jetTank),
                ["battery"] = TankPercent(leadTank)
            };
        }

        private static object TankPercent(SuitTank tank)
        {
            return tank == null ? null : (object)Math.Round(ToolUtil.SafeFloat(tank.PercentFull()), 4);
        }

        private static object TankPercent(JetSuitTank tank)
        {
            return tank == null ? null : (object)Math.Round(ToolUtil.SafeFloat(tank.PercentFull()), 4);
        }

        private static object TankPercent(LeadSuitTank tank)
        {
            return tank == null ? null : (object)Math.Round(ToolUtil.SafeFloat(tank.PercentFull()), 4);
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

        private static Dictionary<string, object> BionicInfo(MinionIdentity minion)
        {
            var monitor = minion.GetSMI<BionicUpgradesMonitor.Instance>();
            var result = TargetInfo(minion.gameObject);
            result["online"] = monitor.IsOnline;
            result["unlockedSlotCount"] = monitor.UnlockedSlotCount;
            result["assignedSlotCount"] = monitor.AssignedSlotCount;
            result["hasAnyUpgradeAssigned"] = monitor.HasAnyUpgradeAssigned;
            result["hasAnyUpgradeInstalled"] = monitor.HasAnyUpgradeInstalled;
            result["slots"] = (monitor.upgradeComponentSlots ?? new BionicUpgradesMonitor.UpgradeComponentSlot[0])
                .Select((slot, index) => BionicSlotInfo(slot, index))
                .ToList();
            return result;
        }

        private static Dictionary<string, object> BionicSlotInfo(BionicUpgradesMonitor.UpgradeComponentSlot slot, int index)
        {
            var assigned = slot?.assignedUpgradeComponent;
            var installed = slot?.installedUpgradeComponent;
            string state = "empty";
            if (slot == null)
                state = "missing";
            else if (slot.IsLocked)
                state = "locked";
            else if (slot.HasUpgradeInstalled)
                state = "installed";
            else if (slot.HasUpgradeComponentAssigned && !slot.GetAssignableSlotInstance().IsUnassigning())
                state = "assigned";
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["state"] = state,
                ["locked"] = slot == null || slot.IsLocked,
                ["assignableSlotId"] = slot?.GetAssignableSlotInstance()?.ID,
                ["assignedUpgrade"] = assigned == null ? null : BionicUpgradeInfo(assigned),
                ["installedUpgrade"] = installed == null ? null : BionicUpgradeInfo(installed),
                ["assignedMatchesInstalled"] = slot != null && slot.AssignedUpgradeMatchesInstalledUpgrade,
                ["wattageCost"] = slot == null ? (object)null : Math.Round(ToolUtil.SafeFloat(slot.WattageCost), 3)
            };
        }

        private static Dictionary<string, object> BionicUpgradeInfo(BionicUpgradeComponent upgrade)
        {
            return new Dictionary<string, object>
            {
                ["id"] = upgrade.GetComponent<KPrefabID>()?.InstanceID ?? upgrade.gameObject.GetInstanceID(),
                ["prefabId"] = upgrade.PrefabID().Name,
                ["name"] = ToolUtil.CleanName(upgrade.GetProperName()),
                ["boosterType"] = upgrade.Booster.ToString(),
                ["currentWattage"] = Math.Round(ToolUtil.SafeFloat(upgrade.CurrentWattage), 3),
                ["potentialWattage"] = Math.Round(ToolUtil.SafeFloat(upgrade.PotentialWattage), 3),
                ["assignee"] = upgrade.assignee == null ? null : ToolUtil.CleanName(upgrade.assignee.GetProperName())
            };
        }

        private static Dictionary<string, object> MinionTodoInfo(MinionIdentity minion, bool includeBlocked, bool potentialOnly, int taskLimit)
        {
            var consumer = minion.GetComponent<ChoreConsumer>();
            var result = TargetInfo(minion.gameObject);
            var schedulable = minion.GetComponent<Schedulable>();
            var scheduleBlock = schedulable?.GetSchedule()?.GetCurrentScheduleBlock();
            result["currentScheduleBlock"] = scheduleBlock?.name;
            if (consumer == null)
            {
                result["current"] = null;
                result["tasks"] = new List<Dictionary<string, object>>();
                result["blocked"] = new List<Dictionary<string, object>>();
                result["note"] = "No ChoreConsumer on target";
                return result;
            }

            var snapshot = consumer.GetLastPreconditionSnapshot();
            if (snapshot.doFailedContextsNeedSorting)
            {
                snapshot.failedContexts.Sort();
                snapshot.doFailedContextsNeedSorting = false;
            }

            var succeeded = snapshot.succeededContexts
                .Where(ctx => ctx.chore != null)
                .OrderByDescending(ChoreSortScore)
                .Select(ctx => ChoreContextInfo(ctx, consumer, success: true))
                .Take(taskLimit)
                .ToList();
            var blocked = includeBlocked
                ? snapshot.failedContexts
                    .Where(ctx => ctx.chore != null && (!potentialOnly || ctx.IsPotentialSuccess()))
                    .Select(ctx => ChoreContextInfo(ctx, consumer, success: false))
                    .Take(taskLimit)
                    .ToList()
                : new List<Dictionary<string, object>>();

            result["current"] = CurrentChoreInfo(consumer);
            result["tasks"] = succeeded;
            result["blocked"] = blocked;
            result["counts"] = new Dictionary<string, object>
            {
                ["succeeded"] = snapshot.succeededContexts.Count,
                ["blocked"] = snapshot.failedContexts.Count,
                ["returnedTasks"] = succeeded.Count,
                ["returnedBlocked"] = blocked.Count
            };
            return result;
        }

        private static Dictionary<string, object> CurrentChoreInfo(ChoreConsumer consumer)
        {
            var driver = consumer.choreDriver;
            var current = driver?.GetCurrentChore();
            if (current == null)
                return null;
            return ChoreInfo(current, consumer, null);
        }

        private static Dictionary<string, object> ChoreContextInfo(Chore.Precondition.Context context, ChoreConsumer consumer, bool success)
        {
            var info = ChoreInfo(context.chore, consumer, context.data);
            info["success"] = success;
            info["potentialSuccess"] = context.IsPotentialSuccess();
            info["personalPriority"] = context.personalPriority;
            info["typePriority"] = context.priority;
            info["priorityMod"] = context.priorityMod;
            info["consumerPriority"] = context.consumerPriority;
            info["cost"] = context.cost;
            info["failedPrecondition"] = FailedPreconditionInfo(context);
            return info;
        }

        private static int ChoreSortScore(Chore.Precondition.Context context)
        {
            return ((int)context.masterPriority.priority_class * 100000)
                + (context.personalPriority * 10000)
                + (context.masterPriority.priority_value * 1000)
                + context.priority
                + context.priorityMod
                + context.consumerPriority
                - context.cost;
        }

        private static Dictionary<string, object> ChoreInfo(Chore chore, ChoreConsumer consumer, object data)
        {
            var target = chore.target?.gameObject;
            var choreGameObject = chore.gameObject;
            var priority = chore.masterPriority;
            return new Dictionary<string, object>
            {
                ["name"] = SafeChoreName(chore, data),
                ["reportName"] = Safe(() => chore.GetReportName(), chore.GetType().Name),
                ["type"] = chore.choreType?.Id ?? chore.GetType().Name,
                ["groups"] = chore.choreType?.groups?.Select(group => group.Id).ToList() ?? new List<string>(),
                ["bestGroup"] = BestChoreGroup(chore, consumer)?.Id,
                ["priorityClass"] = priority.priority_class.ToString(),
                ["priorityValue"] = priority.priority_value,
                ["isCurrent"] = chore.driver == consumer.choreDriver,
                ["target"] = target == null ? null : TargetInfo(target),
                ["provider"] = choreGameObject == null ? null : TargetInfo(choreGameObject)
            };
        }

        private static string SafeChoreName(Chore chore, object data)
        {
            return Safe(() => GameUtil.GetChoreName(chore, data), Safe(() => chore.GetReportName(), chore.GetType().Name));
        }

        private static ChoreGroup BestChoreGroup(Chore chore, ChoreConsumer consumer)
        {
            ChoreGroup best = null;
            var groups = chore.choreType?.groups;
            if (groups == null || groups.Length == 0)
                return null;
            foreach (var group in groups)
            {
                if (best == null || consumer.GetPersonalPriority(best) < consumer.GetPersonalPriority(group))
                    best = group;
            }
            return best;
        }

        private static Dictionary<string, object> FailedPreconditionInfo(Chore.Precondition.Context context)
        {
            if (context.failedPreconditionId < 0)
                return null;
            var preconditions = context.chore.GetPreconditions();
            if (context.failedPreconditionId >= preconditions.Count)
            {
                return new Dictionary<string, object>
                {
                    ["index"] = context.failedPreconditionId,
                    ["id"] = "out_of_range"
                };
            }
            var precondition = preconditions[context.failedPreconditionId].condition;
            return new Dictionary<string, object>
            {
                ["index"] = context.failedPreconditionId,
                ["id"] = precondition.id,
                ["description"] = precondition.description
            };
        }

        private static T Safe<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch
            {
                return fallback;
            }
        }

        private static List<Dictionary<string, object>> AnalyzedArtifactInfos()
        {
            if (ArtifactSelector.Instance == null)
                return new List<Dictionary<string, object>>();
            return ArtifactSelector.Instance.GetAnalyzedArtifactIDs()
                .Select(ArtifactInfo)
                .Where(info => info != null)
                .OrderBy(info => info["name"].ToString())
                .ToList();
        }

        private static Dictionary<string, object> ArtifactInfo(string artifactId)
        {
            var prefab = Assets.GetPrefab(artifactId);
            if (prefab == null)
            {
                return new Dictionary<string, object>
                {
                    ["artifactId"] = artifactId,
                    ["name"] = artifactId,
                    ["prefabAvailable"] = false
                };
            }
            var artifact = prefab.GetComponent<SpaceArtifact>();
            return new Dictionary<string, object>
            {
                ["artifactId"] = artifactId,
                ["name"] = ToolUtil.CleanName(prefab.GetProperName()),
                ["prefabAvailable"] = true,
                ["type"] = artifact == null ? "unknown" : artifact.artifactType.ToString(),
                ["tier"] = artifact == null || artifact.artifactTier == null ? null : artifact.artifactTier.name_key.ToString(),
                ["payloadDropChance"] = artifact == null || artifact.artifactTier == null ? (object)null : Math.Round(ToolUtil.SafeFloat(artifact.artifactTier.payloadDropChance), 4)
            };
        }

        private static IEnumerable<Dictionary<string, object>> WorldArtifactInfos(JObject args)
        {
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            foreach (var artifact in Components.SpaceArtifacts.Items)
            {
                if (artifact == null || !MatchesTarget(artifact.gameObject, rect, worldId))
                    continue;
                var info = TargetInfo(artifact.gameObject);
                info["artifactType"] = artifact.artifactType.ToString();
                info["charmed"] = artifact.gameObject.HasTag(GameTags.CharmedArtifact);
                info["analyzed"] = ArtifactSelector.Instance != null && ArtifactSelector.Instance.GetAnalyzedArtifactIDs().Contains(info["prefabId"].ToString());
                info["payloadDropChance"] = artifact.artifactTier == null ? (object)null : Math.Round(ToolUtil.SafeFloat(artifact.artifactTier.payloadDropChance), 4);
                yield return info;
            }
        }

        private static Dictionary<string, object> ArtifactStationInfo(ArtifactAnalysisStation.StatesInstance station)
        {
            var result = TargetInfo(station.gameObject);
            result["operational"] = station.GetComponent<Operational>()?.IsOperational ?? false;
            result["storedCharmedArtifactKg"] = Math.Round(ToolUtil.SafeFloat(station.storage.GetMassAvailable(GameTags.CharmedArtifact)), 3);
            result["storedItems"] = station.storage.GetItems().Select(StorageItemInfo).ToList();
            return result;
        }

        private static Dictionary<string, object> StorageItemInfo(GameObject item)
        {
            var kpid = item?.GetComponent<KPrefabID>();
            var primary = item?.GetComponent<PrimaryElement>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? item?.GetInstanceID() ?? -1,
                ["prefabId"] = kpid?.PrefabTag.Name ?? item?.name,
                ["name"] = item == null ? null : ToolUtil.CleanName(item.GetProperName()),
                ["massKg"] = Math.Round(ToolUtil.SafeFloat(primary?.Mass ?? 0f), 3),
                ["charmedArtifact"] = item != null && item.HasTag(GameTags.CharmedArtifact)
            };
        }

        private static CallToolResult ListBuildingTargets(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
        {
            var items = BuildingTargets(args, predicate, selector).ToList();
            int worldId = HasRectInput(args) || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            return JsonResult(new Dictionary<string, object>
            {
                ["returned"] = items.Count,
                ["worldId"] = worldId >= 0 ? (object)worldId : null,
                [payloadKey] = items
            });
        }

        private static IEnumerable<Dictionary<string, object>> BuildingTargets(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector)
        {
            if (Game.Instance == null)
                return Enumerable.Empty<Dictionary<string, object>>();
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["query"]?.ToString();
            int limit = ToolUtil.ClampLimit(args, 100, 500);
            return Components.BuildingCompletes.Items
                .Select(building => building?.gameObject)
                .Where(go => MatchesTarget(go, rect, worldId))
                .Where(predicate)
                .Select(selector)
                .Where(info => MatchesQuery(info, query))
                .OrderBy(info => info["name"].ToString())
                .Take(limit);
        }

        private static CallToolResult ListObjectTargets(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["query"]?.ToString();
            int limit = ToolUtil.ClampLimit(args, 100, 500);
            var items = AllCandidateObjects()
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

        private static IEnumerable<GameObject> AllCandidateObjects()
        {
            var seen = new HashSet<int>();
            foreach (var kpid in UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsSortMode.None))
            {
                if (kpid == null || kpid.gameObject == null)
                    continue;
                int id = kpid.gameObject.GetInstanceID();
                if (seen.Add(id))
                    yield return kpid.gameObject;
            }
        }

        private static GameObject FindBuildingTarget(JObject args, Func<GameObject, bool> predicate)
        {
            return FindTarget(args, Components.BuildingCompletes.Items.Select(building => building?.gameObject), predicate);
        }

        private static GameObject FindObjectTarget(JObject args, Func<GameObject, bool> predicate)
        {
            return FindTarget(args, AllCandidateObjects(), predicate);
        }

        private static GameObject FindTarget(JObject args, IEnumerable<GameObject> candidates, Func<GameObject, bool> predicate)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var go in candidates)
            {
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

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标对象格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标对象格子 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
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
