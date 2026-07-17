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
        public static McpTool ControlGame()
        {
            return new McpTool
            {
                Name = "game_control",
                Group = "game",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "game_system_control" },
                Tags = new List<string> { "game", "speed", "pause", "save", "launch", "dlc", "red-alert", "sandbox", "debug", "map", "ui", "feedback" },
                Description = "统一游戏入口。domain=launch/speed/state/save/dlc/sandbox/ui；launch 支持启动、持久化 Steam 重启加载与任务状态查询；sandbox 下 kind=read/area/entity/map_designate；ui 下 uiDomain=action/feedback。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "游戏子系统：launch、speed、state、save、dlc、sandbox、ui", Required = true, EnumValues = new List<string> { "launch", "speed", "state", "save", "dlc", "sandbox", "ui" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "子系统动作：launch=status/start/restart_load/restart_status；speed=time/pause/resume/set_speed；state=red_alert_status/set_red_alert/set_sandbox_mode；save=list/save/load/quit；dlc=list/activate；sandbox/ui 按各 kind/uiDomain 路由", Required = true },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "domain=sandbox 时的子域：read、area、entity 或 map_designate，默认 read；domain=ui uiDomain=action action=list 时过滤类型", Required = false, EnumValues = new List<string> { "read", "area", "entity", "map_designate", "all", "management", "overlay", "build", "navigation" } },
                    ["uiDomain"] = new McpToolParameter { Type = "string", Description = "domain=ui 时的 UI 子域：action、feedback", Required = false, EnumValues = new List<string> { "action", "feedback" } },
                    ["speed"] = new McpToolParameter { Type = "integer", Description = "domain=speed action=set_speed 时的速度等级：0=暂停, 1=正常, 2=快进, 3=超快", Required = false },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "domain=state 写动作 true=开启，false=关闭", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "domain=state 的世界 ID，默认当前激活世界", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "domain=state 是否应用/读取全部已加载世界，默认 false", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "domain=save list/load index 查找范围：local、cloud 或 both，默认 both", Required = false, EnumValues = new List<string> { "local", "cloud", "both" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "读取动作最多返回数量", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "domain=save 的另存为文件名且不能包含目录分隔符；domain=ui uiDomain=feedback action=speech_bubble 时复制人名称", Required = false },
                    ["overwrite"] = new McpToolParameter { Type = "boolean", Description = "domain=save action=save 时目标文件已存在是否覆盖", Required = false },
                    ["updateActiveSave"] = new McpToolParameter { Type = "boolean", Description = "domain=save action=save 成功后是否设为当前 active save，默认 true", Required = false },
                    ["index"] = new McpToolParameter { Type = "integer", Description = "domain=save action=load 时 game_control domain=save action=list 返回的 index", Required = false },
                    ["path"] = new McpToolParameter { Type = "string", Description = "domain=save action=load 或 domain=launch action=start 时完整存档路径；restart_load 固定保存并重载当前 active save", Required = false },
                    ["forceLoad"] = new McpToolParameter { Type = "boolean", Description = "domain=launch action=start：已在游戏内时是否仍强制加载目标存档，默认 false", Required = false },
                    ["resume"] = new McpToolParameter { Type = "boolean", Description = "domain=launch：start 默认 true；restart_load 默认 false", Required = false },
                    ["jobId"] = new McpToolParameter { Type = "string", Description = "domain=launch action=restart_status 可选任务 ID", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "domain=save action=quit 时 menu 退出到主菜单；desktop 退出程序，默认 menu", Required = false, EnumValues = new List<string> { "menu", "desktop" } },
                    ["saveFirst"] = new McpToolParameter { Type = "boolean", Description = "domain=save action=quit 时退出前是否先保存，默认 false", Required = false },
                    ["includeCosmetic"] = new McpToolParameter { Type = "boolean", Description = "domain=dlc action=list 时是否包含 cosmetic/content-only DLC，默认 false", Required = false },
                    ["dlcId"] = new McpToolParameter { Type = "string", Description = "domain=dlc action=activate 时的 DLC id，如 DLC2_ID、DLC3_ID、DLC4_ID、DLC5_ID", Required = false },
                    ["uiAction"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=action action=trigger 时的 Action 枚举名", Required = false },
                    ["screen"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=action action=open_management 时的页面名", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=feedback action=notification 时通知标题", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "domain=ui 的通知正文或提示内容，按 action 解释", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=feedback action=popup/speech_bubble 时显示文字；speech_bubble 必填", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "domain=ui uiDomain=feedback action=speech_bubble 时显示秒数，默认 5，范围 0.5-30", Required = false },
                    ["markerAction"] = new McpToolParameter { Type = "string", Description = "domain=ui uiDomain=feedback action=marker 时的子动作：create/list/clear", Required = false, EnumValues = new List<string> { "create", "list", "clear" } },
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
                    ["id"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 目标对象 InstanceID；domain=ui uiDomain=feedback action=speech_bubble 时复制人 InstanceID", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "domain=sandbox kind=read action=list_story_traits 时搜索词", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "domain=sandbox 元素质量 kg，按 action 解释", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "domain=sandbox 温度 K，按 action 解释", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "domain=sandbox 病菌 ID，默认无", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 每格病菌数量，默认 0", Required = false },
                    ["matchMode"] = new McpToolParameter { Type = "string", Description = "domain=sandbox kind=map_designate 匹配处理：unique/first/all，默认 unique", Required = false, EnumValues = new List<string> { "unique", "first", "all" } },
                    ["matchIndex"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox kind=map_designate 多匹配时选择第几个，0 基", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "domain=sandbox 区域/搜索安全上限", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "domain=launch action=restart_load 或 sandbox 写动作只预览不修改", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "domain=sandbox kind=map_designate 搜索时是否把未揭示格视为 unk，默认 false", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "domain=sandbox 允许绕过对应底层工具的沙盒模式或 InstantBuild 要求，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "底层写入/危险动作需要 true", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "").Trim().ToLowerInvariant();
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


    }
}
