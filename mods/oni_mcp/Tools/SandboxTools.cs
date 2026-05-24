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
    public static class SandboxTools
    {
        private const int MaxSandboxCells = 1000;

        public static McpTool ListSandboxActions()
        {
            return new McpTool
            {
                Name = "sandbox_actions_list",
                Group = "sandbox",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "sandbox_tools_list", "debug_actions_list" },
                Tags = new List<string> { "sandbox", "debug", "actions", "tools" },
                Description = "列出 MCP 暴露的沙盒/Debug 操作、风险和当前沙盒模式状态",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    var actions = new List<Dictionary<string, object>>
                    {
                        ActionInfo("sandbox_sample_cell", "read", "none", "读取格子元素/质量/温度/病菌，并返回可直接用于 paint/flood 的参数。"),
                        ActionInfo("sandbox_paint_element", "execute", "dangerous", "把矩形区域替换为指定元素、质量、温度和病菌。"),
                        ActionInfo("sandbox_flood_fill_element", "execute", "dangerous", "从起点开始替换同世界、同元素的连通区域。"),
                        ActionInfo("sandbox_temperature_area", "execute", "dangerous", "按 set/add 模式修改区域格子温度，保留元素、质量和病菌。"),
                        ActionInfo("sandbox_reveal_area", "execute", "dangerous", "揭示战争迷雾/未探索格子。"),
                        ActionInfo("sandbox_clear_floor_area", "execute", "dangerous", "删除区域内地面可拾取物，不删除复制人。"),
                        ActionInfo("sandbox_clear_critters_area", "execute", "dangerous", "删除区域内小动物，等价于沙盒 Critter Tool。"),
                        ActionInfo("sandbox_destroy_area", "execute", "dangerous", "销毁区域内格子内容。"),
                        ActionInfo("sandbox_spawn_entity", "execute", "dangerous", "生成实体、物品、动物或已完成建筑。"),
                        ActionInfo("sandbox_story_traits_list", "read", "none", "列出可由沙盒 Story Trait Tool 盖章的故事特质模板。"),
                        ActionInfo("sandbox_story_trait_stamp", "execute", "dangerous", "在指定位置盖章故事特质 retrofit 模板。"),
                        ActionInfo("debug_auto_plumb_building", "execute", "dangerous", "Debug/InstantBuild 专用：对目标建筑执行 AutoPlumber 电力/管道/固体配送/全部自动连接，或生成 debug 复制人。"),
                        ActionInfo("sandbox_stress_area", "execute", "dangerous", "对区域内复制人或指定复制人增减压力。")
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["sandboxModeActive"] = Game.Instance?.SandboxModeActive ?? false,
                        ["maxAreaCells"] = MaxSandboxCells,
                        ["actions"] = actions,
                        ["safety"] = "Dangerous sandbox write operations require confirm=true and sandbox mode unless force=true."
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SampleCell()
        {
            return new McpTool
            {
                Name = "sandbox_sample_cell",
                Group = "sandbox",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "sandbox_copy_element", "debug_sample_cell" },
                Tags = new List<string> { "sandbox", "sample", "cell", "element" },
                Description = "沙盒取样器：读取指定格子的元素、质量、温度、病菌，并返回可直接用于沙盒刷子/填充的参数",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");

                    int cell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(cell))
                        return CallToolResult.Error("Invalid target cell");
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!ToolUtil.CellMatchesWorld(cell, worldId))
                        return CallToolResult.Error($"Target cell is not in worldId={worldId}");

                    return CallToolResult.Text(JsonConvert.SerializeObject(CellSample(cell), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool PaintElement()
        {
            return new McpTool
            {
                Name = "sandbox_paint_element",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_brush", "debug_paint_element" },
                Description = "沙盒刷子：把区域内格子替换为指定元素、质量、温度和病菌；默认要求沙盒模式开启",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["element"] = new McpToolParameter { Type = "string", Description = "元素 ID，例如 Oxygen、Water、IgneousRock、Vacuum", Required = true },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "每格质量 kg，Vacuum 自动为 0；默认 1", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "温度 K，默认元素默认温度", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "病菌 ID，默认无", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "每格病菌数量，默认 0", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var element = ResolveElement(args["element"]?.ToString());
                    if (element == null)
                        return CallToolResult.Error("Unknown element");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > MaxSandboxCells)
                        return CallToolResult.Error($"Refusing to paint {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    byte diseaseIdx = byte.MaxValue;
                    var disease = Db.Get().Diseases.TryGet(args["disease"]?.ToString());
                    if (disease != null)
                        diseaseIdx = Db.Get().Diseases.GetIndex(disease.id);
                    int diseaseCount = Math.Max(0, ToolUtil.GetInt(args, "diseaseCount") ?? 0);
                    float mass = element.IsVacuum ? 0f : Math.Max(0f, ToolUtil.GetFloat(args, "massKg") ?? 1f);
                    float temp = ToolUtil.GetFloat(args, "temperatureK") ?? element.defaultValues.temperature;
                    int changed = 0;

                    foreach (int cell in RectCells(rect))
                    {
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        SimMessages.ReplaceElement(cell, element.id, CellEventLogger.Instance.SandBoxTool, mass, temp, diseaseIdx, diseaseCount);
                        changed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = changed,
                        ["element"] = element.id.ToString(),
                        ["massKg"] = mass,
                        ["temperatureK"] = temp,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool DestroyArea()
        {
            return new McpTool
            {
                Name = "sandbox_destroy_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_clear", "debug_destroy_area" },
                Description = "沙盒删除：销毁区域内格子内容，等价于沙盒 Destroy 工具；默认要求沙盒模式开启",
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
                        return CallToolResult.Error($"Refusing to destroy {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int destroyed = 0;
                    foreach (int cell in RectCells(rect))
                    {
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        GameUtil.DestroyCell(cell, CellEventLogger.Instance.SandBoxTool);
                        destroyed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["destroyed"] = destroyed,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SpawnEntity()
        {
            return new McpTool
            {
                Name = "sandbox_spawn_entity",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "debug_spawn_entity", "sandbox_spawn" },
                Description = "沙盒生成实体或已完成建筑；默认要求沙盒模式开启",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "Prefab ID，例如 Hatch、BasicPlantBar、Tile、OxygenDiffuser", Required = true },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "生成 ElementChunk 时的质量 kg，默认 100", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "生成 ElementChunk 时的温度 K，默认元素默认温度", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        return CallToolResult.Error("prefabId is required");
                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");
                    int cell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(cell))
                        return CallToolResult.Error("Invalid target cell");
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!ToolUtil.CellMatchesWorld(cell, worldId))
                        return CallToolResult.Error($"Target cell is not in worldId={worldId}");

                    var prefab = Assets.GetPrefab(prefabId.Trim());
                    if (prefab == null)
                        return CallToolResult.Error("Prefab not found");

                    GameObject spawned;
                    var building = prefab.GetComponent<Building>();
                    if (building != null)
                    {
                        var def = building.Def;
                        spawned = def.Build(cell, Orientation.Neutral, null, def.DefaultElements(), ToolUtil.GetFloat(args, "temperatureK") ?? 298.15f);
                    }
                    else
                    {
                        var controller = prefab.GetComponent<KBatchedAnimController>();
                        Grid.SceneLayer layer = controller == null ? Grid.SceneLayer.Creatures : controller.sceneLayer;
                        spawned = GameUtil.KInstantiate(prefab, Grid.CellToPosCBC(cell, layer), layer);
                        var chunk = spawned.GetComponent<ElementChunk>();
                        if (chunk != null)
                        {
                            var primary = spawned.GetComponent<PrimaryElement>();
                            primary.Mass = Math.Max(0f, ToolUtil.GetFloat(args, "massKg") ?? 100f);
                            primary.Temperature = ToolUtil.GetFloat(args, "temperatureK") ?? primary.Element.defaultValues.temperature;
                        }
                        spawned.SetActive(true);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["spawned"] = spawned != null,
                        ["prefabId"] = prefabId,
                        ["id"] = spawned?.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                        ["x"] = x.Value,
                        ["y"] = y.Value,
                        ["worldId"] = worldId
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListStoryTraits()
        {
            return new McpTool
            {
                Name = "sandbox_story_traits_list",
                Group = "sandbox",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "sandbox_story_trait_templates_list", "story_trait_stamp_templates_list" },
                Tags = new List<string> { "sandbox", "story", "trait", "stamp", "template" },
                Description = "列出可由沙盒 Story Trait Tool 放置的故事特质模板及当前是否已存在",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 storyId、trait 名称或模板 ID 搜索", Required = false }
                },
                Handler = args =>
                {
                    string query = args["query"]?.ToString();
                    var stories = Db.Get().Stories.resources
                        .Where(story => story != null && !string.IsNullOrWhiteSpace(story.sandboxStampTemplateId))
                        .Where(story => StoryMatches(story, query))
                        .OrderBy(story => story.Id)
                        .Select(StoryInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = stories.Count,
                        ["stories"] = stories
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool StampStoryTrait()
        {
            return new McpTool
            {
                Name = "sandbox_story_trait_stamp",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_spawn_story_trait", "story_trait_stamp" },
                Tags = new List<string> { "sandbox", "story", "trait", "stamp", "template" },
                Description = "沙盒故事特质盖章：放置指定 storyId 的 retrofit 模板；默认要求沙盒模式开启",
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
                Aliases = new List<string> { "auto_plumber_control", "debug_auto_plumber", "instant_build_auto_plumb" },
                Tags = new List<string> { "sandbox", "debug", "auto-plumber", "instant-build", "building" },
                Description = "执行 AutoPlumberSideScreen 的 Debug 操作：auto_plumb、power、pipes、solids 或 spawn_minion；要求 InstantBuild 或 force=true，且 confirm=true",
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
                Aliases = new List<string> { "sandbox_bucket_fill", "debug_flood_fill_element" },
                Tags = new List<string> { "sandbox", "flood", "bucket", "element" },
                Description = "沙盒桶填充：从起点开始替换同世界、同元素连通区域；默认要求沙盒模式开启",
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
                Aliases = new List<string> { "sandbox_heat_area", "sandbox_cool_area", "debug_temperature_area" },
                Tags = new List<string> { "sandbox", "temperature", "heat", "cool" },
                Description = "沙盒温度枪：按 set/add 模式修改区域温度，保留当前元素、质量和病菌；默认要求沙盒模式开启",
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
                Aliases = new List<string> { "sandbox_reveal_fog", "debug_reveal_area" },
                Tags = new List<string> { "sandbox", "fog", "reveal", "explore" },
                Description = "沙盒揭示：清除区域战争迷雾/未探索状态；默认要求沙盒模式开启",
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

        public static McpTool ClearFloorArea()
        {
            return new McpTool
            {
                Name = "sandbox_clear_floor_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_delete_items_area", "debug_clear_floor_area" },
                Tags = new List<string> { "sandbox", "items", "pickupables", "clear" },
                Description = "沙盒清地面：删除区域内未存储的可拾取物，不删除复制人；默认要求沙盒模式开启",
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
                        return CallToolResult.Error($"Refusing to clear {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var toDestroy = new List<Pickupable>();
                    foreach (var pickupable in Components.Pickupables.Items)
                    {
                        if (pickupable == null || pickupable.storage != null || IsLiveMinion(pickupable.gameObject))
                            continue;
                        int cell = Grid.PosToCell(pickupable);
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || !CellInRect(cell, rect))
                            continue;
                        toDestroy.Add(pickupable);
                    }

                    var removed = new List<Dictionary<string, object>>();
                    foreach (var pickupable in toDestroy)
                    {
                        var kpid = pickupable.GetComponent<KPrefabID>();
                        removed.Add(new Dictionary<string, object>
                        {
                            ["prefabId"] = kpid?.PrefabTag.ToString() ?? pickupable.name,
                            ["id"] = kpid?.InstanceID ?? -1,
                            ["cell"] = Grid.PosToCell(pickupable)
                        });
                        Util.KDestroyGameObject(pickupable.gameObject);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed.Count,
                        ["items"] = removed,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearCrittersArea()
        {
            return new McpTool
            {
                Name = "sandbox_clear_critters_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_critter_tool", "debug_clear_critters_area" },
                Tags = new List<string> { "sandbox", "critters", "creatures", "clear" },
                Description = "沙盒小动物工具：删除区域内小动物，不删除复制人或机器人；默认要求沙盒模式开启",
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
                        return CallToolResult.Error($"Refusing to scan {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var toDestroy = new List<GameObject>();
                    foreach (var health in Components.Health.Items)
                    {
                        var go = health?.gameObject;
                        var kpid = go?.GetComponent<KPrefabID>();
                        if (go == null || kpid == null || !kpid.HasTag(GameTags.Creature))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId) && CellInRect(cell, rect))
                            toDestroy.Add(go);
                    }

                    var removed = new List<Dictionary<string, object>>();
                    foreach (var go in toDestroy)
                    {
                        var kpid = go.GetComponent<KPrefabID>();
                        int cell = Grid.PosToCell(go);
                        removed.Add(new Dictionary<string, object>
                        {
                            ["id"] = kpid?.InstanceID ?? -1,
                            ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                            ["name"] = ToolUtil.CleanName(go.GetProperName()),
                            ["cell"] = cell
                        });
                        Util.KDestroyGameObject(go);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed.Count,
                        ["critters"] = removed,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }


        public static McpTool StressArea()
        {
            return new McpTool
            {
                Name = "sandbox_stress_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_dupe_stress", "debug_stress_area" },
                Tags = new List<string> { "sandbox", "duplicant", "stress" },
                Description = "沙盒压力工具：对区域内复制人或指定复制人增减压力；默认要求沙盒模式开启",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；提供后可省略区域", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；提供后可省略区域", Required = false },
                    ["delta"] = new McpToolParameter { Type = "number", Description = "压力变化量，正数增加压力、负数降低压力；例如 -20", Required = true },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    float? delta = ToolUtil.GetFloat(args, "delta");
                    if (!delta.HasValue)
                        return CallToolResult.Error("delta is required");

                    var dupes = new List<MinionIdentity>();
                    var selected = ToolUtil.FindDupe(args);
                    if (selected != null)
                    {
                        dupes.Add(selected);
                    }
                    else
                    {
                        if (!HasRectInput(args))
                            return CallToolResult.Error("id/name or areaId/x1/y1/x2/y2 are required");
                        var rect = ToolUtil.GetRect(args);
                        int cells = RectCellCount(rect);
                        if (cells > MaxSandboxCells)
                            return CallToolResult.Error($"Refusing to scan {cells} cells; max={MaxSandboxCells}");

                        int worldId = ToolUtil.ResolveWorldId(args);
                        foreach (var dupe in Components.LiveMinionIdentities.Items)
                        {
                            if (dupe == null)
                                continue;
                            int cell = Grid.PosToCell(dupe.gameObject);
                            if (Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId) && CellInRect(cell, rect))
                                dupes.Add(dupe);
                        }
                    }

                    var affected = new List<Dictionary<string, object>>();
                    foreach (var dupe in dupes)
                    {
                        var amount = Db.Get().Amounts.Stress.Lookup(dupe.gameObject);
                        float before = amount.value;
                        amount.ApplyDelta(delta.Value);
                        affected.Add(new Dictionary<string, object>
                        {
                            ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                            ["name"] = dupe.GetProperName(),
                            ["before"] = before,
                            ["after"] = amount.value,
                            ["delta"] = delta.Value
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["affected"] = affected.Count,
                        ["dupes"] = affected
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static bool ValidateSandbox(Newtonsoft.Json.Linq.JObject args, out string error)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
            {
                error = "confirm=true is required";
                return false;
            }
            if (Game.Instance == null)
            {
                error = "Game not initialized";
                return false;
            }
            if (!Game.Instance.SandboxModeActive && !ToolUtil.GetBool(args, "force", false))
            {
                error = "Sandbox mode is not active; set force=true to override";
                return false;
            }
            error = null;
            return true;
        }

        private static Element ResolveElement(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            SimHashes hash;
            if (!Enum.TryParse(value.Trim(), true, out hash))
                return null;
            return ElementLoader.FindElementByHash(hash);
        }

        private static byte ResolveDiseaseIndex(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return byte.MaxValue;
            var disease = Db.Get().Diseases.TryGet(value.Trim());
            return disease == null ? byte.MaxValue : Db.Get().Diseases.GetIndex(disease.id);
        }

        private static bool HasRectInput(Newtonsoft.Json.Linq.JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static int RectCellCount(Dictionary<string, int> rect)
        {
            return (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
        }

        private static IEnumerable<int> RectCells(Dictionary<string, int> rect)
        {
            for (int y = rect["y1"]; y <= rect["y2"]; y++)
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                    yield return Grid.XYToCell(x, y);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect)
        {
            Grid.CellToXY(cell, out int x, out int y);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static List<int> CollectFloodCells(int startCell, SimHashes sourceElement, int worldId, int maxCells)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            seen.Add(startCell);
            queue.Enqueue(startCell);

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                    continue;
                if ((Grid.Element[cell]?.id ?? SimHashes.Vacuum) != sourceElement)
                    continue;

                result.Add(cell);
                if (result.Count > maxCells)
                    return null;

                Grid.CellToXY(cell, out int x, out int y);
                EnqueueNeighbor(x - 1, y, seen, queue);
                EnqueueNeighbor(x + 1, y, seen, queue);
                EnqueueNeighbor(x, y - 1, seen, queue);
                EnqueueNeighbor(x, y + 1, seen, queue);
            }

            return result;
        }

        private static void EnqueueNeighbor(int x, int y, HashSet<int> seen, Queue<int> queue)
        {
            if (x < 0 || x >= Grid.WidthInCells || y < 0 || y >= Grid.HeightInCells)
                return;
            int cell = Grid.XYToCell(x, y);
            if (seen.Add(cell))
                queue.Enqueue(cell);
        }

        private static bool IsLiveMinion(GameObject gameObject)
        {
            foreach (var minion in Components.LiveMinionIdentities.Items)
            {
                if (minion != null && minion.gameObject == gameObject)
                    return true;
            }
            return false;
        }

        private static Dictionary<string, object> CellSample(int cell)
        {
            var element = Grid.Element[cell];
            string diseaseId = null;
            if (Grid.DiseaseIdx[cell] != byte.MaxValue && Grid.DiseaseIdx[cell] >= 0)
                diseaseId = Db.Get().Diseases[Grid.DiseaseIdx[cell]]?.id.ToString();

            Grid.CellToXY(cell, out int x, out int y);
            return new Dictionary<string, object>
            {
                ["cell"] = cell,
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = Grid.WorldIdx[cell],
                ["isVisible"] = Grid.IsVisible(cell),
                ["element"] = element?.id.ToString() ?? "Unknown",
                ["elementName"] = ToolUtil.CleanName(element?.name ?? "Unknown"),
                ["state"] = ToolUtil.GetElementState(element),
                ["massKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                ["temperatureK"] = Math.Round(ToolUtil.SafeFloat(Grid.Temperature[cell]), 2),
                ["disease"] = diseaseId,
                ["diseaseCount"] = Grid.DiseaseCount[cell],
                ["paintArguments"] = new Dictionary<string, object>
                {
                    ["element"] = element?.id.ToString() ?? "Vacuum",
                    ["massKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                    ["temperatureK"] = Math.Round(ToolUtil.SafeFloat(Grid.Temperature[cell]), 2),
                    ["disease"] = diseaseId,
                    ["diseaseCount"] = Grid.DiseaseCount[cell]
                }
            };
        }

        private static Building FindBuilding(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var buildingComplete in Components.BuildingCompletes.Items)
            {
                var building = buildingComplete?.GetComponent<Building>();
                if (building == null || !ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;
                var kpid = building.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return building;
                if (cell.HasValue && Grid.PosToCell(building) == cell.Value)
                    return building;
            }
            return null;
        }

        private static Dictionary<string, object> BuildingInfo(Building building)
        {
            int cell = Grid.PosToCell(building);
            var kpid = building.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? building.gameObject.GetInstanceID(),
                ["prefabId"] = building.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? building.name,
                ["name"] = ToolUtil.CleanName(building.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static void SpawnDebugMinion(Building building)
        {
            var stats = new MinionStartingStats(is_starter_minion: false, null, null, isDebugMinion: true);
            GameObject prefab = Assets.GetPrefab(BaseMinionConfig.GetMinionIDForModel(stats.personality.model));
            GameObject minion = Util.KInstantiate(prefab);
            minion.name = prefab.name;
            Immigration.Instance.ApplyDefaultPersonalPriorities(minion);
            Vector3 position = Grid.CellToPos(Grid.PosToCell(building), CellAlignment.Bottom, Grid.SceneLayer.Move);
            minion.transform.SetLocalPosition(position);
            minion.SetActive(true);
            stats.Apply(minion);
        }

        private static bool StoryMatches(Story story, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(story.Id, q)
                || Contains(story.worldgenStoryTraitKey, q)
                || Contains(story.sandboxStampTemplateId, q)
                || Contains(story.StoryTrait?.name, q);
        }

        private static Dictionary<string, object> StoryInfo(Story story)
        {
            TemplateContainer template = string.IsNullOrWhiteSpace(story.sandboxStampTemplateId)
                ? null
                : TemplateCache.GetTemplate(story.sandboxStampTemplateId);
            return new Dictionary<string, object>
            {
                ["storyId"] = story.Id,
                ["traitKey"] = story.worldgenStoryTraitKey,
                ["traitName"] = ToolUtil.CleanName(story.StoryTrait?.name ?? story.Id),
                ["templateId"] = story.sandboxStampTemplateId,
                ["hasTemplate"] = template != null,
                ["size"] = template?.info == null ? null : new { x = template.info.size.X, y = template.info.size.Y },
                ["alreadyExists"] = StoryManager.Instance?.GetStoryInstance(story) != null,
                ["keepsakePrefabId"] = story.keepsakePrefabId
            };
        }

        private static string ValidateStoryTemplateCells(TemplateContainer template, int originX, int originY, int worldId)
        {
            if (template.cells == null)
                return null;
            foreach (var cellInfo in template.cells)
            {
                int cell = Grid.XYToCell(originX + cellInfo.location_x, originY + cellInfo.location_y);
                if (!Grid.IsValidBuildingCell(cell))
                    return "Template would place outside valid building cells";
                if (!ToolUtil.CellMatchesWorld(cell, worldId))
                    return $"Template would cross out of worldId={worldId}";
                if (Grid.Element[cell]?.id == SimHashes.Unobtanium)
                    return "Template would overwrite neutronium/unobtanium";
            }
            return null;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> ActionInfo(string name, string mode, string risk, string description)
        {
            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["mode"] = mode,
                ["risk"] = risk,
                ["description"] = description
            };
        }
    }
}
