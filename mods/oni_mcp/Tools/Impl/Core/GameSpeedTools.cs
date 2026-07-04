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
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 game_control domain=speed action=time",
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

    }
}
