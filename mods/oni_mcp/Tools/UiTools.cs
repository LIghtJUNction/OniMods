using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class UiTools
    {
        private static readonly Dictionary<string, global::Action> ManagementScreens = new Dictionary<string, global::Action>(StringComparer.OrdinalIgnoreCase)
        {
            ["vitals"] = global::Action.ManageVitals,
            ["consumables"] = global::Action.ManageConsumables,
            ["priorities"] = global::Action.ManagePriorities,
            ["schedule"] = global::Action.ManageSchedule,
            ["skills"] = global::Action.ManageSkills,
            ["research"] = global::Action.ManageResearch,
            ["starmap"] = global::Action.ManageStarmap,
            ["report"] = global::Action.ManageReport,
            ["database"] = global::Action.ManageDatabase,
            ["codex"] = global::Action.ManageDatabase
        };

        public static McpTool ListUiActions()
        {
            return new McpTool
            {
                Name = "ui_actions_list",
                Hidden = true,
                Group = "ui",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "ui_management_list", "ui_hotkeys_list" },
                Tags = new List<string> { "ui", "management", "screen", "hotkey", "action" },
                Description = "兼容入口：请使用 game_control domain=ui uiDomain=action action=list。列出可由 MCP 安全打开/触发的 UI 页面和 Action，包括管理菜单、覆盖层、建造分类和取消/关闭",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "过滤类型：all、management、overlay、build、navigation，默认 all", Required = false, EnumValues = new List<string> { "all", "management", "overlay", "build", "navigation" } }
                },
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "all").Trim().ToLowerInvariant();
                    var actions = SafeActions()
                        .Select(item => ActionInfo(item.Key, item.Value))
                        .Where(item => kind == "all" || (string)item["kind"] == kind)
                        .OrderBy(item => item["kind"].ToString())
                        .ThenBy(item => item["name"].ToString())
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = actions.Count,
                        ["managementScreens"] = ManagementScreens.Keys.OrderBy(name => name).ToList(),
                        ["actions"] = actions
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool OpenManagementScreen()
        {
            return new McpTool
            {
                Name = "ui_management_open",
                Hidden = true,
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "management_open", "ui_open_screen" },
                Tags = new List<string> { "ui", "management", "screen", "codex", "research", "skills" },
                Description = "兼容入口：请使用 game_control domain=ui uiDomain=action action=open_management。打开指定管理页面：vitals、consumables、priorities、schedule、skills、research、starmap、report、database/codex",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["screen"] = new McpToolParameter { Type = "string", Description = "页面名", Required = true, EnumValues = ManagementScreens.Keys.OrderBy(name => name).ToList() },
                    ["codexId"] = new McpToolParameter { Type = "string", Description = "screen=database/codex 时可直接打开指定 Codex entry id", Required = false },
                    ["researchId"] = new McpToolParameter { Type = "string", Description = "screen=research 时可缩放到指定 tech id", Required = false },
                    ["reportDay"] = new McpToolParameter { Type = "integer", Description = "screen=report 时打开指定周期报告；默认当前周期", Required = false }
                },
                Handler = args =>
                {
                    var menu = ManagementMenu.Instance;
                    if (menu == null)
                        return CallToolResult.Error("ManagementMenu is not available");

                    string screen = (args["screen"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (!ManagementScreens.TryGetValue(screen, out var action))
                        return CallToolResult.Error("Unknown management screen");

                    if ((screen == "database" || screen == "codex") && !string.IsNullOrWhiteSpace(args["codexId"]?.ToString()))
                        menu.OpenCodexToEntry(args["codexId"].ToString().Trim());
                    else if (screen == "research")
                        menu.OpenResearch(string.IsNullOrWhiteSpace(args["researchId"]?.ToString()) ? null : args["researchId"].ToString().Trim());
                    else if (screen == "skills")
                        menu.ToggleSkills();
                    else if (screen == "starmap")
                        OpenStarmap(menu);
                    else if (screen == "priorities")
                        menu.TogglePriorities();
                    else if (screen == "report")
                        menu.OpenReports(ToolUtil.GetInt(args, "reportDay") ?? GameUtil.GetCurrentCycle());
                    else if (screen == "database" || screen == "codex")
                        menu.ToggleCodex();
                    else
                        DispatchUiAction(action);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["opened"] = screen,
                        ["action"] = action.ToString()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool TriggerUiAction()
        {
            return new McpTool
            {
                Name = "ui_action_trigger",
                Hidden = true,
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "ui_hotkey_trigger", "game_action_trigger" },
                Tags = new List<string> { "ui", "hotkey", "action", "management", "build", "overlay" },
                Description = "兼容入口：请使用 game_control domain=ui uiDomain=action action=trigger。触发白名单内的安全 UI Action，用于打开/关闭界面、覆盖层和建造分类；不会触发 debug 或破坏性游戏操作",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "Action 枚举名，如 ManageVitals、Overlay1、BuildCategoryTiles、Escape", Required = true }
                },
                Handler = args =>
                {
                    string name = args["action"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        return CallToolResult.Error("action is required");
                    var safe = SafeActions();
                    var match = safe.Keys.FirstOrDefault(action => string.Equals(action.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (!safe.ContainsKey(match))
                        return CallToolResult.Error("Action is not in the safe UI whitelist");

                    DispatchUiAction(match);
                    return CallToolResult.Text(JsonConvert.SerializeObject(ActionInfo(match, safe[match]), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlUiAction()
        {
            return new McpTool
            {
                Name = "ui_action_control",
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "ui_control", "ui_hotkey_control", "game_action_control" },
                Tags = new List<string> { "ui", "hotkey", "action", "management", "build", "overlay" },
                Description = "UI Action 聚合工具：action=list/trigger/open_management；读取安全 UI Action 白名单、触发白名单内动作或打开管理页面。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、trigger 或 open_management", Required = true, EnumValues = new List<string> { "list", "trigger", "open_management" } },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "action=list 时过滤类型：all、management、overlay、build、navigation，默认 all", Required = false, EnumValues = new List<string> { "all", "management", "overlay", "build", "navigation" } },
                    ["uiAction"] = new McpToolParameter { Type = "string", Description = "action=trigger 时的 Action 枚举名，如 ManageVitals、Overlay1、BuildCategoryTiles、Escape", Required = false },
                    ["screen"] = new McpToolParameter { Type = "string", Description = "action=open_management 时的页面名", Required = false, EnumValues = ManagementScreens.Keys.OrderBy(name => name).ToList() },
                    ["codexId"] = new McpToolParameter { Type = "string", Description = "action=open_management screen=database/codex 时可直接打开指定 Codex entry id", Required = false },
                    ["researchId"] = new McpToolParameter { Type = "string", Description = "action=open_management screen=research 时可缩放到指定 tech id", Required = false },
                    ["reportDay"] = new McpToolParameter { Type = "integer", Description = "action=open_management screen=report 时打开指定周期报告；默认当前周期", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListUiActions().Handler(args);
                    if (action == "trigger")
                    {
                        var forwarded = (Newtonsoft.Json.Linq.JObject)args.DeepClone();
                        forwarded["action"] = args["uiAction"] ?? args["ui_action"] ?? args["name"];
                        return TriggerUiAction().Handler(forwarded);
                    }
                    if (action == "open_management")
                        return OpenManagementScreen().Handler(args);
                    return CallToolResult.Error("action must be list, trigger, or open_management");
                }
            };
        }

        private static Dictionary<global::Action, string> SafeActions()
        {
            var result = new Dictionary<global::Action, string>();
            foreach (var item in ManagementScreens.Values.Distinct())
                result[item] = "management";
            AddRange(result, "overlay", "Overlay", 1, 15);
            AddRange(result, "build", "BuildCategory", new[]
            {
                "Ladders", "Tiles", "Doors", "Storage", "Generators", "Wires", "PowerControl",
                "PlumbingStructures", "Pipes", "VentilationStructures", "Tubes", "TravelTubes",
                "Conveyance", "LogicWiring", "LogicGates", "LogicSwitches", "LogicConduits",
                "Cooking", "Farming", "Ranching", "Research", "Hygiene", "Medical", "Recreation",
                "Furniture", "Decor", "Oxygen", "Utilities", "Refining", "Equipment", "Rocketry"
            });
            AddRange(result, "build", "BuildMenuKey", Enumerable.Range('A', 26).Select(code => ((char)code).ToString()).ToArray());
            result[global::Action.Escape] = "navigation";
            result[global::Action.Find] = "navigation";
            result[global::Action.Help] = "navigation";
            result[global::Action.ToggleScreenshotMode] = "navigation";
            return result;
        }

        private static void AddRange(Dictionary<global::Action, string> actions, string kind, string prefix, int from, int to)
        {
            for (int i = from; i <= to; i++)
            {
                if (Enum.TryParse(prefix + i, out global::Action action))
                    actions[action] = kind;
            }
        }

        private static void AddRange(Dictionary<global::Action, string> actions, string kind, string prefix, IEnumerable<string> suffixes)
        {
            foreach (string suffix in suffixes)
            {
                if (Enum.TryParse(prefix + suffix, out global::Action action))
                    actions[action] = kind;
            }
        }

        private static Dictionary<string, object> ActionInfo(global::Action action, string kind)
        {
            return new Dictionary<string, object>
            {
                ["name"] = action.ToString(),
                ["kind"] = kind,
                ["ordinal"] = (int)action,
                ["available"] = IsAvailable(action)
            };
        }

        private static bool IsAvailable(global::Action action)
        {
            var menu = ManagementMenu.Instance;
            if (menu == null)
                return false;
            if (action == global::Action.ManageResearch)
                return menu.CheckHasResearchCenter();
            if (action == global::Action.ManageSkills)
                return Components.RoleStations.Count > 0 || DebugHandler.InstantBuildMode || (Game.Instance != null && Game.Instance.SandboxModeActive);
            if (action == global::Action.ManageStarmap)
                return ManagementMenu.StarmapAvailable() || DlcManager.FeatureClusterSpaceEnabled();
            return true;
        }

        private static void OpenStarmap(ManagementMenu menu)
        {
            if (DlcManager.FeatureClusterSpaceEnabled())
                menu.OpenClusterMap();
            else
                menu.OpenStarmap();
        }

        private static void DispatchUiAction(global::Action action)
        {
            var manager = KScreenManager.Instance;
            if (manager == null)
                return;
            var down = new KButtonEvent(null, InputEventType.KeyDown, action);
            manager.OnKeyDown(down);
            var up = new KButtonEvent(null, InputEventType.KeyUp, action);
            manager.OnKeyUp(up);
        }
    }
}
