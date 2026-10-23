using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OptionControlTools
    {
        public static McpTool ListOptionControls()
        {
            return new McpTool
            {
                Name = "side_options_list",
                Hidden = true,
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Tags = new List<string> { "controls", "side-screen", "direction", "few-option", "broadcast", "radbolt" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=option action=list。列出非 slider/threshold 的选项型侧屏控件：工作方向、少量选项、逻辑广播频道、辐射粒子方向",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "过滤类型：any、direction、few_option、broadcast_receiver、radbolt_direction，默认 any", Required = false, EnumValues = new List<string> { "any", "direction", "few_option", "broadcast_receiver", "radbolt_direction" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、选项 tag 或广播器名筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "是否返回可选项/可用广播器，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string kind = (args["kind"]?.ToString() ?? "any").Trim().ToLowerInvariant();
                    string query = args["query"]?.ToString();
                    bool includeOptions = ToolUtil.GetBool(args, "includeOptions", true);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var results = new List<Dictionary<string, object>>();
                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        var go = building?.gameObject;
                        if (!MatchesTarget(go, rect, worldId))
                            continue;
                        var info = ControlInfo(go, includeOptions);
                        var kinds = (List<string>)info["controlKinds"];
                        if (kinds.Count == 0)
                            continue;
                        if (kind != "any" && !kinds.Contains(kind))
                            continue;
                        if (!MatchesQuery(info, query))
                            continue;

                        results.Add(info);
                        if (results.Count >= limit)
                            break;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["kind"] = kind,
                        ["controls"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetOptionControl()
        {
            return new McpTool
            {
                Name = "side_option_set",
                Hidden = true,
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Tags = new List<string> { "controls", "side-screen", "direction", "few-option", "broadcast", "radbolt" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=option action=set。用 kind 参数选择 direction、few_option、broadcast_receiver 或 radbolt_direction。",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "控件类型：direction、few_option、broadcast_receiver、radbolt_direction", Required = true, EnumValues = new List<string> { "direction", "few_option", "broadcast_receiver", "radbolt_direction" } },
                    ["direction"] = new McpToolParameter { Type = "string", Description = "kind=direction 时为 Any/Left/Right；kind=radbolt_direction 时为 Up/UpLeft/Left/DownLeft/Down/DownRight/Right/UpRight", Required = false },
                    ["tag"] = new McpToolParameter { Type = "string", Description = "kind=few_option 时的目标选项 tag；可先用 building_control domain=side_surface surface=option action=list kind=few_option 查询", Required = false },
                    ["broadcasterId"] = new McpToolParameter { Type = "integer", Description = "kind=broadcast_receiver 时的目标 LogicBroadcaster InstanceID；clear=true 时可省略", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "kind=broadcast_receiver 时是否清空当前频道", Required = false }
                }),
                Handler = args => SetOptionControl(args, null)
            };
        }

        public static McpTool ControlSideOption()
        {
            return new McpTool
            {
                Name = "side_option_control",
                Hidden = true,
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Tags = new List<string> { "controls", "side-screen", "direction", "few-option", "broadcast", "radbolt" },
                Description = "选项型侧屏聚合工具：action=list/set；读取或设置工作方向、少量选项、逻辑广播频道和辐射粒子方向。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set", Required = true, EnumValues = new List<string> { "list", "set" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标对象 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标格子 Y", Required = false },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "action=list 时为 any/direction/few_option/broadcast_receiver/radbolt_direction；action=set 时为 direction/few_option/broadcast_receiver/radbolt_direction", Required = false, EnumValues = new List<string> { "any", "direction", "few_option", "broadcast_receiver", "radbolt_direction" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、选项 tag 或广播器名筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可选项/可用广播器，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                    ["direction"] = new McpToolParameter { Type = "string", Description = "action=set kind=direction 时为 Any/Left/Right；kind=radbolt_direction 时为八方向", Required = false },
                    ["tag"] = new McpToolParameter { Type = "string", Description = "action=set kind=few_option 时的目标选项 tag", Required = false },
                    ["broadcasterId"] = new McpToolParameter { Type = "integer", Description = "action=set kind=broadcast_receiver 时的目标 LogicBroadcaster InstanceID；clear=true 时可省略", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "action=set kind=broadcast_receiver 时是否清空当前频道", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListOptionControls().Handler(args);
                    if (action == "set")
                        return SetOptionControl().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

    }
}
