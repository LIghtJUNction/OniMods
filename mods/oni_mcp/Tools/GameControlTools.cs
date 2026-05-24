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
                Description = "获取游戏内时间和周期信息",
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
                Aliases = new List<string> { "set_game_speed" },
                Description = "设置游戏速度（1=正常, 2=快进, 3=超快）",
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
                Handler = args =>
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
                            UnpauseAll(speedControl);
                            speedControl.SetSpeed(internalSpeed);
                            break;
                        case 2:
                            UnpauseAll(speedControl);
                            speedControl.SetSpeed(internalSpeed);
                            break;
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
            };
        }

        public static McpTool PauseGame()
        {
            return new McpTool
            {
                Name = "game_pause",
                Group = "game",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "pause_game" },
                Description = "暂停游戏",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    SpeedControlScreen.Instance?.Pause();
                    return CallToolResult.Text("Game paused");
                }
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
                Aliases = new List<string> { "resume_game" },
                Description = "恢复游戏（取消暂停）",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
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
            };
        }

        public static McpTool GetRedAlertStatus()
        {
            return new McpTool
            {
                Name = "game_red_alert_status",
                Group = "game",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "red_alert_status", "emergency_status" },
                Tags = new List<string> { "game", "red-alert", "emergency", "priority", "紧急", "红色警戒" },
                Description = "读取当前/全部世界的红色警戒（紧急模式）状态",
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
                Group = "game",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "red_alert_set", "emergency_set", "game_emergency_set" },
                Tags = new List<string> { "game", "red-alert", "emergency", "priority", "紧急", "红色警戒" },
                Description = "开启或关闭红色警戒（紧急模式）。默认只修改当前世界；allWorlds=true 可同步全部已加载世界。需 confirm=true",
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
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_mode_set", "sandbox_toggle" },
                Tags = new List<string> { "game", "sandbox", "debug", "toggle", "top-left" },
                Description = "设置游戏沙盒模式开关，等价于顶部左侧 Sandbox Toggle。需要存档已启用 sandbox，并要求 confirm=true",
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

        public static McpTool ListSaves()
        {
            return new McpTool
            {
                Name = "game_saves_list",
                Group = "game",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "saves_list", "save_files_list" },
                Tags = new List<string> { "game", "save", "load", "pause-menu", "lifecycle" },
                Description = "列出本地/云端存档文件，供保存、另存为和载入前确认目标",
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
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "save_game", "save_as" },
                Tags = new List<string> { "game", "save", "save-as", "pause-menu", "filesystem" },
                Description = "保存当前游戏；未提供 name 时覆盖当前 active save，提供 name 时保存到当前殖民地目录。必须 confirm=true；覆盖已有文件还需 overwrite=true",
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
                        return CallToolResult.Error("Target save already exists; pass overwrite=true after verifying the path with game_saves_list");

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
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "load_save", "game_load" },
                Tags = new List<string> { "game", "load", "save", "pause-menu", "lifecycle" },
                Description = "载入已有存档，等价于 LoadScreen 选择存档并确认；会停止当前游戏并切换场景。必须先用 game_saves_list 确认 index/path，并传 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["index"] = new McpToolParameter { Type = "integer", Description = "game_saves_list 返回的 index；path 为空时使用", Required = false },
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
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "quit_game", "game_quit_to_menu", "game_quit_to_desktop" },
                Tags = new List<string> { "game", "quit", "pause-menu", "lifecycle", "desktop" },
                Description = "退出当前游戏到主菜单或桌面。可先保存；操作会中断当前游戏场景，desktop 会结束游戏进程。必须 confirm=true",
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
                            return CallToolResult.Error("Target save already exists; pass overwrite=true after verifying the path with game_saves_list");

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

        public static McpTool ListDlcActivation()
        {
            return new McpTool
            {
                Name = "game_dlc_activation_list",
                Group = "game",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "dlc_activation_list", "game_dlc_list" },
                Tags = new List<string> { "game", "dlc", "pause-menu", "save", "lifecycle" },
                Description = "列出暂停菜单中可查看/可激活的 DLC 存档状态，包括订阅、当前存档是否启用以及是否允许由玩家激活",
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
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "activate_dlc_for_save", "dlc_activate" },
                Tags = new List<string> { "game", "dlc", "pause-menu", "save", "reload", "lifecycle" },
                Description = "为当前存档激活一个暂停菜单可编辑 DLC。会先写入备份存档，再修改当前存档并触发重载；必须先用 game_dlc_activation_list 确认可激活并传 confirm=true",
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
                error = "index is outside game_saves_list range";
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

        public static McpTool GetBuildings()
        {
            return new McpTool
            {
                Name = "buildings_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_buildings" },
                Description = "获取殖民地建筑列表，可按类型筛选",
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
                Aliases = new List<string> { "get_building_summary" },
                Description = "按建筑类型聚合统计数量、运行状态和世界分布",
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
