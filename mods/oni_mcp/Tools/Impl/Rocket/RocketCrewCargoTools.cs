using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class RocketCrewCargoTools
    {
        public static McpTool ListCrewRequests()
        {
            return new McpTool
            {
                Name = "rocket_crew_requests_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "summon_crew_list", "rocket_passenger_requests_list" },
                Tags = new List<string> { "rocket", "crew", "passenger", "summon", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=rocket rocketDomain=crew_request action=list。列出 SummonCrewSideScreen 乘员召集/释放状态、登船人数和驾驶员状态",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["rocketId"] = new McpToolParameter { Type = "integer", Description = "可选火箭 InstanceID", Required = false },
                    ["rocketName"] = new McpToolParameter { Type = "string", Description = "可选火箭名称", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按火箭名、模块名或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    var craftFilter = FindRocket(args);
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var rows = EnumeratePassengerModules(craftFilter)
                        .Select(item => CrewRequestInfo(item.craft, item.passenger))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["rocketName"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["crewRequests"] = rows
                    });
                }
            };
        }

        public static McpTool SetCrewRequest()
        {
            return new McpTool
            {
                Name = "rocket_crew_request_set",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "summon_crew_set", "rocket_passenger_request_set" },
                Tags = new List<string> { "rocket", "crew", "passenger", "summon", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=rocket rocketDomain=crew_request action=set。设置 SummonCrewSideScreen 按钮状态：request=召集乘员登船，release=开放/释放乘员",
                Parameters = PassengerLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["state"] = new McpToolParameter { Type = "string", Description = "request 或 release", Required = true, EnumValues = new List<string> { "request", "release" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改乘员登船请求，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    var found = FindPassengerModule(args);
                    if (found.passenger == null)
                        return CallToolResult.Error("Target passenger rocket module not found");
                    string state = (args["state"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = CrewRequestInfo(found.craft, found.passenger);
                    if (state == "request")
                        found.passenger.RequestCrewBoard(PassengerRocketModule.RequestCrewState.Request);
                    else if (state == "release")
                        found.passenger.RequestCrewBoard(PassengerRocketModule.RequestCrewState.Release);
                    else
                        return CallToolResult.Error("state must be request or release");

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(found.passenger.gameObject),
                        ["before"] = before,
                        ["crewRequest"] = CrewRequestInfo(found.craft, found.passenger)
                    });
                }
            };
        }

        public static McpTool ControlCrewRequest()
        {
            return new McpTool
            {
                Name = "rocket_crew_request_control",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "summon_crew_control", "rocket_passenger_request_control" },
                Tags = new List<string> { "rocket", "crew", "passenger", "summon", "side-screen" },
                Description = "统一读取和设置火箭乘员召集/释放状态。action=list/set；set 需 confirm=true。",
                Parameters = PassengerLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list、set", Required = true },
                    ["state"] = new McpToolParameter { Type = "string", Description = "action=set 时为 request 或 release", Required = false, EnumValues = new List<string> { "request", "release" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按火箭名、模块名或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=set 时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListCrewRequests().Handler(args);
                    if (action == "set")
                        return SetCrewRequest().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

        public static McpTool ListAssignmentGroups()
        {
            return new McpTool
            {
                Name = "assignment_groups_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "rocket_assignment_groups_list", "crew_assignment_groups_list" },
                Tags = new List<string> { "rocket", "crew", "assignment", "group", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=rocket rocketDomain=assignment_group action=list。列出 AssignmentGroupControllerSideScreen 分配组，以及每个复制人的成员状态、同世界状态和驾驶员资格",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["groupId"] = new McpToolParameter { Type = "string", Description = "可选 AssignmentGroupID 精确匹配", Required = false },
                    ["controllerId"] = new McpToolParameter { Type = "integer", Description = "可选分配组控制器对象 InstanceID", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象名、prefabId、groupId 或复制人名筛选", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否返回每个复制人的成员状态，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string query = args["query"]?.ToString();
                    bool includeDupes = ToolUtil.GetBool(args, "includeDupes", true);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var target = FindAssignmentGroupController(args);

                    var rows = EnumerateAssignmentGroups()
                        .Where(group => target == null || group == target)
                        .Select(group => AssignmentGroupInfo(group, includeDupes))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["assignmentGroups"] = rows
                    });
                }
            };
        }

        public static McpTool SetAssignmentGroupMember()
        {
            return new McpTool
            {
                Name = "assignment_group_member_set",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "rocket_assignment_group_member_set", "crew_assignment_group_member_set" },
                Tags = new List<string> { "rocket", "crew", "assignment", "group", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=rocket rocketDomain=assignment_group action=set。设置 AssignmentGroupControllerSideScreen 中单个复制人是否属于指定分配组，等价于点击该复制人的成员开关",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["groupId"] = new McpToolParameter { Type = "string", Description = "AssignmentGroupID；controllerId 为空时使用", Required = false },
                    ["controllerId"] = new McpToolParameter { Type = "integer", Description = "分配组控制器对象 InstanceID；groupId 为空时使用", Required = false },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "复制人或 MinionAssignablesProxy InstanceID；dupeName 为空时使用", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "复制人名称；dupeId 为空时使用", Required = false },
                    ["isMember"] = new McpToolParameter { Type = "boolean", Description = "true 加入分配组，false 移出分配组", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改分配组成员，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var group = FindAssignmentGroupController(args);
                    if (group == null)
                        return CallToolResult.Error("Assignment group controller not found; use building_control domain=rocket rocketDomain=assignment_group action=list first");

                    var proxy = FindAssignableProxy(args);
                    if (proxy == null)
                        return CallToolResult.Error("Duplicant assignable proxy not found");

                    bool isMember = ToolUtil.GetBool(args, "isMember", false);
                    var before = AssignmentGroupInfo(group, includeDupes: true);
                    group.SetMember(proxy, isMember);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(group.gameObject),
                        ["dupe"] = AssignmentGroupDupeInfo(group, proxy, AssignmentGroupWorld(group)),
                        ["before"] = before,
                        ["after"] = AssignmentGroupInfo(group, includeDupes: true)
                    });
                }
            };
        }

        public static McpTool ControlAssignmentGroup()
        {
            return new McpTool
            {
                Name = "assignment_group_control",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "rocket_assignment_group_control", "crew_assignment_group_control" },
                Tags = new List<string> { "rocket", "crew", "assignment", "group", "side-screen" },
                Description = "统一读取和设置火箭/分配组成员。action=list/set；set 需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list、set", Required = true },
                    ["groupId"] = new McpToolParameter { Type = "string", Description = "AssignmentGroupID；controllerId 为空时使用", Required = false },
                    ["controllerId"] = new McpToolParameter { Type = "integer", Description = "分配组控制器对象 InstanceID；groupId 为空时使用", Required = false },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "action=set 时复制人或 MinionAssignablesProxy InstanceID；dupeName 为空时使用", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "action=set 时复制人名称；dupeId 为空时使用", Required = false },
                    ["isMember"] = new McpToolParameter { Type = "boolean", Description = "action=set 时 true 加入分配组，false 移出分配组", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按对象名、prefabId、groupId 或复制人名筛选", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回每个复制人的成员状态，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=set 时必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListAssignmentGroups().Handler(args);
                    if (action == "set")
                        return SetAssignmentGroupMember().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

    }
}
