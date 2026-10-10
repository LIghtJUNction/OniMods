using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class GameControlTools
    {
        public static McpTool ControlGame()
        {
            return new McpTool
            {
                Name = "game_control",
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "game_system_control" },
                Tags = new List<string> { "game", "speed", "pause", "save", "launch", "dlc", "red-alert", "ui", "feedback" },
                Description = "正常游戏控制：launch/speed/state/save/dlc/ui。观察和下达指令时暂停；短时间运行后再次暂停并核对结果。建造和工作由游戏完成，不提供直接改写世界的功能。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "launch、speed、state、save、dlc、ui", Required = true, EnumValues = new List<string> { "launch", "speed", "state", "save", "dlc", "ui" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "launch=status/start/restart_load/restart_status；speed=time/pause/resume/set_speed；state=red_alert_status/set_red_alert；save=list/save/load/quit；dlc=list/activate；ui 按 uiDomain 路由", Required = true },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "ui action=list 的类型过滤", Required = false, EnumValues = new List<string> { "all", "management", "overlay", "build", "navigation" } },
                    ["uiDomain"] = new McpToolParameter { Type = "string", Description = "ui 子域：action、feedback", Required = false, EnumValues = new List<string> { "action", "feedback" } },
                    ["speed"] = new McpToolParameter { Type = "integer", Description = "set_speed：0=暂停，1=正常，2=快进，3=超快", Required = false },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "set_red_alert：true 开启，false 关闭", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "state 的世界 ID；默认当前世界", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "state 是否作用于全部已加载世界；默认 false", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "存档位置；默认 both", Required = false, EnumValues = new List<string> { "local", "cloud", "both" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "读取结果上限", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "另存为文件名（不能含目录）；speech_bubble 的复制人名称", Required = false },
                    ["overwrite"] = new McpToolParameter { Type = "boolean", Description = "另存为时是否覆盖已有文件", Required = false },
                    ["updateActiveSave"] = new McpToolParameter { Type = "boolean", Description = "保存后设为当前存档；默认 true", Required = false },
                    ["index"] = new McpToolParameter { Type = "integer", Description = "save list 返回的存档 index", Required = false },
                    ["path"] = new McpToolParameter { Type = "string", Description = "load/start 的完整存档路径；restart_load 使用当前存档", Required = false },
                    ["forceLoad"] = new McpToolParameter { Type = "boolean", Description = "start：已在游戏内时仍加载指定存档；默认 false", Required = false },
                    ["resume"] = new McpToolParameter { Type = "boolean", Description = "start 默认 true；restart_load 默认 false", Required = false },
                    ["jobId"] = new McpToolParameter { Type = "string", Description = "restart_status 的任务 ID", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "quit 的退出目标；默认 menu", Required = false, EnumValues = new List<string> { "menu", "desktop" } },
                    ["saveFirst"] = new McpToolParameter { Type = "boolean", Description = "退出前保存；默认 false", Required = false },
                    ["includeCosmetic"] = new McpToolParameter { Type = "boolean", Description = "dlc list 是否包含装饰内容；默认 false", Required = false },
                    ["dlcId"] = new McpToolParameter { Type = "string", Description = "activate 的 DLC ID", Required = false },
                    ["uiAction"] = new McpToolParameter { Type = "string", Description = "ui trigger 的 Action 名称", Required = false },
                    ["screen"] = new McpToolParameter { Type = "string", Description = "open_management 的页面名", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "notification 标题", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "通知或提示正文", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "popup/speech_bubble 文字", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "speech_bubble 秒数；默认 5，范围 0.5–30", Required = false },
                    ["markerAction"] = new McpToolParameter { Type = "string", Description = "marker 动作", Required = false, EnumValues = new List<string> { "create", "list", "clear" } },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "UI 提示或标记的 X 坐标", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "UI 提示或标记的 Y 坐标", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "speech_bubble 的复制人 InstanceID", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "restart_load：只预览，不重启", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认底层要求确认的写操作", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args?["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "launch":
                        case "start":
                        case "startup":
                            return GameLaunchTools.ControlGameLaunch().Handler(args);
                        case "speed":
                        case "time":
                            return ControlGameSpeed().Handler(args);
                        case "state":
                        case "red_alert":
                            return ControlGameState().Handler(args);
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
                            return CallToolResult.Error("Unsupported domain. Use launch, speed, state, save, dlc, or ui. Only normal gameplay is supported.");
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
    }
}
