using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    /// <summary>
    /// 游戏控制相关 MCP Tools
    /// </summary>
    public static partial class GameControlTools
    {
        private static Game _mcpPauseGame;
        private static SpeedControlScreen _mcpPauseScreen;

        public static McpTool GetGameTime()
        {
            return new McpTool
            {
                Name = "game_time",
                Group = "game",
                Mode = "read",
                Risk = "none",
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

        public static McpTool ControlGameSpeed()
        {
            return new McpTool
            {
                Name = "game_speed_control",
                Group = "game",
                Mode = "execute",
                Risk = "low",
                Tags = new List<string> { "game", "speed", "pause", "resume", "time" },
                Description = "统一读取游戏时间并控制暂停/恢复/速度/步进。action=time、pause、resume、set_speed、step_until、step_status、step_facts、step_cancel；step_until 类似 Playwright waitForCondition，以快进推进直到工单完成、小人发呆、警报或超时后自动暂停并返回增量差异报告。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：time、pause、resume、set_speed、step_until、step_status、step_facts、step_cancel", Required = true, EnumValues = new List<string> { "time", "pause", "resume", "set_speed", "step_until", "step_status", "step_facts", "step_cancel" } },
                    ["speed"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "set_speed/step_until 时的速度等级：0=暂停, 1=正常, 2=快进, 3=超快（step_until 默认 3）",
                        Required = false,
                        EnumValues = new List<string> { "0", "1", "2", "3" }
                    },
                    ["condition"] = new McpToolParameter { Type = "string", Description = "兼容停止条件：orders_complete、dupe_idle（任一工作时段复制人）、all_dupes_idle、cycle_end、alert、any；与 until 二选一", Required = false },
                    ["until"] = new McpToolParameter { Type = "object", Description = "可组合条件：{fact:dupes_idle,quantifier:all,schedule:work,stableSeconds:2} 或 {all:[...]}/{any:[...]}；最多 4 层/32 节点，未知字段拒绝。fact 支持 orders_complete/cycle_end/alert/alarm/new_notification/notification/printing_pod_ready/research_complete；printing_pod_ready 为当前原生就绪状态，默认已就绪时恢复前返回", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "step_until/step_facts 的复制人和挖掘/建筑工单世界；默认当前世界，危险告警仍全局强制暂停", Required = false },
                    ["maxGameSeconds"] = new McpToolParameter { Type = "number", Description = "step_until 游戏内最大推进秒数，默认 60，范围 1–600", Required = false },
                    ["maxWallSeconds"] = new McpToolParameter { Type = "number", Description = "step_until 真实世界最大等待秒数，默认 15，范围 0.5–30", Required = false },
                    ["wait"] = new McpToolParameter { Type = "boolean", Description = "step_until 是否等待执行结果完成后返回，默认 true；false 为后台执行", Required = false },
                    ["requestId"] = new McpToolParameter { Type = "string", Description = "step_status/step_cancel 的等待句柄；省略时查询当前等待或最近完成结果", Required = false },
                    ["targetCells"] = new McpToolParameter { Type = "array", Description = "step_until 监控的 cell 数组；捕获涉及这些格子的挖掘、建筑及先挖后建流程，直到各流程终止；不传则捕获当前世界", Required = false }
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
                        case "step_until":
                        case "step":
                        case "wait_until":
                            return StepUntil(args);
                        case "step_status":
                            return StepStatus(args);
                        case "step_facts":
                            return StepFacts(args);
                        case "step_cancel":
                            return StepCancel(args);
                        default:
                            return CallToolResult.Error("action must be time, pause, resume, set_speed, step_until, step_status, step_facts, or step_cancel");
                    }
                }
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

            CancelStepForSpeedCommand("set_speed");
            int internalSpeed = Math.Max(0, Math.Min(speed - 1, 2));
            switch (speed)
            {
                case 0:
                    AcquireMcpPause(speedControl);
                    break;
                case 1:
                case 2:
                case 3:
                    ReleaseMcpPause(speedControl);
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

            var speedControl = SpeedControlScreen.Instance;
            if (speedControl == null)
                return CallToolResult.Error("SpeedControlScreen not available");

            CancelStepForSpeedCommand("pause");
            AcquireMcpPause(speedControl);
            return CallToolResult.Text("Game paused");
        }

        private static CallToolResult Resume()
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");

            var speedControl = SpeedControlScreen.Instance;
            if (speedControl == null)
                return CallToolResult.Error("SpeedControlScreen not available");

            CancelStepForSpeedCommand("resume");
            ReleaseMcpPause(speedControl, releaseNativePause: true);
            var result = new Dictionary<string, object>
            {
                ["isPaused"] = speedControl.IsPaused,
                ["speed"] = speedControl.GetSpeed() + 1,
                ["internalSpeed"] = speedControl.GetSpeed(),
                ["timeScale"] = Time.timeScale
            };
            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        private static void AcquireMcpPause(SpeedControlScreen speedControl)
        {
            var game = Game.Instance;
            if (game == null || speedControl == null)
                return;

            if (ReferenceEquals(_mcpPauseGame, game) && ReferenceEquals(_mcpPauseScreen, speedControl))
                return;

            _mcpPauseGame = null;
            _mcpPauseScreen = null;
            speedControl.Pause();
            _mcpPauseGame = game;
            _mcpPauseScreen = speedControl;
        }

        private static void ReleaseMcpPause(SpeedControlScreen speedControl, bool releaseNativePause = false)
        {
            var game = Game.Instance;
            bool hasMcpPauseLease = _mcpPauseGame != null || _mcpPauseScreen != null;
            bool ownsPause = game != null
                && ReferenceEquals(_mcpPauseGame, game)
                && ReferenceEquals(_mcpPauseScreen, speedControl);

            _mcpPauseGame = null;
            _mcpPauseScreen = null;
            if (speedControl == null || !speedControl.IsPaused)
                return;

            if (ownsPause)
            {
                // Release only the reference acquired by MCP. Other ONI pause owners stay intact.
                speedControl.Unpause();
            }
            else if (!hasMcpPauseLease && releaseNativePause)
            {
                // Resume must also release a native ONI pause when MCP never acquired one.
                speedControl.Unpause();
            }
        }

    }
}
