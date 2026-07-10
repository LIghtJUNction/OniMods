using System;
using System.Collections.Generic;
using System.Linq;
using Database;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using TemplateClasses;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SandboxTools
    {
        public static McpTool StampStoryTrait()
        {
            return new McpTool
            {
                Name = "sandbox_story_trait_stamp",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_spawn_story_trait", "story_trait_stamp" },
                Tags = new List<string> { "sandbox", "story", "trait", "stamp", "template" },
                Description = "兼容入口：沙盒故事特质盖章，放置指定 storyId 的 retrofit 模板。新调用请使用 game_control domain=sandbox kind=entity action=story_trait_stamp；默认要求沙盒模式开启。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["storyId"] = new McpToolParameter { Type = "string", Description = "故事 ID，例如 MegaBrainTank、CreatureManipulator、LonelyMinion、FossilHunt、MorbRoverMaker、HijackHeadquarters", Required = true },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "盖章原点格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "盖章原点格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["allowExisting"] = new McpToolParameter { Type = "boolean", Description = "允许同一故事实例已存在时仍盖章，默认 false", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    string storyId = args["storyId"]?.ToString();
                    var story = string.IsNullOrWhiteSpace(storyId) ? null : Db.Get().Stories.TryGet(storyId.Trim());
                    if (story == null)
                        return CallToolResult.Error("storyId not found");
                    if (!ToolUtil.GetBool(args, "allowExisting", false) && StoryManager.Instance.GetStoryInstance(story) != null)
                        return CallToolResult.Error("Story instance already exists; set allowExisting=true to stamp anyway");

                    TemplateContainer template = string.IsNullOrWhiteSpace(story.sandboxStampTemplateId)
                        ? null
                        : TemplateCache.GetTemplate(story.sandboxStampTemplateId);
                    if (template == null)
                        return CallToolResult.Error("Story has no sandbox stamp template");

                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");
                    int originCell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(originCell))
                        return CallToolResult.Error("Invalid origin cell");
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!ToolUtil.CellMatchesWorld(originCell, worldId))
                        return CallToolResult.Error($"Origin cell is not in worldId={worldId}");
                    var world = ClusterManager.Instance.GetWorld(worldId);
                    if (world == null || world.IsModuleInterior)
                        return CallToolResult.Error("Story traits can only be stamped in an asteroid world");

                    string validationError = ValidateStoryTemplateCells(template, x.Value, y.Value, worldId);
                    if (validationError != null)
                        return CallToolResult.Error(validationError);

                    SandboxStoryTraitTool.Stamp(new Vector2(x.Value, y.Value), template, delegate
                    {
                        var instance = StoryManager.Instance.GetStoryInstance(story);
                        if (instance != null)
                        {
                            var current = instance.CurrentState;
                            instance.CurrentState = StoryInstance.State.RETROFITTED;
                            instance.CurrentState = current;
                        }
                    });

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["queued"] = true,
                        ["story"] = StoryInfo(story),
                        ["origin"] = new { x = x.Value, y = y.Value, cell = originCell, worldId },
                        ["templateId"] = story.sandboxStampTemplateId
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool AutoPlumbBuilding()
        {
            return new McpTool
            {
                Name = "debug_auto_plumb_building",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "auto_plumber_control", "debug_auto_plumber", "instant_build_auto_plumb" },
                Tags = new List<string> { "sandbox", "debug", "auto-plumber", "instant-build", "building" },
                Description = "兼容入口：执行 AutoPlumberSideScreen 的 Debug 操作。新调用请使用 game_control domain=sandbox kind=entity action=auto_plumb_building，并用 plumbAction=auto_plumb/power/pipes/solids/spawn_minion；要求 InstantBuild 或 force=true，且 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑 InstanceID；spawn_minion 也用该建筑定位", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "auto_plumb、power、pipes、solids 或 spawn_minion", Required = true, EnumValues = new List<string> { "auto_plumb", "power", "pipes", "solids", "spawn_minion" } },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许 InstantBuild 关闭时执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险 Debug 操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (!DebugHandler.InstantBuildMode && !ToolUtil.GetBool(args, "force", false))
                        return CallToolResult.Error("DebugHandler.InstantBuildMode is not active; set force=true to override");

                    var building = FindBuilding(args);
                    if (building == null)
                        return CallToolResult.Error("Target building not found");
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = BuildingInfo(building);

                    if (action == "auto_plumb")
                    {
                        DevAutoPlumber.AutoPlumbBuilding(building);
                    }
                    else if (action == "power")
                    {
                        DevAutoPlumber.DoElectricalPlumbing(building);
                    }
                    else if (action == "pipes")
                    {
                        DevAutoPlumber.DoLiquidAndGasPlumbing(building);
                    }
                    else if (action == "solids")
                    {
                        DevAutoPlumber.SetupSolidOreDelivery(building);
                    }
                    else if (action == "spawn_minion")
                    {
                        SpawnDebugMinion(building);
                    }
                    else
                    {
                        return CallToolResult.Error("action must be auto_plumb, power, pipes, solids, or spawn_minion");
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = before,
                        ["action"] = action,
                        ["instantBuildMode"] = DebugHandler.InstantBuildMode,
                        ["forced"] = ToolUtil.GetBool(args, "force", false)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool FloodFillElement()
        {
            return new McpTool
            {
                Name = "sandbox_flood_fill_element",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_bucket_fill", "debug_flood_fill_element" },
                Tags = new List<string> { "sandbox", "flood", "bucket", "element" },
                Description = "兼容入口：沙盒桶填充。新调用请使用 game_control domain=sandbox kind=area action=flood_fill。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "起点格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "起点格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["element"] = new McpToolParameter { Type = "string", Description = "目标元素 ID，例如 Oxygen、Water、IgneousRock、Vacuum", Required = true },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "每格质量 kg，Vacuum 自动为 0；默认 1", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "温度 K，默认元素默认温度", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "病菌 ID，默认无", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "每格病菌数量，默认 0", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "安全上限，默认/最大 1000；超过则拒绝不修改", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);

                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");

                    int startCell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(startCell))
                        return CallToolResult.Error("Invalid start cell");
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!ToolUtil.CellMatchesWorld(startCell, worldId))
                        return CallToolResult.Error($"Start cell is not in worldId={worldId}");

                    var targetElement = ResolveElement(args["element"]?.ToString());
                    if (targetElement == null)
                        return CallToolResult.Error("Unknown element");

                    SimHashes sourceElement = Grid.Element[startCell]?.id ?? SimHashes.Vacuum;
                    int maxCells = Math.Min(MaxSandboxCells, Math.Max(1, ToolUtil.GetInt(args, "maxCells") ?? MaxSandboxCells));
                    var cells = CollectFloodCells(startCell, sourceElement, worldId, maxCells);
                    if (cells == null)
                        return CallToolResult.Error($"Flood fill exceeds maxCells={maxCells}; no changes applied");

                    byte diseaseIdx = ResolveDiseaseIndex(args["disease"]?.ToString());
                    int diseaseCount = Math.Max(0, ToolUtil.GetInt(args, "diseaseCount") ?? 0);
                    float mass = targetElement.IsVacuum ? 0f : Math.Max(0f, ToolUtil.GetFloat(args, "massKg") ?? 1f);
                    float temp = ToolUtil.GetFloat(args, "temperatureK") ?? targetElement.defaultValues.temperature;

                    foreach (int cell in cells)
                        SimMessages.ReplaceElement(cell, targetElement.id, CellEventLogger.Instance.SandBoxTool, mass, temp, diseaseIdx, diseaseCount);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = cells.Count,
                        ["fromElement"] = sourceElement.ToString(),
                        ["element"] = targetElement.id.ToString(),
                        ["massKg"] = mass,
                        ["temperatureK"] = temp,
                        ["worldId"] = worldId,
                        ["start"] = new { x = x.Value, y = y.Value, cell = startCell }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetTemperatureArea()
        {
            return new McpTool
            {
                Name = "sandbox_temperature_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_heat_area", "sandbox_cool_area", "debug_temperature_area" },
                Tags = new List<string> { "sandbox", "temperature", "heat", "cool" },
                Description = "兼容入口：沙盒温度枪。新调用请使用 game_control domain=sandbox kind=area action=temperature。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["mode"] = new McpToolParameter { Type = "string", Description = "set 设为 temperatureK；add 增加 deltaK，默认 set", Required = false, EnumValues = new List<string> { "set", "add" } },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "mode=set 时目标温度 K", Required = false },
                    ["deltaK"] = new McpToolParameter { Type = "number", Description = "mode=add 时温度增量 K，可为负", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > MaxSandboxCells)
                        return CallToolResult.Error($"Refusing to modify {cells} cells; max={MaxSandboxCells}");

                    string mode = (args["mode"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                    if (mode != "set" && mode != "add")
                        return CallToolResult.Error("mode must be set or add");
                    float? temperatureK = ToolUtil.GetFloat(args, "temperatureK");
                    float? deltaK = ToolUtil.GetFloat(args, "deltaK");
                    if (mode == "set" && !temperatureK.HasValue)
                        return CallToolResult.Error("temperatureK is required when mode=set");
                    if (mode == "add" && !deltaK.HasValue)
                        return CallToolResult.Error("deltaK is required when mode=add");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int changed = 0;
                    foreach (int cell in RectCells(rect))
                    {
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        var element = Grid.Element[cell];
                        if (element == null)
                            continue;
                        float temp = mode == "set" ? temperatureK.Value : Grid.Temperature[cell] + deltaK.Value;
                        temp = Mathf.Clamp(temp, 1f, 9999f);
                        SimMessages.ReplaceElement(cell, element.id, CellEventLogger.Instance.SandBoxTool, Grid.Mass[cell], temp, Grid.DiseaseIdx[cell], Grid.DiseaseCount[cell]);
                        changed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = changed,
                        ["mode"] = mode,
                        ["temperatureK"] = temperatureK,
                        ["deltaK"] = deltaK,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool RevealArea()
        {
            return new McpTool
            {
                Name = "sandbox_reveal_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_reveal_fog", "debug_reveal_area" },
                Tags = new List<string> { "sandbox", "fog", "reveal", "explore" },
                Description = "兼容入口：沙盒揭示。新调用请使用 game_control domain=sandbox kind=area action=reveal。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > MaxSandboxCells)
                        return CallToolResult.Error($"Refusing to reveal {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int revealed = 0;
                    int newlyVisible = 0;
                    foreach (int cell in RectCells(rect))
                    {
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        bool wasVisible = Grid.IsVisible(cell);
                        Grid.Reveal(cell, byte.MaxValue, forceReveal: true);
                        revealed++;
                        if (!wasVisible && Grid.IsVisible(cell))
                            newlyVisible++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["revealed"] = revealed,
                        ["newlyVisible"] = newlyVisible,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
