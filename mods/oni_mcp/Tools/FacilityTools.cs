using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class FacilityTools
    {
        public static McpTool ControlFacility()
        {
            return new McpTool
            {
                Name = "facility_control",
                Group = "buildings",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "special_facility_control", "story_space_facility_control" },
                Tags = new List<string> { "facility", "special", "story", "space", "side-screen", "building" },
                Description = "特殊/剧情/太空设施聚合入口：domain=space_building/space_story/special/story_facility；保留各子工具 kind/action 与确认规则。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "space_building、space_story、special 或 story_facility", Required = true, EnumValues = new List<string> { "space_building", "space_story", "special", "story_facility" } },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "子设施类型，按 domain 使用原 kind，例如 comet_detector、railgun、warp_portal、artable、printerceptor", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "子动作，按 domain/kind 使用原 action，例如 list、set、set_target、set_stage、consume、assign、cancel", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标设施 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标 Y", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "列表或选择动作的筛选词", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "列表返回上限", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "列表是否返回可选项", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "子动作需要确认时传 true", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "space_building":
                        case "space":
                        case "automation_space":
                            return SpaceBuildingTools.ControlSpaceBuilding().Handler(args);
                        case "space_story":
                        case "story_space":
                        case "starmap":
                            return SpaceStoryTools.ControlSpaceStory().Handler(args);
                        case "special":
                        case "special_building":
                        case "building":
                            return SpecialBuildingTools.ControlSpecialBuilding().Handler(args);
                        case "story_facility":
                        case "story":
                        case "facility":
                            return StoryFacilityTools.ControlStoryFacility().Handler(args);
                        default:
                            return CallToolResult.Error("domain must be space_building, space_story, special, or story_facility");
                    }
                }
            };
        }
    }
}
