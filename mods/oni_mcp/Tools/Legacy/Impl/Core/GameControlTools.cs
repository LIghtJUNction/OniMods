using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using STRINGS;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    /// <summary>
    /// 游戏控制相关 MCP Tools
    /// </summary>
    public static class GameControlTools
    {
        public static McpTool GetGameTime()
        {
            return new McpTool
            {
                Name = "game_time",
                Group = "game",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_game_time" },
                Hidden = true,
                Description = "兼容旧工具：请改用 game_control domain=speed action=time",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var cycle = GameUtil.GetCurrentCycle();
                    var timeOfDay = GameClock.Instance?.GetCurrentCycleAsPercentage() ?? 0f;

                    var info = new Dictionary<string, object>
                    {
                        ["cycle"] = cycle,
                        ["timeOfDayPercent"] = Math.Round(timeOfDay * 100, 1),
                        ["timeScale"] = Time.timeScale,
                        ["isPaused"] = SpeedControlScreen.Instance != null ? SpeedControlScreen.Instance.IsPaused : Time.timeScale == 0f,
                        ["speed"] = SpeedControlScreen.Instance != null ? SpeedControlScreen.Instance.GetSpeed() + 1 : 0,
                        ["internalSpeed"] = SpeedControlScreen.Instance != null ? SpeedControlScreen.Instance.GetSpeed() : -1,
                        ["realtimeSinceStartup"] = Mathf.RoundToInt(Time.realtimeSinceStartup)
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(info, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetGameSpeed()
        {
            return new McpTool
            {
                Name = "game_set_speed",
                Group = "game",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Aliases = new List<string> { "set_game_speed" },
                Description = "兼容入口：请优先使用 game_control domain=speed action=set_speed",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["speed"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "游戏速度等级: 0=暂停, 1=正常, 2=快进, 3=超快",
                        Required = true,
                        EnumValues = new List<string> { "0", "1", "2", "3" }
                    }
                },
                Handler = args => SetSpeed(args)
            };
        }

        public static McpTool ControlGameSpeed()
        {
            return new McpTool
            {
                Name = "game_speed_control",
                Group = "game",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "game_control_speed", "game_pause_resume_speed" },
                Tags = new List<string> { "game", "speed", "pause", "resume", "time" },
                Description = "统一读取游戏时间并控制暂停/恢复/速度。action=time、pause、resume 或 set_speed；set_speed 时传 speed=0..3。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：time、pause、resume、set_speed", Required = true, EnumValues = new List<string> { "time", "pause", "resume", "set_speed" } },
                    ["speed"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "action=set_speed 时的速度等级：0=暂停, 1=正常, 2=快进, 3=超快",
                        Required = false,
                        EnumValues = new List<string> { "0", "1", "2", "3" }
                    }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "time":
                        case "get_time":
                            return GetGameTime().Handler(args);
                        case "pause":
                            return Pause();
                        case "resume":
                            return Resume();
                        case "set_speed":
                        case "speed":
                            return SetSpeed(args);
                        default:
                            return CallToolResult.Error("action must be time, pause, resume, or set_speed");
                    }
                }
            };
        }

        public static McpTool ControlGame()
        {
            return new McpTool
            {
                Name = "game_control",
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "game_system_control" },
                Tags = new List<string> { "game", "speed", "pause", "save", "dlc", "red-alert", "sandbox", "debug", "map", "ui", "feedback", "edit-mark" },
                Description = "统一游戏入口。domain=speed/state/save/dlc/sandbox/ui；sandbox 下 kind=read/area/entity/map_designate 用于沙盒读取、区域编辑、实体生成和 search/designate 地图编辑；ui 下 uiDomain=action/feedback/edit_mark。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "游戏子系统：speed、state、save、dlc、sandbox、ui", Required = true, EnumValues = new List<string> { "speed", "state", "save", "dlc", "sandbox", "ui" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "子系统动作：speed=time/pause/resume/set_speed；state=red_alert_status/set_red_alert/set_sandbox_mode；save=list/save/load/quit；dlc=list/activate；sandbox 按 kind 支持 list_actions/sample_cell/list_story_traits/paint/flood_fill/temperature/reveal/clear_floor/clear_critters/destroy/stress/spawn_entity/story_trait_stamp/auto_plumb_building；ui 按 uiDomain 支持 list/trigger/open_management/notification/popup/marker/create/clear", Required = true },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "domain=sandbox 时的子域：read、area、entity 或 map_designate，默认 read；domain=ui uiDomain=action action=list 时过滤类型", Required = false, EnumValues = new List<string> { "read", "area", "entity", "map_designate", "all", "management", "overlay", "build", "navigation" } },
                    ["uiDomain"] = new McpToolParameter { Type = "string", Description = "domain=ui 时的 UI 子域：action、feedback 或 edit_mark", Required = false, EnumValues = new List<string> { "action", "feedback", "edit_mark" } },
                    ["speed"] = new McpToolParameter { Type = "integer", Description = "domain=speed action=set_speed 时的速度等级：0=暂停, 1=正常, 2=快进, 3=超快", Required = false },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "domain=state 写动作 true=开启，false=关闭", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "domain=state 的世界 ID，默认当前激活世界", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "domain=state 是否应用/读取全部已加载世界，默认 false", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "domain=save list/load index 查找范围：local、cloud 或 both，默认 both", Required = false, EnumValues = new List<string> { "local", "cloud", "both" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "读取动作最多返回数量", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "domain=save 的另存为文件名；不能包含目录分隔符", Required = false },
                    ["overwrite"] = new McpToolParameter { Type = "boolean", Description = "domain=save action=save 时目标文件已存在是否覆盖", Required = false },
                    ["updateActiveSave"] = new McpToolParameter { Type = "boolean", Description = "domain=save action=save 成功后是否设为当前 active save，默认 true", Required = false },
                    ["index"] = new McpToolParameter { Type = "integer", Description = "domain=save action=load 时 game_control domain=save action=list 返回的 index", Required = false },
                    ["path"] = new McpToolParameter { Type = "string", Description = "domain=save action=load 时完整存档路径", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "domain=save action=quit 时 menu 退出到主菜单；desktop 退出程序，默认 menu", Required = false, EnumValues = new List<string> { "menu", "desktop" } },
                    ["saveFirst"] = new McpToolParameter { Type = "boolean", Description = "domain=save action=quit 时退出前是否先保存，默认 false", Required = false },
                    ["includeCosmetic"] = new McpToolParameter { Type = "boolean", Description = "domain=dlc action=list 时是否包含 cosmetic/content-only DLC，默认 false", Required = false },
                    ["dlcId"] = new McpToolParameter { Type = "string", Description = "domain=dlc action=activate 时的 DLC id，如 DLC2_ID、DLC3_ID、DLC4_ID、DLC5_ID", Required = false },
                    ["uiAction"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=action action=trigger 时的 Action 枚举名", Required = false },
                    ["screen"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=action action=open_management 时的页面名", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=feedback action=notification 时通知标题", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "domain=ui 的通知正文或提示内容，按 action 解释", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=feedback action=popup 时浮动提示文字", Required = false },
                    ["markerAction"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=feedback action=marker 时的子动作：create/list/clear", Required = false, EnumValues = new List<string> { "create", "list", "clear" } },
                    ["prompt"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=edit_mark action=create 时用户对框选区域的修改提示词", Required = false },
                    ["search"] = new McpToolParameter { Type = "string", Description = "domain=sandbox kind=map_designate 时要查找的文本地图片段", Required = false },
                    ["designate"] = new McpToolParameter { Type = "string", Description = "domain=sandbox kind=map_designate 时指定片段；_、same、keep 保留原格", Required = false },
                    ["replace"] = new McpToolParameter { Type = "string", Description = "兼容旧参数：请改用 designate", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 起点/目标格 X，按 action 解释", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 起点/目标格 Y，按 action 解释", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 矩形/搜索区域左下 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 矩形/搜索区域左下 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 矩形/搜索区域右上 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 矩形/搜索区域右上 Y", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "domain=sandbox 可选区域句柄", Required = false },
                    ["element"] = new McpToolParameter { Type = "string", Description = "domain=sandbox paint/flood_fill 或 map_designate 默认元素", Required = false },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "domain=sandbox kind=entity action=spawn_entity 时 Prefab ID", Required = false },
                    ["storyId"] = new McpToolParameter { Type = "string", Description = "domain=sandbox kind=entity action=story_trait_stamp 时故事 ID", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 目标对象 InstanceID，按 action 解释", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "domain=sandbox kind=read action=list_story_traits 时搜索词", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "domain=sandbox 元素质量 kg，按 action 解释", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "domain=sandbox 温度 K，按 action 解释", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "domain=sandbox 病菌 ID，默认无", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 每格病菌数量，默认 0", Required = false },
                    ["matchMode"] = new McpToolParameter { Type = "string", Description = "domain=sandbox kind=map_designate 匹配处理：unique/first/all，默认 unique", Required = false, EnumValues = new List<string> { "unique", "first", "all" } },
                    ["matchIndex"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox kind=map_designate 多匹配时选择第几个，0 基", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 区域/搜索安全上限", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "domain=sandbox kind=map_designate 只预览不修改，默认 true", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "domain=sandbox kind=map_designate 搜索时是否把未揭示格视为 unk，默认 false", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "domain=sandbox 允许绕过对应底层工具的沙盒模式或 InstantBuild 要求，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "底层写入/危险动作需要 true", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "speed":
                        case "time":
                            return ControlGameSpeed().Handler(args);
                        case "state":
                        case "red_alert":
                            return ControlGameState().Handler(args);
                        case "sandbox":
                        case "sandbox_tools":
                        case "debug":
                            return ForwardSandbox(args);
                        case "ui":
                        case "interface":
                            return ForwardUi(args);
                        case "save":
                        case "saves":
                        case "lifecycle":
                            return ControlGameSave().Handler(args);
                        case "dlc":
                        case "dlc_activation":
                            return ControlDlcActivation().Handler(args);
                        default:
                            return CallToolResult.Error("domain must be speed, state, save, dlc, sandbox, or ui");
                    }
                }
            };
        }

        private static CallToolResult ForwardUi(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            string uiDomain = (forwarded["uiDomain"]?.ToString() ?? string.Empty).Trim();
            bool uiDomainFromKind = false;
            if (string.IsNullOrWhiteSpace(uiDomain))
            {
                uiDomain = (forwarded["kind"]?.ToString() ?? string.Empty).Trim();
                uiDomainFromKind = true;
            }

            forwarded["domain"] = uiDomain;
            forwarded.Remove("uiDomain");
            if (uiDomainFromKind && !string.IsNullOrWhiteSpace(uiDomain))
                forwarded.Remove("kind");

            return UiControlTools.ControlUi().Handler(forwarded);
        }

        private static CallToolResult ForwardSandbox(JObject args)
        {
            string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string kind = (args["kind"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(kind) &&
                (action == "set_sandbox_mode" || action == "sandbox_mode" || action == "sandbox_toggle" || action == "sandbox"))
                return ControlGameState().Handler(args);

            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            forwarded.Remove("domain");
            return SandboxTools.ControlSandbox().Handler(forwarded);
        }

        public static McpTool PauseGame()
        {
            return new McpTool
            {
                Name = "game_pause",
                Group = "game",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Aliases = new List<string> { "pause_game" },
                Description = "兼容入口：请优先使用 game_control domain=speed action=pause",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args => Pause()
            };
        }

        public static McpTool ResumeGame()
        {
            return new McpTool
            {
                Name = "game_resume",
                Group = "game",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Aliases = new List<string> { "resume_game" },
                Description = "兼容入口：请优先使用 game_control domain=speed action=resume",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args => Resume()
            };
        }

        private static CallToolResult SetSpeed(JObject args)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");

            var speedToken = args["speed"];
            if (speedToken == null || !int.TryParse(speedToken.ToString(), out int speed))
                return CallToolResult.Error("Invalid speed parameter");

            if (speed < 0 || speed > 3)
                return CallToolResult.Error("Speed must be 0-3");

            var speedControl = SpeedControlScreen.Instance;
            if (speedControl == null)
                return CallToolResult.Error("SpeedControlScreen not available");

            int internalSpeed = Math.Max(0, Math.Min(speed - 1, 2));
            switch (speed)
            {
                case 0:
                    speedControl.Pause();
                    break;
                case 1:
                case 2:
                case 3:
                    UnpauseAll(speedControl);
                    speedControl.SetSpeed(internalSpeed);
                    break;
            }

            var result = new Dictionary<string, object>
            {
                ["requestedSpeed"] = speed,
                ["speed"] = speed == 0 ? 0 : speedControl.GetSpeed() + 1,
                ["internalSpeed"] = speedControl.GetSpeed(),
                ["timeScale"] = Time.timeScale,
                ["isPaused"] = speedControl.IsPaused
            };
            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        private static CallToolResult Pause()
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");

            SpeedControlScreen.Instance?.Pause();
            return CallToolResult.Text("Game paused");
        }

        private static CallToolResult Resume()
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");

            var speedControl = SpeedControlScreen.Instance;
            if (speedControl == null)
                return CallToolResult.Error("SpeedControlScreen not available");

            UnpauseAll(speedControl);
            var result = new Dictionary<string, object>
            {
                ["isPaused"] = speedControl.IsPaused,
                ["speed"] = speedControl.GetSpeed() + 1,
                ["internalSpeed"] = speedControl.GetSpeed(),
                ["timeScale"] = Time.timeScale
            };
            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        public static McpTool GetRedAlertStatus()
        {
            return new McpTool
            {
                Name = "game_red_alert_status",
                Hidden = true,
                Group = "game",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "red_alert_status", "emergency_status" },
                Tags = new List<string> { "game", "red-alert", "emergency", "priority", "紧急", "红色警戒" },
                Description = "兼容入口：请优先使用 game_control domain=state action=red_alert_status",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "是否返回全部已加载世界，默认 false", Required = false }
                },
                Handler = args =>
                {
                    if (ClusterManager.Instance == null)
                        return CallToolResult.Error("ClusterManager not initialized");

                    bool allWorlds = ToolUtil.GetBool(args, "allWorlds", false);
                    var worlds = ResolveAlertWorlds(args, allWorlds);
                    if (worlds.Count == 0)
                        return CallToolResult.Error("No matching world found");

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["activeWorldId"] = ClusterManager.Instance.activeWorldId,
                        ["returned"] = worlds.Count,
                        ["worlds"] = worlds.Select(RedAlertWorldInfo).ToList()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetRedAlert()
        {
            return new McpTool
            {
                Name = "game_red_alert_set",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "red_alert_set", "emergency_set", "game_emergency_set" },
                Tags = new List<string> { "game", "red-alert", "emergency", "priority", "紧急", "红色警戒" },
                Description = "兼容入口：请优先使用 game_control domain=state action=set_red_alert。默认只修改当前世界；allWorlds=true 可同步全部已加载世界。需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "true 开启红色警戒/紧急模式，false 关闭", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界；allWorlds=true 时忽略", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "是否应用到全部已加载世界，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认切换紧急模式，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (ClusterManager.Instance == null)
                        return CallToolResult.Error("ClusterManager not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    if (args["enabled"] == null)
                        return CallToolResult.Error("enabled is required");

                    bool enabled = ToolUtil.GetBool(args, "enabled", false);
                    bool allWorlds = ToolUtil.GetBool(args, "allWorlds", false);
                    var worlds = ResolveAlertWorlds(args, allWorlds);
                    if (worlds.Count == 0)
                        return CallToolResult.Error("No matching world found");

                    var before = worlds.Select(RedAlertWorldInfo).ToList();
                    foreach (var world in worlds)
                    {
                        var alert = world?.AlertManager;
                        if (alert == null)
                            continue;
                        if (alert.IsRedAlertToggledOn() != enabled)
                            alert.ToggleRedAlert(enabled);
                    }
                    var after = worlds.Select(RedAlertWorldInfo).ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["requestedEnabled"] = enabled,
                        ["allWorlds"] = allWorlds,
                        ["changed"] = JsonConvert.SerializeObject(before) != JsonConvert.SerializeObject(after),
                        ["before"] = before,
                        ["after"] = after
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetSandboxMode()
        {
            return new McpTool
            {
                Name = "game_sandbox_mode_set",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_mode_set", "sandbox_toggle" },
                Tags = new List<string> { "game", "sandbox", "debug", "toggle", "top-left" },
                Description = "兼容入口：请优先使用 game_control domain=state action=set_sandbox_mode。设置游戏沙盒模式开关，等价于顶部左侧 Sandbox Toggle。需要存档已启用 sandbox，并要求 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "true 开启沙盒模式，false 关闭", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (Game.Instance == null || global::SaveGame.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    if (!global::SaveGame.Instance.sandboxEnabled)
                        return CallToolResult.Error("Sandbox mode is locked for this save");

                    bool enabled = ToolUtil.GetBool(args, "enabled", false);
                    bool before = Game.Instance.SandboxModeActive;
                    Game.Instance.SandboxModeActive = enabled;
                    TopLeftControlScreen.Instance?.UpdateSandboxToggleState();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["before"] = before,
                        ["after"] = Game.Instance.SandboxModeActive,
                        ["sandboxEnabledForSave"] = global::SaveGame.Instance.sandboxEnabled
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlGameState()
        {
            return new McpTool
            {
                Name = "game_state_control",
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "game_emergency_control", "game_sandbox_control" },
                Tags = new List<string> { "game", "red-alert", "sandbox", "emergency", "state" },
                Description = "游戏状态聚合工具：action=red_alert_status/set_red_alert/set_sandbox_mode；高风险动作需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "red_alert_status、set_red_alert 或 set_sandbox_mode", Required = true, EnumValues = new List<string> { "red_alert_status", "set_red_alert", "set_sandbox_mode" } },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "set_red_alert/set_sandbox_mode 时 true=开启，false=关闭", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "red alert 世界 ID，默认当前激活世界", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "red alert 是否应用/读取全部已加载世界，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "set_red_alert/set_sandbox_mode 必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "red_alert_status" || action == "status")
                        return GetRedAlertStatus().Handler(args);
                    if (action == "set_red_alert" || action == "red_alert")
                        return SetRedAlert().Handler(args);
                    if (action == "set_sandbox_mode" || action == "sandbox")
                        return SetSandboxMode().Handler(args);
                    return CallToolResult.Error("action must be red_alert_status, set_red_alert, or set_sandbox_mode");
                }
            };
        }

        private static List<WorldContainer> ResolveAlertWorlds(JObject args, bool allWorlds)
        {
            if (ClusterManager.Instance == null)
                return new List<WorldContainer>();

            if (allWorlds)
                return ClusterManager.Instance.WorldContainers
                    .Where(item => item != null)
                    .OrderBy(item => item.id)
                    .ToList();

            int worldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance.activeWorldId;
            var world = ClusterManager.Instance.GetWorld(worldId);
            return world == null ? new List<WorldContainer>() : new List<WorldContainer> { world };
        }

        private static Dictionary<string, object> RedAlertWorldInfo(WorldContainer world)
        {
            var alert = world?.AlertManager;
            return new Dictionary<string, object>
            {
                ["worldId"] = world?.id ?? -1,
                ["name"] = world == null ? null : ToolUtil.CleanName(world.GetProperName()),
                ["isActive"] = ClusterManager.Instance != null && world != null && world.id == ClusterManager.Instance.activeWorldId,
                ["available"] = alert != null,
                ["redAlertToggledOn"] = alert != null && alert.IsRedAlertToggledOn(),
                ["isRedAlert"] = alert != null && alert.IsRedAlert(),
                ["isYellowAlert"] = alert != null && alert.IsYellowAlert(),
                ["isOn"] = alert != null && alert.IsOn()
            };
        }

        public static McpTool ControlGameSave()
        {
            return new McpTool
            {
                Name = "game_save_control",
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "game_saves_control", "game_lifecycle_control" },
                Tags = new List<string> { "game", "save", "load", "quit", "pause-menu", "lifecycle" },
                Description = "游戏存档/生命周期聚合工具：action=list/save/load/quit；save/load/quit 必须传 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、save、load 或 quit", Required = true, EnumValues = new List<string> { "list", "save", "load", "quit" } },
                    ["type"] = new McpToolParameter { Type = "string", Description = "list/load index 查找范围：local、cloud 或 both，默认 both", Required = false, EnumValues = new List<string> { "local", "cloud", "both" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 最多返回数量，默认 40，最大 200", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "save/quit saveFirst=true 时的另存为文件名；不能包含目录分隔符", Required = false },
                    ["overwrite"] = new McpToolParameter { Type = "boolean", Description = "目标文件已存在时是否允许覆盖，默认 false", Required = false },
                    ["updateActiveSave"] = new McpToolParameter { Type = "boolean", Description = "action=save 保存成功后是否把目标设为当前 active save，默认 true", Required = false },
                    ["index"] = new McpToolParameter { Type = "integer", Description = "action=load 时 game_control domain=save action=list 返回的 index；path 为空时使用", Required = false },
                    ["path"] = new McpToolParameter { Type = "string", Description = "action=load 时完整存档路径；必须位于 ONI 本地或云存档目录下", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "action=quit 时 menu 退出到主菜单；desktop 退出程序，默认 menu", Required = false, EnumValues = new List<string> { "menu", "desktop" } },
                    ["saveFirst"] = new McpToolParameter { Type = "boolean", Description = "action=quit 时退出前是否先保存，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=save/load/quit 必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                            return ListSaves().Handler(args);
                        case "save":
                            return SaveGame().Handler(args);
                        case "load":
                            return LoadSave().Handler(args);
                        case "quit":
                            return QuitGame().Handler(args);
                        default:
                            return CallToolResult.Error("action must be list, save, load, or quit");
                    }
                }
            };
        }

        public static McpTool ListSaves()
        {
            return new McpTool
            {
                Name = "game_saves_list",
                Hidden = true,
                Group = "game",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "saves_list", "save_files_list" },
                Tags = new List<string> { "game", "save", "load", "pause-menu", "lifecycle" },
                Description = "兼容入口：请优先使用 game_control domain=save action=list",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter { Type = "string", Description = "local、cloud 或 both，默认 both", Required = false, EnumValues = new List<string> { "local", "cloud", "both" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 40，最大 200", Required = false }
                },
                Handler = args =>
                {
                    if (SaveLoader.Instance == null)
                        return CallToolResult.Error("SaveLoader not initialized");

                    SaveLoader.SaveType type = ParseSaveType(args["type"]?.ToString());
                    int limit = ToolUtil.ClampLimit(args, 40, 200);
                    string active = SaveLoader.GetActiveSaveFilePath();
                    var files = SaveLoader.GetAllFiles(sort: true, type: type)
                        .Take(limit)
                        .Select((entry, index) => SaveFileInfo(entry.path, entry.timeStamp, index, active))
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["activeSaveFile"] = active,
                        ["activeSaveFolder"] = SafeCall(SaveLoader.GetActiveSaveFolder),
                        ["activeColonyFolder"] = SafeCall(SaveLoader.GetActiveSaveColonyFolder),
                        ["localSaveRoot"] = SafeCall(SaveLoader.GetSavePrefixAndCreateFolder),
                        ["cloudSaveRoot"] = SafeCall(SaveLoader.GetCloudSavePrefix),
                        ["returned"] = files.Count,
                        ["saves"] = files
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SaveGame()
        {
            return new McpTool
            {
                Name = "game_save",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "save_game", "save_as" },
                Tags = new List<string> { "game", "save", "save-as", "pause-menu", "filesystem" },
                Description = "兼容入口：请优先使用 game_control domain=save action=save。必须 confirm=true；覆盖已有文件还需 overwrite=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["name"] = new McpToolParameter { Type = "string", Description = "可选另存为文件名；不能包含目录分隔符；自动补 .sav", Required = false },
                    ["overwrite"] = new McpToolParameter { Type = "boolean", Description = "目标文件已存在时是否允许覆盖，默认 false", Required = false },
                    ["updateActiveSave"] = new McpToolParameter { Type = "boolean", Description = "保存成功后是否把目标设为当前 active save，默认 true", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险文件写入确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (Game.Instance == null || SaveLoader.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    string error;
                    string target = ResolveSaveTarget(args["name"]?.ToString(), out error);
                    if (target == null)
                        return CallToolResult.Error(error);

                    bool overwrite = ToolUtil.GetBool(args, "overwrite", false);
                    bool existedBefore = File.Exists(target);
                    if (existedBefore && !overwrite)
                        return CallToolResult.Error("Target save already exists; pass overwrite=true after verifying the path with game_control domain=save action=list");

                    bool updateActiveSave = ToolUtil.GetBool(args, "updateActiveSave", true);
                    string before = SaveLoader.GetActiveSaveFilePath();
                    string saved = SaveLoader.Instance.Save(target, isAutoSave: false, updateSavePointer: updateActiveSave);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["saved"] = saved,
                        ["target"] = target,
                        ["overwroteExisting"] = existedBefore,
                        ["activeSaveBefore"] = before,
                        ["activeSaveAfter"] = SaveLoader.GetActiveSaveFilePath(),
                        ["updateActiveSave"] = updateActiveSave
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool LoadSave()
        {
            return new McpTool
            {
                Name = "game_load_save",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "load_save", "game_load" },
                Tags = new List<string> { "game", "load", "save", "pause-menu", "lifecycle" },
                Description = "兼容入口：请优先使用 game_control domain=save action=load。必须先用 game_control domain=save action=list 确认 index/path，并传 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["index"] = new McpToolParameter { Type = "integer", Description = "game_control domain=save action=list 返回的 index；path 为空时使用", Required = false },
                    ["path"] = new McpToolParameter { Type = "string", Description = "完整存档路径；必须位于 ONI 本地或云存档目录下", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "index 查找范围：local、cloud 或 both，默认 both", Required = false, EnumValues = new List<string> { "local", "cloud", "both" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险载入确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (SaveLoader.Instance == null)
                        return CallToolResult.Error("SaveLoader not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    string error;
                    string target = ResolveLoadTarget(args, out error);
                    if (target == null)
                        return CallToolResult.Error(error);

                    LoadScreen.DoLoad(target);
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["loading"] = target,
                        ["activeSaveFile"] = SaveLoader.GetActiveSaveFilePath(),
                        ["note"] = "LoadScreen.DoLoad was invoked; current scene will transition through ONI loading."
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool QuitGame()
        {
            return new McpTool
            {
                Name = "game_quit",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "quit_game", "game_quit_to_menu", "game_quit_to_desktop" },
                Tags = new List<string> { "game", "quit", "pause-menu", "lifecycle", "desktop" },
                Description = "兼容入口：请优先使用 game_control domain=save action=quit。可先保存；操作会中断当前游戏场景，desktop 会结束游戏进程。必须 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["target"] = new McpToolParameter { Type = "string", Description = "menu 退出到主菜单；desktop 退出程序，默认 menu", Required = false, EnumValues = new List<string> { "menu", "desktop" } },
                    ["saveFirst"] = new McpToolParameter { Type = "boolean", Description = "退出前是否先保存，默认 false", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "saveFirst=true 且需要另存为时使用的文件名；不能包含目录分隔符", Required = false },
                    ["overwrite"] = new McpToolParameter { Type = "boolean", Description = "saveFirst=true 且目标文件已存在时是否允许覆盖，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险生命周期操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    string target = (args["target"]?.ToString() ?? "menu").Trim().ToLowerInvariant();
                    if (target != "menu" && target != "desktop")
                        return CallToolResult.Error("target must be menu or desktop");

                    string saved = null;
                    bool saveFirst = ToolUtil.GetBool(args, "saveFirst", false);
                    if (saveFirst)
                    {
                        if (SaveLoader.Instance == null)
                            return CallToolResult.Error("SaveLoader not initialized");

                        string error;
                        string saveTarget = ResolveSaveTarget(args["name"]?.ToString(), out error);
                        if (saveTarget == null)
                            return CallToolResult.Error(error);

                        bool overwrite = ToolUtil.GetBool(args, "overwrite", false);
                        if (File.Exists(saveTarget) && !overwrite)
                            return CallToolResult.Error("Target save already exists; pass overwrite=true after verifying the path with game_control domain=save action=list");

                        saved = SaveLoader.Instance.Save(saveTarget, isAutoSave: false, updateSavePointer: true);
                    }

                    MainThreadBridge.Enqueue(new System.Action(() =>
                    {
                        if (target == "desktop")
                            App.Quit();
                        else
                            PauseScreen.TriggerQuitGame();
                    }));

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["scheduled"] = target == "desktop" ? "quit_desktop" : "quit_to_menu",
                        ["saveFirst"] = saveFirst,
                        ["saved"] = saved,
                        ["note"] = target == "desktop"
                            ? "App.Quit was scheduled for the next Unity frame; MCP server process will terminate with the game."
                            : "PauseScreen.TriggerQuitGame was scheduled for the next Unity frame."
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlDlcActivation()
        {
            return new McpTool
            {
                Name = "game_dlc_activation_control",
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "game_dlc_control", "dlc_activation_control" },
                Tags = new List<string> { "game", "dlc", "pause-menu", "save", "reload", "lifecycle" },
                Description = "DLC 存档激活聚合工具：action=list/activate；activate 必须传 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list 或 activate", Required = true, EnumValues = new List<string> { "list", "activate" } },
                    ["includeCosmetic"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否包含 cosmetic/content-only DLC，默认 false", Required = false },
                    ["dlcId"] = new McpToolParameter { Type = "string", Description = "action=activate 时的 DLC id，如 DLC2_ID、DLC3_ID、DLC4_ID、DLC5_ID", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=activate 必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                            return ListDlcActivation().Handler(args);
                        case "activate":
                            return ActivateDlcForSave().Handler(args);
                        default:
                            return CallToolResult.Error("action must be list or activate");
                    }
                }
            };
        }

        public static McpTool ListDlcActivation()
        {
            return new McpTool
            {
                Name = "game_dlc_activation_list",
                Hidden = true,
                Group = "game",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "dlc_activation_list", "game_dlc_list" },
                Tags = new List<string> { "game", "dlc", "pause-menu", "save", "lifecycle" },
                Description = "兼容入口：请优先使用 game_control domain=dlc action=list",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["includeCosmetic"] = new McpToolParameter { Type = "boolean", Description = "是否包含 cosmetic/content-only DLC，默认 false", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null || SaveLoader.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool includeCosmetic = ToolUtil.GetBool(args, "includeCosmetic", false);
                    var entries = DlcActivationInfos(includeCosmetic).ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["activeSaveFile"] = SaveLoader.GetActiveSaveFilePath(),
                        ["activeDlcIds"] = SaveLoader.Instance.GameInfo.dlcIds != null ? SaveLoader.Instance.GameInfo.dlcIds.ToList() : new List<string>(),
                        ["returned"] = entries.Count,
                        ["dlc"] = entries
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ActivateDlcForSave()
        {
            return new McpTool
            {
                Name = "game_dlc_activate",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "activate_dlc_for_save", "dlc_activate" },
                Tags = new List<string> { "game", "dlc", "pause-menu", "save", "reload", "lifecycle" },
                Description = "兼容入口：请优先使用 game_control domain=dlc action=activate。会先写入备份存档，再修改当前存档并触发重载；必须先用 game_control domain=dlc action=list 确认可激活并传 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["dlcId"] = new McpToolParameter { Type = "string", Description = "DLC id，如 DLC2_ID、DLC3_ID、DLC4_ID、DLC5_ID", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险存档修改和重载确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (Game.Instance == null || SaveLoader.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    string dlcId = (args["dlcId"]?.ToString() ?? "").Trim();
                    string error;
                    var info = DlcActivationInfo(dlcId, includeCosmetic: true, out error);
                    if (info == null)
                        return CallToolResult.Error(error);

                    bool canActivate = (bool)info["canActivate"];
                    if (!canActivate)
                        return CallToolResult.Error(info["blockedReason"]?.ToString() ?? "DLC cannot be activated for this save");

                    string activeSave = SaveLoader.GetActiveSaveFilePath();
                    string backupSave = null;
                    try
                    {
                        string activeSaveFolder = SaveLoader.GetActiveSaveFolder();
                        string baseName = global::SaveGame.Instance?.BaseName;
                        if (!string.IsNullOrWhiteSpace(activeSaveFolder) && !string.IsNullOrWhiteSpace(baseName))
                            backupSave = Path.Combine(activeSaveFolder, baseName + UI.FRONTEND.OPTIONS_SCREEN.TOGGLE_SANDBOX_SCREEN.BACKUP_SAVE_GAME_APPEND + ".sav");
                    }
                    catch
                    {
                        backupSave = null;
                    }

                    MainThreadBridge.Enqueue(new System.Action(() =>
                    {
                        SaveLoader.Instance.UpgradeActiveSaveDLCInfo(dlcId, trigger_load: true);
                    }));

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["scheduled"] = true,
                        ["dlcId"] = dlcId,
                        ["title"] = info["title"],
                        ["activeSaveFile"] = activeSave,
                        ["backupSaveFile"] = backupSave,
                        ["note"] = "SaveLoader.UpgradeActiveSaveDLCInfo was scheduled for the next Unity frame; ONI will save a backup, update the active save, and reload it."
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static void UnpauseAll(SpeedControlScreen speedControl)
        {
            for (int i = 0; i < 16 && speedControl.IsPaused; i++)
                speedControl.Unpause(playSound: i == 0);
        }

        private static SaveLoader.SaveType ParseSaveType(string value)
        {
            switch ((value ?? "both").Trim().ToLowerInvariant())
            {
                case "local": return SaveLoader.SaveType.local;
                case "cloud": return SaveLoader.SaveType.cloud;
                default: return SaveLoader.SaveType.both;
            }
        }

        private static Dictionary<string, object> SaveFileInfo(string path, System.DateTime timeStamp, int index, string active)
        {
            var file = new FileInfo(path);
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["fileName"] = Path.GetFileName(path),
                ["name"] = Path.GetFileNameWithoutExtension(path),
                ["path"] = path,
                ["folder"] = Path.GetDirectoryName(path),
                ["timestampUtc"] = timeStamp.ToString("o"),
                ["sizeBytes"] = file.Exists ? file.Length : 0,
                ["isActive"] = string.Equals(path, active, StringComparison.OrdinalIgnoreCase),
                ["isAutoSave"] = SaveLoader.IsSaveAuto(path),
                ["location"] = SaveLoader.IsSaveCloud(path) ? "cloud" : SaveLoader.IsSaveLocal(path) ? "local" : "unknown"
            };
        }

        private static string ResolveSaveTarget(string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                string active = SaveLoader.GetActiveSaveFilePath();
                if (string.IsNullOrWhiteSpace(active))
                {
                    error = "No active save file; provide name to save into the active colony folder";
                    return null;
                }
                return active;
            }

            string trimmed = name.Trim();
            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || trimmed.Contains("/")
                || trimmed.Contains("\\"))
            {
                error = "name must be a plain file name without path separators";
                return null;
            }

            string folder = SaveLoader.GetActiveSaveColonyFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                error = "Active colony save folder is not available";
                return null;
            }
            return Path.Combine(folder, SaveScreen.GetValidSaveFilename(trimmed));
        }

        private static string ResolveLoadTarget(JObject args, out string error)
        {
            error = null;
            string path = args["path"]?.ToString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                string full = Path.GetFullPath(path);
                if (!File.Exists(full))
                {
                    error = "Save file does not exist";
                    return null;
                }
                if (!IsUnderSaveRoot(full))
                {
                    error = "path must be inside ONI local or cloud save roots";
                    return null;
                }
                return full;
            }

            int index = ToolUtil.GetInt(args, "index") ?? -1;
            if (index < 0)
            {
                error = "index or path is required";
                return null;
            }

            var files = SaveLoader.GetAllFiles(sort: true, type: ParseSaveType(args["type"]?.ToString()));
            if (index >= files.Count)
            {
                error = "index is outside game_control domain=save action=list range";
                return null;
            }
            return files[index].path;
        }

        private static bool IsUnderSaveRoot(string path)
        {
            string full = Path.GetFullPath(path);
            return IsUnderRoot(full, SafeCall(SaveLoader.GetSavePrefixAndCreateFolder))
                   || IsUnderRoot(full, SafeCall(SaveLoader.GetCloudSavePrefix));
        }

        private static bool IsUnderRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return false;

            string normalizedRoot = Path.GetFullPath(root);
            if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
                normalizedRoot += Path.DirectorySeparatorChar;
            return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeCall(Func<string> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<Dictionary<string, object>> DlcActivationInfos(bool includeCosmetic)
        {
            string error;
            var expansion1 = DlcActivationInfo(DlcManager.EXPANSION1_ID, includeCosmetic: true, out error);
            if (expansion1 != null)
                yield return expansion1;

            foreach (var pair in DlcManager.DLC_PACKS.OrderBy(pair => pair.Key))
            {
                if (!includeCosmetic && pair.Value.isCosmetic)
                    continue;

                var info = DlcActivationInfo(pair.Key, includeCosmetic, out error);
                if (info != null)
                    yield return info;
            }
        }

        private static Dictionary<string, object> DlcActivationInfo(string dlcId, bool includeCosmetic, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(dlcId))
            {
                error = "dlcId is required";
                return null;
            }

            if (!DlcManager.IsDlcId(dlcId))
            {
                error = "Unknown DLC id";
                return null;
            }

            bool isExpansion1 = dlcId == DlcManager.EXPANSION1_ID;
            bool isCosmetic = false;
            string versionLetter = DlcManager.GetContentLetter(dlcId);
            if (DlcManager.DLC_PACKS.TryGetValue(dlcId, out var pack))
            {
                isCosmetic = pack.isCosmetic;
                versionLetter = pack.versionLetter;
            }

            if (isCosmetic && !includeCosmetic)
            {
                error = "Cosmetic/content-only DLC is not part of pause-menu save activation";
                return null;
            }

            bool subscribed = DlcManager.IsContentSubscribed(dlcId);
            bool activeForSave = Game.IsDlcActiveForCurrentSave(dlcId);
            bool userEditable = !isExpansion1 && !isCosmetic;
            string blockedReason = "";
            if (isExpansion1)
                blockedReason = "EXPANSION1_ID is shown in the pause menu but is not user editable for an existing save";
            else if (isCosmetic)
                blockedReason = "Cosmetic/content-only DLC is not a save activation operation";
            else if (!subscribed)
                blockedReason = "DLC content is not subscribed/enabled on this installation";
            else if (activeForSave)
                blockedReason = "DLC is already active for the current save";

            return new Dictionary<string, object>
            {
                ["dlcId"] = dlcId,
                ["title"] = DlcManager.GetDlcTitleNoFormatting(dlcId),
                ["versionLetter"] = versionLetter,
                ["isSubscribed"] = subscribed,
                ["isActiveForSave"] = activeForSave,
                ["isCosmetic"] = isCosmetic,
                ["userEditableInPauseMenu"] = userEditable,
                ["canActivate"] = userEditable && subscribed && !activeForSave,
                ["blockedReason"] = blockedReason
            };
        }

        public static McpTool ControlBuildingsRead()
        {
            return new McpTool
            {
                Name = "buildings_read_control",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "buildings_control", "building_read_control" },
                Tags = new List<string> { "buildings", "read", "summary" },
                Description = "统一读取建筑列表和建筑类型摘要；action=list/summary。兼容 buildings_list 与 buildings_summary。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "读取动作：list=建筑明细列表，summary=按建筑类型聚合摘要",
                        Required = true
                    },
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "建筑类型筛选（如 Generator, Pump, Bed, Tile 等），留空返回所有",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少个建筑/建筑类型，默认 100，最大 500",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    var action = args["action"]?.ToString()?.Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                        case "buildings":
                            return GetBuildings().Handler(args);
                        case "summary":
                        case "aggregate":
                            return GetBuildingSummary().Handler(args);
                        default:
                            return CallToolResult.Error("action must be one of: list, summary");
                    }
                }
            };
        }

        public static McpTool GetBuildings()
        {
            return new McpTool
            {
                Name = "buildings_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "get_buildings" },
                Description = "兼容入口：请优先使用 read_control domain=buildings action=list。获取殖民地建筑列表，可按类型筛选",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "建筑类型筛选（如 Generator, Pump, Bed 等），留空返回所有",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少个建筑，默认 100，最大 500",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string filterType = args["type"]?.ToString()?.ToLower();
                    int limit = 100;
                    if (args["limit"] != null && int.TryParse(args["limit"].ToString(), out int parsedLimit))
                        limit = Math.Max(1, Math.Min(parsedLimit, 500));

                    var buildings = new List<Dictionary<string, object>>();
                    var seen = new HashSet<string>();

                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        if (building == null) continue;

                        var prefabName = building.name;
                        var def = building.Def;
                        var name = ToolUtil.CleanName(def?.Name ?? prefabName);
                        var position = building.transform?.position ?? Vector3.zero;
                        var x = Mathf.RoundToInt(position.x);
                        var y = Mathf.RoundToInt(position.y);
                        var worldId = building.GetMyWorldId();
                        var identity = (def?.PrefabID ?? prefabName) + "|" + x + "|" + y + "|" + worldId;
                        if (!seen.Add(identity)) continue;

                        if (!string.IsNullOrEmpty(filterType) &&
                            !name.ToLower().Contains(filterType) &&
                            !prefabName.ToLower().Contains(filterType))
                            continue;

                        var operational = building.GetComponent<Operational>();

                        buildings.Add(new Dictionary<string, object>
                        {
                            ["name"] = name,
                            ["prefabId"] = def?.PrefabID ?? "unknown",
                            ["position"] = new { x, y },
                            ["isOperational"] = operational?.IsOperational ?? false,
                            ["isActive"] = operational?.IsActive ?? false,
                            ["worldId"] = worldId
                        });
                    }

                    var limited = buildings.Take(limit).ToList();
                    var summary = new Dictionary<string, object>
                    {
                        ["total"] = buildings.Count,
                        ["returned"] = limited.Count,
                        ["buildings"] = limited
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(summary, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetBuildingSummary()
        {
            return new McpTool
            {
                Name = "buildings_summary",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "get_building_summary" },
                Description = "兼容入口：请优先使用 read_control domain=buildings action=summary。按建筑类型聚合统计数量、运行状态和世界分布",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "建筑类型筛选（如 Generator, Pump, Tile 等），留空返回所有",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少种建筑，默认 100，最大 500",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string filterType = args["type"]?.ToString()?.ToLower();
                    int limit = 100;
                    if (args["limit"] != null && int.TryParse(args["limit"].ToString(), out int parsedLimit))
                        limit = Math.Max(1, Math.Min(parsedLimit, 500));

                    var groups = new Dictionary<string, BuildingAggregate>();
                    var seen = new HashSet<string>();

                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        if (building == null) continue;

                        var def = building.Def;
                        var prefabId = def?.PrefabID ?? building.name;
                        var name = ToolUtil.CleanName(def?.Name ?? prefabId);
                        var position = building.transform?.position ?? Vector3.zero;
                        var x = Mathf.RoundToInt(position.x);
                        var y = Mathf.RoundToInt(position.y);
                        var worldId = building.GetMyWorldId();
                        var identity = prefabId + "|" + x + "|" + y + "|" + worldId;
                        if (!seen.Add(identity)) continue;

                        if (!string.IsNullOrEmpty(filterType) &&
                            !name.ToLower().Contains(filterType) &&
                            !prefabId.ToLower().Contains(filterType))
                            continue;

                        BuildingAggregate aggregate;
                        if (!groups.TryGetValue(prefabId, out aggregate))
                        {
                            aggregate = new BuildingAggregate
                            {
                                Name = name,
                                PrefabId = prefabId,
                                WorldIds = new HashSet<int>()
                            };
                            groups[prefabId] = aggregate;
                        }

                        var operational = building.GetComponent<Operational>();
                        aggregate.Count++;
                        aggregate.OperationalCount += operational != null && operational.IsOperational ? 1 : 0;
                        aggregate.ActiveCount += operational != null && operational.IsActive ? 1 : 0;
                        aggregate.WorldIds.Add(worldId);
                    }

                    var summaries = groups.Values
                        .OrderByDescending(group => group.Count)
                        .Take(limit)
                        .Select(group => group.ToDictionary())
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["totalTypes"] = groups.Count,
                        ["returned"] = summaries.Count,
                        ["buildings"] = summaries
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private class BuildingAggregate
        {
            public string Name;
            public string PrefabId;
            public int Count;
            public int OperationalCount;
            public int ActiveCount;
            public HashSet<int> WorldIds;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["count"] = Count,
                    ["operationalCount"] = OperationalCount,
                    ["activeCount"] = ActiveCount,
                    ["worldIds"] = WorldIds.OrderBy(id => id).ToList()
                };
            }
        }
    }
}
