using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class GameLaunchTools
    {
        public static McpTool ControlGameLaunch()
        {
            return new McpTool
            {
                Name = "game_launch_control",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "game_start_control", "game_auto_start" },
                Tags = new List<string> { "game", "launch", "start", "load", "lifecycle", "automation" },
                Description = "兼容入口：请优先使用 game_control domain=launch。支持 status/start/restart_load/restart_status；restart_load 保存当前精确存档后通过 Steam 重启并由新进程加载。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "status 查询会话；start 加载存档；restart_load 保存并经 Steam 重启后精确加载；restart_status 查询持久任务", Required = true, EnumValues = new List<string> { "status", "start", "restart_load", "restart_status" } },
                    ["type"] = new McpToolParameter { Type = "string", Description = "存档来源：local、cloud 或 both，默认 both", Required = false, EnumValues = new List<string> { "local", "cloud", "both" } },
                    ["index"] = new McpToolParameter { Type = "integer", Description = "要加载的存档索引，来自 action=status 返回的 saves；省略则使用最近存档", Required = false },
                    ["path"] = new McpToolParameter { Type = "string", Description = "要加载的完整存档路径；优先于 index/latest", Required = false },
                    ["forceLoad"] = new McpToolParameter { Type = "boolean", Description = "已在游戏内时是否仍强制加载目标存档，默认 false", Required = false },
                    ["resume"] = new McpToolParameter { Type = "boolean", Description = "start 默认 true；restart_load 默认 false，重启加载后保持暂停", Required = false },
                    ["speed"] = new McpToolParameter { Type = "integer", Description = "resume=true 时设置速度：1=正常、2=快进、3=超快，默认 1", Required = false, EnumValues = new List<string> { "1", "2", "3" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "status 返回多少个候选存档，默认 5，最大 50", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "restart_load/start 仅预览，不执行保存、退出或加载", Required = false },
                    ["jobId"] = new McpToolParameter { Type = "string", Description = "restart_status 可选任务 ID，用于核对查询结果", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=start/restart_load 实际执行必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "status").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "status":
                            return Status(args);
                        case "start":
                        case "load":
                        case "continue":
                            return Start(args);
                        case "restart_load":
                            return RestartLoad(args);
                        case "restart_status":
                            return RestartStatus(args);
                        default:
                            return CallToolResult.Error("action must be status, start, restart_load, or restart_status");
                    }
                }
            };
        }

        private static CallToolResult Status(JObject args)
        {
            int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 5, 50));
            var saves = ListSaves(args, limit);
            string activeSaveFile = SafeCall(SaveLoader.GetActiveSaveFilePath);
            bool hasActiveSave = IsUsableSavePath(activeSaveFile);
            bool canStart = saves.Count > 0 || hasActiveSave;
            bool alreadyInGame = Game.Instance != null;
            var blockers = new List<string>();
            if (!canStart)
                blockers.Add("no_save_candidates");
            var result = new Dictionary<string, object>
            {
                ["gameInitialized"] = alreadyInGame,
                ["loaded"] = alreadyInGame,
                ["saveLoaderReady"] = SaveLoader.Instance != null,
                ["activeSaveFile"] = activeSaveFile,
                ["activeSaveUsable"] = hasActiveSave,
                ["canStart"] = canStart,
                ["blockers"] = blockers,
                ["next"] = LaunchNextHint(alreadyInGame, canStart),
                ["isPaused"] = SpeedControlScreen.Instance != null ? SpeedControlScreen.Instance.IsPaused : (bool?)null,
                ["speed"] = SpeedControlScreen.Instance != null ? (object)(SpeedControlScreen.Instance.GetSpeed() + 1) : null,
                ["candidateCount"] = saves.Count,
                ["saves"] = saves
            };
            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        private static string LaunchNextHint(bool alreadyInGame, bool canStart)
        {
            if (alreadyInGame)
                return "Game is already loaded. Use colony_control domain=snapshot action=get, or pass forceLoad=true to reload another save intentionally.";
            return canStart
                ? "Call game_control domain=launch action=start confirm=true. Pass index/path to choose a save."
                : "Wait for ONI main menu save services or pass a valid save path under the ONI save root.";
        }

        private static CallToolResult Start(JObject args)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required for action=start");
            bool forceLoad = ToolUtil.GetBool(args, "forceLoad", false);
            bool alreadyInGame = Game.Instance != null;
            string target = ResolveTargetSave(args, out string error);
            if (target == null)
                return CallToolResult.Error(error);

            if (ToolUtil.GetBool(args, "dryRun", false))
            {
                return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["started"] = false,
                    ["dryRun"] = true,
                    ["wouldLoad"] = !alreadyInGame || forceLoad,
                    ["alreadyInGame"] = alreadyInGame,
                    ["forceLoad"] = forceLoad,
                    ["target"] = target,
                    ["activeSaveFile"] = SaveLoader.GetActiveSaveFilePath(),
                    ["resumeRequested"] = ToolUtil.GetBool(args, "resume", true),
                    ["speedRequested"] = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "speed") ?? 1, 3)),
                    ["next"] = "Dry run only; no save load or speed/pause change was applied."
                }, McpJsonUtil.Settings));
            }

            Dictionary<string, object> speedResult = null;
            if (alreadyInGame && !forceLoad)
            {
                speedResult = ApplyResume(args);
                return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["started"] = false,
                    ["alreadyInGame"] = true,
                    ["loaded"] = false,
                    ["target"] = target,
                    ["activeSaveFile"] = SaveLoader.GetActiveSaveFilePath(),
                    ["speed"] = speedResult,
                    ["next"] = "Game was already initialized; pass forceLoad=true to load target save anyway."
                }, McpJsonUtil.Settings));
            }

            if (!CanStartExactSaveLoad(target))
                return CallToolResult.Error("Exact save or loading UI is not ready; wait for the main menu and retry");
            StartExactSaveLoad(target);
            speedResult = ApplyResume(args);
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["started"] = true,
                ["alreadyInGame"] = alreadyInGame,
                ["loaded"] = true,
                ["target"] = target,
                ["activeSaveFileBeforeLoad"] = SaveLoader.GetActiveSaveFilePath(),
                ["speedRequested"] = speedResult,
                ["note"] = "LoadingOverlay.Load invoked; scene transition continues asynchronously. Verify with game_control domain=launch action=status or colony snapshot after load."
            }, McpJsonUtil.Settings));
        }

        private static string ResolveTargetSave(JObject args, out string error)
        {
            error = null;
            string path = args["path"]?.ToString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                string full = Path.GetFullPath(path);
                if (!IsUnderSaveRoot(full))
                {
                    error = "path must be inside ONI local or cloud save roots";
                    return null;
                }
                return full;
            }

            var saves = SaveLoader.GetAllFiles(sort: true, type: ParseSaveType(args["type"]?.ToString()));
            if (saves == null || saves.Count == 0)
            {
                string active = SafeCall(SaveLoader.GetActiveSaveFilePath);
                if (IsUsableSavePath(active))
                    return active;
                error = "No save files found; wait for ONI save services or pass path to an existing save";
                return null;
            }

            int index = ToolUtil.GetInt(args, "index") ?? 0;
            if (index < 0 || index >= saves.Count)
            {
                error = $"index outside save range: {index}, available 0..{saves.Count - 1}";
                return null;
            }
            return saves[index].path;
        }

        private static List<Dictionary<string, object>> ListSaves(JObject args, int limit)
        {
            string active = SafeCall(SaveLoader.GetActiveSaveFilePath);
            return SaveLoader.GetAllFiles(sort: true, type: ParseSaveType(args["type"]?.ToString()))
                .Take(limit)
                .Select((entry, index) => SaveInfo(entry.path, entry.timeStamp, index, active))
                .ToList();
        }

        private static Dictionary<string, object> SaveInfo(string path, System.DateTime timeStamp, int index, string active)
        {
            var file = new FileInfo(path);
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["fileName"] = Path.GetFileName(path),
                ["name"] = Path.GetFileNameWithoutExtension(path),
                ["path"] = path,
                ["timestampUtc"] = timeStamp.ToString("o"),
                ["sizeBytes"] = file.Exists ? file.Length : 0,
                ["isActive"] = string.Equals(path, active, StringComparison.OrdinalIgnoreCase),
                ["isAutoSave"] = SaveLoader.IsSaveAuto(path),
                ["location"] = SaveLoader.IsSaveCloud(path) ? "cloud" : SaveLoader.IsSaveLocal(path) ? "local" : "unknown"
            };
        }

        private static Dictionary<string, object> ApplyResume(JObject args)
        {
            bool resume = ToolUtil.GetBool(args, "resume", true);
            int speed = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "speed") ?? 1, 3));
            var speedControl = SpeedControlScreen.Instance;
            if (!resume || speedControl == null)
                return new Dictionary<string, object> { ["applied"] = false, ["reason"] = resume ? "SpeedControlScreen unavailable" : "resume=false" };

            for (int i = 0; i < 16 && speedControl.IsPaused; i++)
                speedControl.Unpause(playSound: i == 0);
            speedControl.SetSpeed(Math.Max(0, Math.Min(speed - 1, 2)));
            return new Dictionary<string, object>
            {
                ["applied"] = true,
                ["speed"] = speedControl.GetSpeed() + 1,
                ["isPaused"] = speedControl.IsPaused
            };
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

        internal static bool IsUnderSaveRoot(string path)
        {
            string full = Path.GetFullPath(path);
            return IsUnderRoot(full, SafeCall(SaveLoader.GetSavePrefixAndCreateFolder))
                || IsUnderRoot(full, SafeCall(SaveLoader.GetCloudSavePrefix));
        }

        private static bool IsUsableSavePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            try
            {
                return File.Exists(path) && IsUnderSaveRoot(path);
            }
            catch
            {
                return false;
            }
        }

        internal static bool CanStartExactSaveLoad(string path)
        {
            return IsUsableSavePath(path)
                && ScreenPrefabs.Instance != null
                && ScreenPrefabs.Instance.loadingOverlay != null
                && (GameScreenManager.Instance != null
                    || UnityEngine.GameObject.Find("/SceneInitializerFE/FrontEndManager") != null);
        }

        internal static void StartExactSaveLoad(string path, Action<Exception> onCallbackError = null)
        {
            LoadingOverlay.Load(() =>
            {
                try { LoadScreen.DoLoad(path); }
                catch (Exception ex)
                {
                    if (onCallbackError == null)
                        throw;
                    onCallbackError(ex);
                }
            });
        }

        private static bool IsUnderRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
                return false;
            string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return normalizedPath.StartsWith(normalizedRoot, comparison);
        }

        internal static string SafeCall(Func<string> func)
        {
            try { return func(); }
            catch { return null; }
        }
    }
}
