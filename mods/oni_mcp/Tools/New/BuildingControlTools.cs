using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class BuildingControlTools
    {
        public static McpTool ControlBuilding()
        {
            return new McpTool
            {
                Name = "building_control",
                Group = "buildings",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "buildings_control", "building_system_control" },
                Tags = new List<string> { "buildings", "planning", "config", "production", "storage", "facility", "side-screen", "materials", "preview", "rocket", "space", "auto-connect", "wire", "power", "conduit", "utility" },
                Description = "建筑域统一入口：domain=planning/config/production/storage/filter/tile_selection/receptacle/side_surface/space_building/space_story/special/story_facility/rocket。优先用 action + query/target/search/id/areaId 定位和执行；x/y 坐标仅作精确 fallback。规划建造、建筑配置、生产队列、储存/过滤/插槽、对象侧屏、特殊/剧情/太空设施和火箭系统都通过 domain 参数路由；planning 支持 parse_plan/build_area/auto_connect，用于一步放置并连接电线、电力线、管道和运输轨道；保留各子工具 action/kind/confirm 规则。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "planning、config、production、storage、filter、tile_selection、receptacle、side_surface、space_building、space_story、special、story_facility 或 rocket", Required = true, EnumValues = new List<string> { "planning", "config", "production", "storage", "filter", "tile_selection", "receptacle", "side_surface", "space_building", "space_story", "special", "story_facility", "rocket" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "子动作。planning=parse_plan/search_defs/materials/preview/placement_candidates/auto_connect/build_area；config=list/list_automation/set_*；production=list_fabricators/list_recipes/set/batch/mutant_seed_*；storage=list/detail/set_filter；filter=list/set；tile_selection=list/set/batch；receptacle=list/request/cancel_request/remove_occupant/cancel_remove/batch；side_surface=list/press/focus/batch/list_rewards/claim；rocket=ops/module/flight_utility/restriction/usage/crew_request/assignment_group/cargo_status/self_destruct 的子动作；facility=list/set/assign/consume 等", Required = false },
                    ["surface"] = new McpToolParameter { Type = "string", Description = "domain=side_surface 的原侧屏领域：generic/option/activation/automation/facility/misc/geo_tuner/user_menu/maintenance", Required = false },
                    ["rocketDomain"] = new McpToolParameter { Type = "string", Description = "domain=rocket 的原火箭子系统：ops/module/flight_utility/restriction/usage/crew_request/assignment_group/cargo_status/self_destruct", Required = false },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "config/facility/filter/side_surface 子类型；filter 支持 any/single/tree/flat；side_surface 支持 button/checklist/progress/related/automatable/critter_sensor 等；例如 artable、comet_detector、light、pixel_pack", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索或筛选词，按子动作语义使用", Required = false },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "planning 材料/预览/候选目标建筑 prefabId；可由 plan/blueprint/sequence 自动解析", Required = false },
                    ["plan"] = new McpToolParameter { Type = "string", Description = "planning 文字建造序列，解析成 prefabId/material/query，例如 粉砂岩砖@氧气、Wire-小型冰箱、用铜矿造手动发电机在电池旁", Required = false },
                    ["blueprint"] = new McpToolParameter { Type = "string", Description = "plan 的别名", Required = false },
                    ["sequence"] = new McpToolParameter { Type = "string", Description = "plan 的别名；用于搜索/行动一体化传文字序列", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "plan 的别名", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "planning preview/auto_connect 材料；支持 auto", Required = false },
                    ["recipeId"] = new McpToolParameter { Type = "string", Description = "production set/batch/list_recipes 的目标配方 ID", Required = false },
                    ["categoryId"] = new McpToolParameter { Type = "string", Description = "production list_recipes 的配方分类 ID", Required = false },
                    ["count"] = new McpToolParameter { Type = "integer", Description = "production set/batch 的队列数量，按 mode 解释", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "production batch 的批量队列项", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "production batch 的默认参数", Required = false },
                    ["queuedOnly"] = new McpToolParameter { Type = "boolean", Description = "production list 时是否只返回有队列/工作项的制作站或配方", Required = false },
                    ["includeRecipes"] = new McpToolParameter { Type = "boolean", Description = "production list_fabricators 时是否附带配方摘要", Required = false },
                    ["includeLocked"] = new McpToolParameter { Type = "boolean", Description = "production list_recipes 时是否包含科技未解锁配方", Required = false },
                    ["forbid"] = new McpToolParameter { Type = "boolean", Description = "production set_mutant_seeds：true=拒收突变种子", Required = false },
                    ["resource"] = new McpToolParameter { Type = "string", Description = "domain=storage action=list 时按储存过滤标签或建筑名筛选", Required = false },
                    ["tag"] = new McpToolParameter { Type = "string", Description = "domain=filter action=set kind=single 时的目标 tag/元素", Required = false },
                    ["tags"] = new McpToolParameter { Type = "array", Description = "domain=storage/filter 写入时的 tag 列表", Required = false },
                    ["itemTag"] = new McpToolParameter { Type = "string", Description = "domain=tile_selection action=set 时的目标物品 tag", Required = false },
                    ["entityTag"] = new McpToolParameter { Type = "string", Description = "domain=receptacle action=request 时的实体 tag", Required = false },
                    ["additionalTag"] = new McpToolParameter { Type = "string", Description = "domain=receptacle action=request 时的附加 tag", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "domain=filter/tile_selection 时清空当前选择", Required = false },
                    ["replaceExistingRequest"] = new McpToolParameter { Type = "boolean", Description = "domain=receptacle action=request 时是否替换现有请求，默认 true", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑/设施 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标或锚点 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标或锚点 Y", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点 X，按子动作语义使用", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点 Y，按子动作语义使用", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点 X，按子动作语义使用", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点 Y，按子动作语义使用", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄，按子动作语义使用", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，按子动作语义使用", Required = false },
                    ["limit"] = new McpToolParameter { Type = "number", Description = "返回上限或限制值，按子动作语义使用", Required = false },
                    ["itemId"] = new McpToolParameter { Type = "string", Description = "side_surface/facility/printing_pod claim 或物品选择动作的目标 prefab/tag/id", Required = false },
                    ["rewardIndex"] = new McpToolParameter { Type = "integer", Description = "side_surface surface=facility kind=printing_pod action=claim 的奖励序号", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险或批量写入确认，按子工具规则使用", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "支持预检的动作可传 true 只返回计划", Required = false }
                },
                Handler = args =>
                {
                    string domain = NormalizeDomain(args);
                    switch (domain)
                    {
                        case "planning":
                        case "plan":
                        case "build":
                        case "placement":
                            return Forward(args, BuildPlanningTools.ControlBuildPlanning());
                        case "config":
                        case "configuration":
                        case "side_screen":
                            return Forward(args, BuildingConfigTools.ControlBuildingConfig());
                        case "production":
                        case "queue":
                        case "fabricator":
                        case "crafting":
                            return Forward(args, ProductionTools.ControlQueue());
                        case "storage":
                        case "stores":
                        case "filter":
                        case "filters":
                        case "tile_selection":
                        case "receptacle":
                            return ForwardStorage(args);
                        case "side_surface":
                        case "surface":
                        case "generic_surface":
                            return Forward(args, GenericSideSurfaceTools.ControlSideSurface());
                        case "facility":
                            return Forward(args, FacilityTools.ControlFacility());
                        case "space_building":
                        case "space_story":
                        case "special":
                        case "story":
                        case "space":
                        case "story_facility":
                            return FacilityTools.ControlFacility().Handler(args);
                        case "rocket":
                        case "rockets":
                        case "rocket_system":
                            return ForwardRocket(args);
                        default:
                    return CallToolResult.Error("domain must be planning, config, production, storage, filter, tile_selection, receptacle, side_surface, space_building, space_story, special, story_facility, or rocket");
                    }
                }
            };
        }

        private static string NormalizeDomain(JObject args)
        {
            string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(domain))
                return domain;

            string action = (args["action"]?.ToString() ?? args["operation"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            switch (action)
            {
                case "search_defs":
                case "search":
                case "defs":
                case "materials":
                case "list_materials":
                case "preview":
                case "validate":
                case "placement_candidates":
                case "candidates":
                case "anchors":
                case "auto_connect":
                case "utility_auto_connect":
                case "connect":
                case "build_area":
                case "batch_build":
                    return "planning";
                case "list_fabricators":
                case "fabricators":
                case "list_recipes":
                case "recipes":
                case "batch":
                case "set_mutant_seeds":
                case "mutant_seeds":
                case "mutant_seed_list":
                case "list_mutant_seeds":
                case "mutant_seed_set":
                case "set_mutant_seed_control":
                    return "production";
                case "set_filter":
                    return "storage";
            }

            return "config";
        }

        private static CallToolResult ForwardStorage(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            string originalDomain = (forwarded["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string kind = (forwarded["kind"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(forwarded["storageDomain"]?.ToString()))
                forwarded["domain"] = forwarded["storageDomain"]?.ToString();
            else if ((originalDomain == "storage" || originalDomain == "stores") && IsLegacyStorageDomain(kind))
            {
                forwarded["domain"] = kind;
                forwarded.Remove("kind");
            }
            else if (originalDomain == "filter" || originalDomain == "filters" ||
                     originalDomain == "tile_selection" || originalDomain == "receptacle")
                forwarded["domain"] = originalDomain;
            else
                forwarded["domain"] = "storage";
            forwarded.Remove("storageDomain");
            return StorageTools.ControlStorageSystem().Handler(forwarded);
        }

        private static bool IsLegacyStorageDomain(string kind)
        {
            switch (kind)
            {
                case "storage":
                case "stores":
                case "building":
                case "buildings":
                case "filter":
                case "filters":
                case "tile_selection":
                case "tile":
                case "storage_tile":
                case "single_item":
                case "receptacle":
                case "receptacles":
                case "entity_slot":
                case "entity_slots":
                    return true;
                default:
                    return false;
            }
        }

        private static CallToolResult Forward(JObject args, McpTool tool)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            forwarded.Remove("domain");
            return tool.Handler(forwarded);
        }

        private static CallToolResult ForwardRocket(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            string rocketDomain = (forwarded["rocketDomain"]?.ToString() ?? forwarded["kind"]?.ToString() ?? string.Empty).Trim();
            forwarded["domain"] = rocketDomain;
            forwarded.Remove("rocketDomain");
            if (!string.IsNullOrEmpty(rocketDomain))
                forwarded.Remove("kind");
            return RocketSystemControlTools.ControlRocketSystem().Handler(forwarded);
        }
    }
}
