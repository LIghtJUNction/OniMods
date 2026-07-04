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
    public static partial class GameControlTools
    {
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
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只预览目标路径和覆盖需求，不写入存档", Required = false },
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
                    bool updateActiveSave = ToolUtil.GetBool(args, "updateActiveSave", true);
                    string before = SaveLoader.GetActiveSaveFilePath();
                    if (ToolUtil.GetBool(args, "dryRun", false))
                    {
                        return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["dryRun"] = true,
                            ["wouldSave"] = target,
                            ["target"] = target,
                            ["exists"] = existedBefore,
                            ["wouldOverwrite"] = existedBefore && overwrite,
                            ["requiresOverwrite"] = existedBefore && !overwrite,
                            ["activeSaveBefore"] = before,
                            ["updateActiveSave"] = updateActiveSave,
                            ["next"] = existedBefore && !overwrite
                                ? "Pass overwrite=true and dryRun=false to overwrite this save."
                                : "Pass dryRun=false with confirm=true to save."
                        }, McpJsonUtil.Settings));
                    }
                    if (existedBefore && !overwrite)
                        return CallToolResult.Error("Target save already exists; pass overwrite=true after verifying the path with game_control domain=save action=list");

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
                return getter?.Invoke();
            }
            catch
            {
                return null;
            }
        }
    }
}
