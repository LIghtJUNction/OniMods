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
    public static class RocketCrewCargoTools
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

        public static McpTool ControlCargoStatus()
        {
            return new McpTool
            {
                Name = "rocket_cargo_status_control",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "rocket_cargo_and_harvest_status", "rocket_cargo_harvest_control" },
                Tags = new List<string> { "rocket", "cargo", "collector", "harvest", "diamond", "side-screen" },
                Description = "读取火箭货舱收集器和太空钻探模块状态：action=collectors 或 harvest_modules",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：collectors、harvest_modules", Required = true, EnumValues = new List<string> { "collectors", "harvest_modules" } },
                    ["rocketId"] = new McpToolParameter { Type = "integer", Description = "可选火箭 InstanceID", Required = false },
                    ["rocketName"] = new McpToolParameter { Type = "string", Description = "可选火箭名称", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按火箭、模块、prefabId 或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "collectors")
                        return ListCargoCollectors().Handler(args);
                    if (action == "harvest_modules")
                        return ListHarvestModules().Handler(args);
                    return CallToolResult.Error("action must be collectors or harvest_modules");
                }
            };
        }

        public static McpTool ListCargoCollectors()
        {
            return new McpTool
            {
                Name = "rocket_cargo_collectors_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "cargo_module_status_list", "hex_cell_collectors_list" },
                Tags = new List<string> { "rocket", "cargo", "collector", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=rocket rocketDomain=cargo_status action=collectors。列出 CargoModuleSideScreen 星图货舱收集模块容量、库存和收集进度",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["rocketId"] = new McpToolParameter { Type = "integer", Description = "可选火箭 InstanceID", Required = false },
                    ["rocketName"] = new McpToolParameter { Type = "string", Description = "可选火箭名称", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按火箭、模块、prefabId 或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    var craftFilter = FindRocket(args);
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var rows = EnumerateCargoCollectors(craftFilter)
                        .Select(item => CargoCollectorInfo(item.craft, item.module, item.collector))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["rocketName"].ToString())
                        .ThenBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["collectors"] = rows
                    });
                }
            };
        }

        private static IEnumerable<AssignmentGroupController> EnumerateAssignmentGroups()
        {
            return UnityEngine.Object.FindObjectsByType<AssignmentGroupController>(FindObjectsSortMode.None)
                .Where(group => group != null && !string.IsNullOrWhiteSpace(group.AssignmentGroupID));
        }

        private static AssignmentGroupController FindAssignmentGroupController(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "controllerId") ?? ToolUtil.GetInt(args, "id");
            string groupId = args["groupId"]?.ToString();
            string query = args["query"]?.ToString();

            foreach (var group in EnumerateAssignmentGroups())
            {
                var kpid = group.GetComponent<KPrefabID>();
                if (id.HasValue && ((kpid != null && kpid.InstanceID == id.Value) || group.GetInstanceID() == id.Value))
                    return group;
                if (!string.IsNullOrWhiteSpace(groupId) && string.Equals(group.AssignmentGroupID, groupId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return group;
                if (!string.IsNullOrWhiteSpace(query) && MatchesQuery(TargetInfo(group.gameObject), query))
                    return group;
            }

            return null;
        }

        private static MinionAssignablesProxy FindAssignableProxy(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "dupeId");
            string name = args["dupeName"]?.ToString();

            foreach (var proxy in Components.MinionAssignablesProxy.Items)
            {
                if (proxy == null)
                    continue;

                var proxyKpid = proxy.GetComponent<KPrefabID>();
                var target = proxy.GetTargetGameObject();
                var targetKpid = target?.GetComponent<KPrefabID>();
                if (id.HasValue && ((proxyKpid != null && proxyKpid.InstanceID == id.Value) || (targetKpid != null && targetKpid.InstanceID == id.Value)))
                    return proxy;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(proxy.GetProperName(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return proxy;
            }

            return null;
        }

        private static Dictionary<string, object> AssignmentGroupInfo(AssignmentGroupController group, bool includeDupes)
        {
            var result = TargetInfo(group.gameObject);
            var world = AssignmentGroupWorld(group);
            result["groupId"] = group.AssignmentGroupID;
            result["worldId"] = world?.id ?? group.GetMyWorldId();
            result["memberCount"] = group.GetMembers().Count;
            if (includeDupes)
            {
                result["dupes"] = Components.MinionAssignablesProxy.Items
                    .Where(proxy => proxy != null && proxy.GetTargetGameObject() != null && !proxy.GetTargetGameObject().HasTag(GameTags.Dead))
                    .Select(proxy => AssignmentGroupDupeInfo(group, proxy, world))
                    .OrderByDescending(info => (bool)info["isSameWorld"])
                    .ThenByDescending(info => (bool)info["isPilot"])
                    .ThenBy(info => info["name"].ToString())
                    .ToList();
            }
            return result;
        }

        private static WorldContainer AssignmentGroupWorld(AssignmentGroupController group)
        {
            var world = group.GetMyWorld();
            var exteriorDoor = group.GetComponent<ClustercraftExteriorDoor>();
            var interiorDoor = exteriorDoor?.GetInteriorDoor();
            if (interiorDoor != null)
                world = interiorDoor.GetMyWorld();
            return world;
        }

        private static Dictionary<string, object> AssignmentGroupDupeInfo(AssignmentGroupController group, MinionAssignablesProxy proxy, WorldContainer groupWorld)
        {
            var target = proxy.GetTargetGameObject();
            var resume = target?.GetComponent<MinionResume>();
            var targetWorld = target?.GetMyWorld();
            bool isSameWorld = groupWorld != null && targetWorld != null && targetWorld.ParentWorldId == groupWorld.ParentWorldId;
            var proxyKpid = proxy.GetComponent<KPrefabID>();
            var targetKpid = target?.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["proxyId"] = proxyKpid?.InstanceID ?? proxy.GetInstanceID(),
                ["dupeId"] = targetKpid?.InstanceID ?? -1,
                ["name"] = proxy.GetProperName(),
                ["isMember"] = group.CheckMinionIsMember(proxy),
                ["isSameWorld"] = isSameWorld,
                ["worldId"] = targetWorld?.id ?? -1,
                ["isPilot"] = resume != null && resume.HasPerk(Db.Get().SkillPerks.CanUseRocketControlStation)
            };
        }

        public static McpTool ListHarvestModules()
        {
            return new McpTool
            {
                Name = "rocket_harvest_modules_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "space_miner_modules_list", "harvest_module_status_list" },
                Tags = new List<string> { "rocket", "harvest", "diamond", "poi", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=rocket rocketDomain=cargo_status action=harvest_modules。列出 HarvestModuleSideScreen 太空钻探模块钻探状态、钻石库存和容量",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["rocketId"] = new McpToolParameter { Type = "integer", Description = "可选火箭 InstanceID", Required = false },
                    ["rocketName"] = new McpToolParameter { Type = "string", Description = "可选火箭名称", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按火箭、模块、prefabId 或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    var craftFilter = FindRocket(args);
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var rows = EnumerateHarvestModules(craftFilter)
                        .Select(item => HarvestModuleInfo(item.craft, item.module, item.harvest))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["rocketName"].ToString())
                        .ThenBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["harvestModules"] = rows
                    });
                }
            };
        }

        private static IEnumerable<(Clustercraft craft, PassengerRocketModule passenger)> EnumeratePassengerModules(Clustercraft craftFilter)
        {
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null || (craftFilter != null && craft != craftFilter))
                    continue;
                var passenger = craft.ModuleInterface?.GetPassengerModule();
                if (passenger != null)
                    yield return (craft, passenger);
            }
        }

        private static IEnumerable<(Clustercraft craft, RocketModuleCluster module, IHexCellCollector collector)> EnumerateCargoCollectors(Clustercraft craftFilter)
        {
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null || (craftFilter != null && craft != craftFilter) || craft.ModuleInterface == null)
                    continue;
                foreach (var clusterModule in craft.ModuleInterface.ClusterModules)
                {
                    var module = clusterModule.Get();
                    if (module == null)
                        continue;
                    var collector = module.GetComponent<IHexCellCollector>() ?? module.GetSMI<IHexCellCollector>();
                    if (collector != null)
                        yield return (craft, module, collector);
                }
            }
        }

        private static IEnumerable<(Clustercraft craft, RocketModuleCluster module, ResourceHarvestModule.StatesInstance harvest)> EnumerateHarvestModules(Clustercraft craftFilter)
        {
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null || (craftFilter != null && craft != craftFilter) || craft.ModuleInterface == null)
                    continue;
                foreach (var clusterModule in craft.ModuleInterface.ClusterModules)
                {
                    var module = clusterModule.Get();
                    if (module == null || module.gameObject.GetDef<ResourceHarvestModule.Def>() == null)
                        continue;
                    var harvest = module.GetSMI<ResourceHarvestModule.StatesInstance>();
                    if (harvest != null)
                        yield return (craft, module, harvest);
                }
            }
        }

        private static (Clustercraft craft, PassengerRocketModule passenger) FindPassengerModule(JObject args)
        {
            int? moduleId = ToolUtil.GetInt(args, "moduleId");
            var craftFilter = FindRocket(args);
            foreach (var item in EnumeratePassengerModules(craftFilter))
            {
                var kpid = item.passenger.GetComponent<KPrefabID>();
                if (!moduleId.HasValue || (kpid != null && kpid.InstanceID == moduleId.Value))
                    return item;
            }
            return (null, null);
        }

        private static Clustercraft FindRocket(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "rocketId");
            string name = args["rocketName"]?.ToString();
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null)
                    continue;
                var kpid = craft.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return craft;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(craft.Name, name, StringComparison.OrdinalIgnoreCase))
                    return craft;
            }
            return null;
        }

        private static Dictionary<string, object> CrewRequestInfo(Clustercraft craft, PassengerRocketModule passenger)
        {
            var boarded = passenger.GetCrewBoardedFraction();
            var pilot = passenger.GetDupePilot();
            var robotPilot = craft.ModuleInterface?.GetRobotPilotModule();
            var result = TargetInfo(passenger.gameObject);
            result["rocketId"] = craft.GetComponent<KPrefabID>()?.InstanceID ?? craft.GetInstanceID();
            result["rocketName"] = craft.Name;
            result["requestState"] = passenger.PassengersRequested == PassengerRocketModule.RequestCrewState.Request ? "request" : "release";
            result["crewAssigned"] = passenger.HasCrewAssigned();
            result["crewCount"] = passenger.GetCrewCount();
            result["crewBoarded"] = boarded.first;
            result["crewRequired"] = boarded.second;
            result["passengersBoarded"] = passenger.CheckPassengersBoarded(robotPilot == null);
            result["extraPassengers"] = passenger.CheckExtraPassengers();
            result["dupePilot"] = pilot == null ? null : DupeInfo(pilot.GetComponent<MinionIdentity>());
            result["hasRoboPilot"] = robotPilot != null;
            result["state"] = CrewUiState(passenger, robotPilot, boarded);
            return result;
        }

        private static string CrewUiState(PassengerRocketModule passenger, RoboPilotModule robotPilot, Tuple<int, int> boarded)
        {
            if (passenger.GetCrewCount() <= 0)
                return robotPilot != null ? "no_crew_needed" : "no_crew_found";
            if (passenger.PassengersRequested == PassengerRocketModule.RequestCrewState.Release)
                return "public_access";
            return boarded.first >= boarded.second ? "ready" : "awaiting_crew";
        }

        private static Dictionary<string, object> CargoCollectorInfo(Clustercraft craft, RocketModuleCluster module, IHexCellCollector collector)
        {
            var result = TargetInfo(module.gameObject);
            float capacity = collector.GetCapacity();
            float stored = collector.GetMassStored();
            result["rocketId"] = craft.GetComponent<KPrefabID>()?.InstanceID ?? craft.GetInstanceID();
            result["rocketName"] = craft.Name;
            result["displayName"] = collector.GetProperName();
            result["collecting"] = collector.CheckIsCollecting();
            result["massStoredKg"] = Math.Round(ToolUtil.SafeFloat(stored), 3);
            result["capacityKg"] = Math.Round(ToolUtil.SafeFloat(capacity), 3);
            result["remainingCapacityKg"] = Math.Round(ToolUtil.SafeFloat(Math.Max(0f, capacity - stored)), 3);
            result["fill"] = capacity > 0f ? Math.Round(ToolUtil.SafeFloat(stored / capacity), 3) : 0;
            result["timeInState"] = Math.Round(ToolUtil.SafeFloat(collector.TimeInState()), 3);
            result["capacityText"] = collector.GetCapacityBarText();
            result["status"] = collector.CheckIsCollecting() ? "collecting" : (capacity > 0f && stored >= capacity ? "full" : "stopped");
            return result;
        }

        private static Dictionary<string, object> HarvestModuleInfo(Clustercraft craft, RocketModuleCluster module, ResourceHarvestModule.StatesInstance harvest)
        {
            var storage = harvest.GetComponent<Storage>();
            float stored = storage?.MassStored() ?? 0f;
            float capacity = storage?.Capacity() ?? 0f;
            bool canHarvest = harvest.sm.canHarvest.Get(harvest);
            var result = TargetInfo(module.gameObject);
            result["rocketId"] = craft.GetComponent<KPrefabID>()?.InstanceID ?? craft.GetInstanceID();
            result["rocketName"] = craft.Name;
            result["canHarvest"] = canHarvest;
            result["status"] = canHarvest ? "mining" : "stopped";
            result["diamondStoredKg"] = Math.Round(ToolUtil.SafeFloat(stored), 3);
            result["diamondCapacityKg"] = Math.Round(ToolUtil.SafeFloat(capacity), 3);
            result["fill"] = capacity > 0f ? Math.Round(ToolUtil.SafeFloat(stored / capacity), 3) : 0;
            result["timeInState"] = Math.Round(ToolUtil.SafeFloat(harvest.timeinstate), 3);
            result["maxExtractFromDiamondKg"] = Math.Round(ToolUtil.SafeFloat(harvest.GetMaxExtractKGFromDiamondAvailable()), 3);
            return result;
        }

        private static Dictionary<string, object> DupeInfo(MinionIdentity dupe)
        {
            if (dupe == null)
                return null;
            return new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? dupe.GetInstanceID(),
                ["name"] = dupe.GetProperName(),
                ["worldId"] = dupe.GetMyWorldId()
            };
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static Dictionary<string, McpToolParameter> PassengerLookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["rocketId"] = new McpToolParameter { Type = "integer", Description = "目标火箭 InstanceID", Required = false },
                ["rocketName"] = new McpToolParameter { Type = "string", Description = "目标火箭名称", Required = false },
                ["moduleId"] = new McpToolParameter { Type = "integer", Description = "目标 PassengerRocketModule InstanceID；可选", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static CallToolResult JsonResult(Dictionary<string, object> payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
