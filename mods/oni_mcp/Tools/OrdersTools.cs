using System;
using System.Collections.Generic;
using System.Linq;
using Klei.Input;
using Newtonsoft.Json;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class OrdersTools
    {
        private const int CancelEvent = 2127324410;

        public static McpTool ListPriorities()
        {
            return new McpTool
            {
                Name = "priorities_list",
                Group = "orders",
                Mode = "read",
                Risk = "none",
                Description = "列出可设置优先级的对象，可按区域、世界和名称筛选",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 关键词筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "是否包含当前不可设置优先级的对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = GetWorldFilter(args, hasRect);
                    string query = args["query"]?.ToString();
                    bool includeInactive = ToolUtil.GetBool(args, "includeInactive", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var items = new List<Dictionary<string, object>>();
                    foreach (var prioritizable in Components.Prioritizables.Items)
                    {
                        if (!MatchesPriorityTarget(prioritizable, rect, worldId, query, includeInactive))
                            continue;

                        items.Add(PriorityTargetToDictionary(prioritizable));
                        if (items.Count >= limit)
                            break;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["rect"] = rect,
                        ["priorities"] = items
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetBuildingPriority()
        {
            return new McpTool
            {
                Name = "buildings_set_priority",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Description = "设置建筑或可优先级对象的差事优先级",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9", Required = true },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var prioritizable = go.GetComponent<Prioritizable>();
                    if (prioritizable == null)
                        return CallToolResult.Error("Target is not prioritizable");

                    int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
                    bool top = ToolUtil.GetBool(args, "topPriority", false);
                    var setting = new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
                    prioritizable.SetMasterPriority(setting);
                    return CallToolResult.Text($"Set priority for {go.GetProperName()} to {(top ? "topPriority" : priority.ToString())}");
                }
            };
        }

        public static McpTool SetPriorityArea()
        {
            return new McpTool
            {
                Name = "priorities_set_area",
                Group = "orders",
                Mode = "write",
                Risk = "medium",
                Description = "批量设置矩形区域内可优先级对象的差事优先级",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9", Required = true },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "可选名称或 prefabId 关键词筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "是否包含当前不可设置优先级的对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多修改数量，默认 200，最大 1000", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing priorities in more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    string query = args["query"]?.ToString();
                    bool includeInactive = ToolUtil.GetBool(args, "includeInactive", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 200, 1000));
                    var setting = ParsePrioritySetting(args);
                    var changed = new List<Dictionary<string, object>>();
                    int matched = 0;

                    foreach (var prioritizable in Components.Prioritizables.Items)
                    {
                        if (!MatchesPriorityTarget(prioritizable, rect, worldId, query, includeInactive))
                            continue;

                        matched++;
                        if (changed.Count >= limit)
                            continue;

                        prioritizable.SetMasterPriority(setting);
                        changed.Add(PriorityTargetToDictionary(prioritizable));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["matched"] = matched,
                        ["changed"] = changed.Count,
                        ["skippedByLimit"] = Math.Max(0, matched - changed.Count),
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["priorityClass"] = setting.priority_class.ToString(),
                        ["priority"] = setting.priority_value,
                        ["targets"] = changed
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool DeconstructBuilding()
        {
            return new McpTool
            {
                Name = "buildings_deconstruct",
                Group = "buildings",
                Mode = "execute",
                Risk = "dangerous",
                Description = "将指定建筑标记为拆除，需要 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for deconstruction");
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var deconstructable = go.GetComponent<Deconstructable>();
                    if (deconstructable == null)
                        return CallToolResult.Error("Target is not deconstructable");
                    deconstructable.QueueDeconstruction(userTriggered: true);
                    return CallToolResult.Text($"Queued deconstruction for {go.GetProperName()}");
                }
            };
        }

        public static McpTool SweepArea()
        {
            return new McpTool
            {
                Name = "orders_sweep_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Description = "把矩形区域内散落物标记为清扫",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when sweeping more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int marked = 0;
                    foreach (var pickupable in Components.Pickupables.Items)
                    {
                        if (pickupable == null || pickupable.KPrefabID == null) continue;
                        if (pickupable.KPrefabID.HasTag(GameTags.Stored) || pickupable.KPrefabID.HasTag(GameTags.Equipped)) continue;
                        int cell = pickupable.cachedCell;
                        if (!CellInRect(cell, rect, worldId)) continue;
                        var clearable = pickupable.GetComponent<Clearable>();
                        if (clearable == null) continue;
                        clearable.MarkForClear();
                        marked++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool DigArea()
        {
            return new McpTool
            {
                Name = "orders_dig_area",
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Description = "在矩形区域内对自然实体格子下达挖掘命令，需要 confirm=true",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for dig orders");
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");
                    if (DigTool.Instance == null)
                        return CallToolResult.Error("DigTool is not initialized; open a loaded colony UI before issuing dig orders");

                    var rect = ToolUtil.GetRect(args);
                    int worldId = ToolUtil.ResolveWorldId(args);
                    int marked = 0;
                    int dist = 0;
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell) || !ToolUtil.CellMatchesWorld(cell, worldId)) continue;
                            if (!Grid.Solid[cell] || Grid.Foundation[cell]) continue;
                            if (Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null) continue;
                            if (DigTool.PlaceDig(cell, dist++) != null)
                                marked++;
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool Attack()
        {
            return new McpTool
            {
                Name = "orders_attack",
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Description = "按对象、格子或矩形区域标记/取消攻击目标；标记攻击需要 confirm=true",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID；提供后优先于坐标/区域", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；不提供矩形时按单格查找", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；不提供矩形时按单格查找", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "mark 标记攻击，cancel 取消攻击；默认 mark", Required = false, EnumValues = new List<string> { "mark", "cancel" } },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "攻击差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许标记友方/协助阵营目标，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认；标记攻击时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                    bool mark = action != "cancel";
                    if (mark && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for attack orders");
                    if (FactionManager.Instance == null)
                        return CallToolResult.Error("FactionManager is not initialized");

                    var targets = FindAttackTargets(args);
                    if (targets.Count == 0)
                        return CallToolResult.Error("No attack target found");

                    bool force = ToolUtil.GetBool(args, "force", false);
                    int changed = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();
                    foreach (var target in targets)
                    {
                        var go = target?.gameObject;
                        if (go == null)
                            continue;

                        if (mark && !force && FactionManager.Instance.GetDisposition(FactionManager.FactionID.Duplicant, target.Alignment) == FactionManager.Disposition.Assist)
                        {
                            skipped++;
                            results.Add(TargetResult(go, target, "skipped_assist_faction"));
                            continue;
                        }

                        target.SetPlayerTargeted(mark);
                        if (mark)
                            ApplyPriority(go, args);

                        changed++;
                        results.Add(TargetResult(go, target, mark ? "marked" : "cancelled"));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = mark ? "mark" : "cancel",
                        ["changed"] = changed,
                        ["skipped"] = skipped,
                        ["targets"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool MopArea()
        {
            return new McpTool
            {
                Name = "orders_mop_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "mop_area", "liquids_mop_area" },
                Description = "在矩形区域内对可拖地液体格子下达拖地命令；遵循游戏限制：下方必须有地面且液体质量不能超过可拖地上限",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "拖地差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when mopping more than 100 cells");

                    var prefab = Assets.GetPrefab(new Tag("MopPlacer"));
                    if (prefab == null)
                        return CallToolResult.Error("MopPlacer prefab is not available");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int marked = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;
                            if (Grid.Solid[cell] || !Grid.Element[cell].IsLiquid)
                                continue;
                            if (Grid.Objects[cell, (int)ObjectLayer.MopPlacer] != null)
                            {
                                skipped++;
                                continue;
                            }
                            bool onFloor = Grid.IsValidCell(Grid.CellBelow(cell)) && Grid.Solid[Grid.CellBelow(cell)];
                            bool smallEnough = Grid.Mass[cell] <= MopTool.maxMopAmt;
                            if (!onFloor || !smallEnough)
                            {
                                skipped++;
                                results.Add(CellResult(cell, onFloor ? "skipped_too_much_liquid" : "skipped_no_floor"));
                                continue;
                            }

                            if (DebugHandler.InstantBuildMode)
                            {
                                Moppable.MopCell(cell, 1000000f, null);
                                marked++;
                                results.Add(CellResult(cell, "instant_mopped"));
                                continue;
                            }

                            var placer = Util.KInstantiate(prefab);
                            Grid.Objects[cell, (int)ObjectLayer.MopPlacer] = placer;
                            var position = Grid.CellToPosCBC(cell, Grid.SceneLayer.Move);
                            position.z -= 0.15f;
                            placer.transform.SetPosition(position);
                            placer.SetActive(true);
                            ApplyPriority(placer, args);
                            marked++;
                            results.Add(CellResult(cell, "marked"));
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["skipped"] = skipped,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool DisinfectArea()
        {
            return new McpTool
            {
                Name = "orders_disinfect_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "disinfect_area", "germs_disinfect_area" },
                Description = "标记矩形区域内带病菌且支持消毒的对象",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "消毒差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when disinfecting more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var seen = new HashSet<GameObject>();
                    int marked = 0;
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
                            {
                                var go = Grid.Objects[cell, layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                var disinfectable = go.GetComponent<Disinfectable>();
                                var element = go.GetComponent<PrimaryElement>();
                                if (disinfectable == null || element == null || element.DiseaseCount <= 0)
                                    continue;

                                disinfectable.MarkForDisinfect();
                                ApplyPriority(go, args);
                                marked++;
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool EmptyConduits()
        {
            return new McpTool
            {
                Name = "conduits_empty_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "empty_pipe_area", "orders_empty_pipe" },
                Description = "按区域标记气管、液管或运输轨道清空内容，对应游戏 Empty Pipe 工具",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "清空类型：all、gas、liquid、solid，默认 all",
                        Required = false,
                        EnumValues = new List<string> { "all", "gas", "liquid", "solid" }
                    },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "清空差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when emptying conduits in more than 100 cells");

                    var layers = GetEmptyLayers(args["type"]?.ToString());
                    if (layers.Count == 0)
                        return CallToolResult.Error("type must be all, gas, liquid or solid");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var seen = new HashSet<GameObject>();
                    var results = new List<Dictionary<string, object>>();
                    int marked = 0;
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            foreach (var layer in layers)
                            {
                                var go = Grid.Objects[cell, (int)layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                var workable = go.GetComponent<IEmptyConduitWorkable>();
                                if (workable == null)
                                    continue;

                                if (DebugHandler.InstantBuildMode)
                                    workable.EmptyContents();
                                else
                                    workable.MarkForEmptying();
                                ApplyPriority(go, args);
                                marked++;
                                results.Add(ObjectResult(go, DebugHandler.InstantBuildMode ? "instant_emptied" : "marked"));
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["type"] = string.IsNullOrWhiteSpace(args["type"]?.ToString()) ? "all" : args["type"].ToString(),
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CutConduits()
        {
            return new McpTool
            {
                Name = "conduits_cut",
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Description = "按格子或矩形区域剪断管路/电线/运输轨道，实际下达拆除对应段的命令，需要 confirm=true",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；不提供矩形时按单格处理", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；不提供矩形时按单格处理", Required = false },
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "剪断类型：auto=气/液/固体管路，all=管路+电线+自动化线+运输管，或指定 gas/liquid/solid/wire/logic/travel_tube；默认 auto",
                        Required = false,
                        EnumValues = new List<string> { "auto", "all", "gas", "liquid", "solid", "wire", "logic", "travel_tube" }
                    },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "拆除差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for cutting conduits");
                    if (!HasRectInput(args) && (args["x"] == null || args["y"] == null))
                        return CallToolResult.Error("areaId, x/y or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 200)
                        return CallToolResult.Error("Refusing to cut more than 200 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var layers = GetCutLayers(args["type"]?.ToString());
                    if (layers.Count == 0)
                        return CallToolResult.Error("Invalid type; expected auto, all, gas, liquid, solid, wire, logic or travel_tube");
                    var seen = new HashSet<GameObject>();
                    var results = new List<Dictionary<string, object>>();
                    int queued = 0;
                    int skipped = 0;

                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            foreach (var go in FindCuttableObjects(cell, layers))
                            {
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                string error;
                                if (QueueDeconstruct(go, args, out error))
                                {
                                    queued++;
                                    results.Add(CutResult(go, cell, "queued", null));
                                }
                                else
                                {
                                    skipped++;
                                    results.Add(CutResult(go, cell, "skipped", error));
                                }
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["queued"] = queued,
                        ["skipped"] = skipped,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["type"] = string.IsNullOrWhiteSpace(args["type"]?.ToString()) ? "auto" : args["type"].ToString(),
                        ["targets"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CancelArea()
        {
            return new McpTool
            {
                Name = "orders_cancel_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "cancel_area", "orders_cancel" },
                Description = "取消矩形区域内玩家已下达的订单：挖掘、建造、拆除、清扫、收获、攻击、抓捕等可取消对象",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["includeAttack"] = new McpToolParameter { Type = "boolean", Description = "是否取消区域内攻击标记，默认 true", Required = false },
                    ["includeCapture"] = new McpToolParameter { Type = "boolean", Description = "是否取消区域内抓捕标记，默认 true", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when cancelling more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool includeAttack = ToolUtil.GetBool(args, "includeAttack", true);
                    bool includeCapture = ToolUtil.GetBool(args, "includeCapture", true);
                    var seen = new HashSet<GameObject>();
                    int triggered = 0;

                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            for (int layer = 0; layer < 45; layer++)
                            {
                                var go = Grid.Objects[cell, layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);
                                go.Trigger(CancelEvent);
                                triggered++;
                            }
                        }
                    }

                    int attacksCancelled = includeAttack ? SetAttackMarks(rect, worldId, false, null, false) : 0;
                    int capturesCancelled = includeCapture ? SetCaptureMarks(rect, worldId, false, new PrioritySetting(PriorityScreen.PriorityClass.basic, 5), false, false) : 0;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["triggeredObjects"] = triggered,
                        ["attacksCancelled"] = attacksCancelled,
                        ["capturesCancelled"] = capturesCancelled,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool HarvestArea()
        {
            return new McpTool
            {
                Name = "orders_harvest_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "harvest_area", "plants_harvest_area" },
                Description = "标记、取消或设置区域内植物/可收获对象的收获命令；mode=mark 仅标记当前可收获，when_ready 设置成熟即收获",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["mode"] = new McpToolParameter { Type = "string", Description = "mark、when_ready、cancel；默认 mark", Required = false, EnumValues = new List<string> { "mark", "when_ready", "cancel" } },
                    ["readyOnly"] = new McpToolParameter { Type = "boolean", Description = "mark 模式是否只处理当前可收获对象，默认 true", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing harvest orders in more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    string mode = (args["mode"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                    bool readyOnly = ToolUtil.GetBool(args, "readyOnly", true);
                    int changed = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();

                    foreach (var harvestable in Components.HarvestDesignatables.Items)
                    {
                        var go = harvestable?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (!CellInRect(cell, rect, worldId))
                            continue;

                        if (mode == "cancel")
                        {
                            go.Trigger(CancelEvent);
                            harvestable.SetHarvestWhenReady(false);
                            changed++;
                            results.Add(ObjectResult(go, "cancelled"));
                        }
                        else if (mode == "when_ready")
                        {
                            harvestable.SetHarvestWhenReady(true);
                            changed++;
                            results.Add(ObjectResult(go, "when_ready"));
                        }
                        else
                        {
                            bool canHarvest = harvestable.CanBeHarvested();
                            if (readyOnly && !canHarvest)
                            {
                                skipped++;
                                results.Add(ObjectResult(go, "skipped_not_ready"));
                                continue;
                            }
                            harvestable.MarkForHarvest();
                            changed++;
                            results.Add(ObjectResult(go, "marked"));
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["mode"] = mode == "when_ready" || mode == "cancel" ? mode : "mark",
                        ["changed"] = changed,
                        ["skipped"] = skipped,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CaptureCritters()
        {
            return new McpTool
            {
                Name = "critters_capture",
                Group = "ranching",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "orders_capture", "critters_wrangle" },
                Description = "按对象或区域标记/取消抓捕小动物；action=release 可释放已捆绑小动物",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID；提供后优先于区域", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "mark、cancel、release；默认 mark", Required = false, EnumValues = new List<string> { "mark", "cancel", "release" } },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "抓捕差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true；release 必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                    if (action == "release" && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for releasing critters");

                    int? id = ToolUtil.GetInt(args, "id");
                    if (!id.HasValue && !HasRectInput(args))
                        return CallToolResult.Error("id, areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (!id.HasValue && cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing capture orders in more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var setting = ParsePrioritySetting(args);
                    int changed = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();

                    foreach (var capturable in Components.Capturables.Items)
                    {
                        var go = capturable?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        var kpid = go.GetComponent<KPrefabID>();
                        if (id.HasValue)
                        {
                            if (kpid == null || kpid.InstanceID != id.Value)
                                continue;
                        }
                        else
                        {
                            int cell = Grid.PosToCell(go);
                            if (!CellInRect(cell, rect, worldId))
                                continue;
                        }

                        if (action == "release")
                        {
                            var baggable = go.GetComponent<Baggable>();
                            if (baggable == null || !baggable.wrangled)
                            {
                                skipped++;
                                results.Add(ObjectResult(go, "skipped_not_wrangled"));
                                continue;
                            }
                            baggable.Free();
                            changed++;
                            results.Add(ObjectResult(go, "released"));
                        }
                        else
                        {
                            bool mark = action != "cancel";
                            if (mark && !capturable.IsCapturable())
                            {
                                skipped++;
                                results.Add(ObjectResult(go, "skipped_not_capturable"));
                                continue;
                            }
                            capturable.MarkForCapture(mark, setting, updateMarkedPriority: true);
                            changed++;
                            results.Add(ObjectResult(go, mark ? "marked" : "cancelled"));
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = action == "cancel" || action == "release" ? action : "mark",
                        ["changed"] = changed,
                        ["skipped"] = skipped,
                        ["worldId"] = worldId,
                        ["rect"] = id.HasValue ? null : rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetBuildingEnabled()
        {
            return new McpTool
            {
                Name = "buildings_set_enabled",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "building_enable", "building_disable" },
                Description = "启用或禁用指定建筑；直接设置建筑状态，不排队复制人开关差事",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "true 启用，false 禁用", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var button = go.GetComponent<BuildingEnabledButton>();
                    if (button == null)
                        return CallToolResult.Error("Target does not support enabled/disabled state");

                    bool enabled = ToolUtil.GetBool(args, "enabled", true);
                    button.IsEnabled = enabled;
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["enabled"] = button.IsEnabled
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetBuildingToggle()
        {
            return new McpTool
            {
                Name = "buildings_set_toggle",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "logic_switch_set", "building_toggle_set" },
                Description = "设置支持玩家手动开关的建筑/自动化开关状态，例如逻辑开关；仅当目标实现 IPlayerControlledToggle 时可用",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["on"] = new McpToolParameter { Type = "boolean", Description = "true 打开，false 关闭", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var toggle = go.GetComponents<Component>().OfType<IPlayerControlledToggle>().FirstOrDefault();
                    if (toggle == null)
                        return CallToolResult.Error("Target does not expose a player-controlled toggle");

                    bool desired = ToolUtil.GetBool(args, "on", true);
                    bool before = toggle.ToggledOn();
                    if (before != desired)
                        toggle.ToggledByPlayer();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["before"] = before,
                        ["on"] = toggle.ToggledOn(),
                        ["sideScreenTitleKey"] = toggle.SideScreenTitleKey
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ConfigureManualDelivery()
        {
            return new McpTool
            {
                Name = "buildings_manual_delivery",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "manual_delivery_set", "building_refill_set" },
                Description = "配置建筑手动补料/搬运：暂停或恢复补料、设置容量/补料阈值、立即请求搬运",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["paused"] = new McpToolParameter { Type = "boolean", Description = "可选：true 暂停手动补料，false 恢复", Required = false },
                    ["capacityKg"] = new McpToolParameter { Type = "number", Description = "可选：目标储量上限 kg", Required = false },
                    ["refillMassKg"] = new McpToolParameter { Type = "number", Description = "可选：低于该质量时请求补料 kg", Required = false },
                    ["minimumMassKg"] = new McpToolParameter { Type = "number", Description = "可选：单次搬运最小质量 kg", Required = false },
                    ["requestNow"] = new McpToolParameter { Type = "boolean", Description = "是否立即请求一次搬运，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var delivery = go.GetComponent<ManualDeliveryKG>();
                    if (delivery == null)
                        return CallToolResult.Error("Target does not support manual delivery");

                    if (args["paused"] != null)
                        delivery.Pause(ToolUtil.GetBool(args, "paused", false), "Oni MCP manual delivery setting");
                    float? capacity = ToolUtil.GetFloat(args, "capacityKg");
                    if (capacity.HasValue)
                        delivery.capacity = Math.Max(0f, capacity.Value);
                    float? refill = ToolUtil.GetFloat(args, "refillMassKg");
                    if (refill.HasValue)
                        delivery.refillMass = Math.Max(0f, refill.Value);
                    float? minimum = ToolUtil.GetFloat(args, "minimumMassKg");
                    if (minimum.HasValue)
                        delivery.MinimumMass = Math.Max(0f, minimum.Value);
                    if (ToolUtil.GetBool(args, "requestNow", false))
                        delivery.RequestDelivery();

                    delivery.UpdateDeliveryState();
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["paused"] = delivery.IsPaused,
                        ["capacityKg"] = Math.Round(delivery.capacity, 3),
                        ["refillMassKg"] = Math.Round(delivery.refillMass, 3),
                        ["minimumMassKg"] = Math.Round(delivery.MinimumMass, 3),
                        ["requestedItemTag"] = delivery.RequestedItemTag.Name
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X；使用 areaId 时可省略", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y；使用 areaId 时可省略", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X；使用 areaId 时可省略", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y；使用 areaId 时可省略", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认 areaId 绑定世界或当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static GameObject FindTarget(Newtonsoft.Json.Linq.JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var prioritizable in Components.Prioritizables.Items)
            {
                var go = prioritizable?.gameObject;
                if (go == null) continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }

            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null) continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }

            return null;
        }

        private static PrioritySetting ParsePrioritySetting(Newtonsoft.Json.Linq.JObject args)
        {
            bool top = ToolUtil.GetBool(args, "topPriority", false);
            int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
            return new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
        }

        private static int GetWorldFilter(Newtonsoft.Json.Linq.JObject args, bool hasRect)
        {
            if (ToolUtil.GetInt(args, "worldId").HasValue)
                return ToolUtil.GetInt(args, "worldId").Value;
            if (hasRect)
                return ToolUtil.ResolveWorldId(args);
            return -1;
        }

        private static bool MatchesPriorityTarget(Prioritizable prioritizable, Dictionary<string, int> rect, int worldId, string query, bool includeInactive)
        {
            var go = prioritizable?.gameObject;
            if (go == null) return false;
            if (!includeInactive && !prioritizable.IsPrioritizable()) return false;
            if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) return false;

            int cell = Grid.PosToCell(go);
            if (rect != null && !CellInRect(cell, rect, worldId)) return false;
            if (!MatchesQuery(go, query)) return false;
            return true;
        }

        private static bool MatchesQuery(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            var kpid = go.GetComponent<KPrefabID>();
            string prefabId = kpid?.PrefabTag.Name ?? go.name;
            string name = ToolUtil.CleanName(go.GetProperName());
            return Contains(name, q) || Contains(prefabId, q) || Contains(go.name, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> PriorityTargetToDictionary(Prioritizable prioritizable)
        {
            var go = prioritizable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            var setting = prioritizable.GetMasterPriority();
            int cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : Mathf.RoundToInt(go.transform.GetPosition().x);
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : Mathf.RoundToInt(go.transform.GetPosition().y);

            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["position"] = new { x, y },
                ["worldId"] = GetTargetWorldId(go, cell),
                ["isPrioritizable"] = prioritizable.IsPrioritizable(),
                ["priorityClass"] = setting.priority_class.ToString(),
                ["priority"] = setting.priority_value,
                ["topPriority"] = setting.priority_class == PriorityScreen.PriorityClass.topPriority
            };
        }

        private static int GetTargetWorldId(GameObject go, int cell)
        {
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell];

            var component = go.GetComponent<KMonoBehaviour>();
            return component != null ? component.GetMyWorldId() : -1;
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell)) return false;
            if (!ToolUtil.CellMatchesWorld(cell, worldId)) return false;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static int RectCellCount(Dictionary<string, int> rect)
        {
            return (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
        }

        private static bool HasRectInput(Newtonsoft.Json.Linq.JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static int SetAttackMarks(Dictionary<string, int> rect, int worldId, bool mark, Newtonsoft.Json.Linq.JObject args, bool applyPriority)
        {
            int changed = 0;
            foreach (var target in Components.FactionAlignments.Items)
            {
                var go = target?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                int cell = Grid.PosToCell(go);
                if (!CellInRect(cell, rect, worldId))
                    continue;
                if (mark && (!target.canBePlayerTargeted || !target.IsAlignmentActive()))
                    continue;

                target.SetPlayerTargeted(mark);
                if (mark && applyPriority && args != null)
                    ApplyPriority(go, args);
                changed++;
            }
            return changed;
        }

        private static int SetCaptureMarks(Dictionary<string, int> rect, int worldId, bool mark, PrioritySetting setting, bool updatePriority, bool requireCapturable)
        {
            int changed = 0;
            foreach (var capturable in Components.Capturables.Items)
            {
                var go = capturable?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                int cell = Grid.PosToCell(go);
                if (!CellInRect(cell, rect, worldId))
                    continue;
                if (mark && requireCapturable && !capturable.IsCapturable())
                    continue;

                capturable.MarkForCapture(mark, setting, updatePriority);
                changed++;
            }
            return changed;
        }

        private static void ApplyPriority(GameObject go, Newtonsoft.Json.Linq.JObject args)
        {
            var prioritizable = go.GetComponent<Prioritizable>();
            if (prioritizable == null)
                return;

            bool top = ToolUtil.GetBool(args, "topPriority", false);
            int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
            var setting = new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
            prioritizable.SetMasterPriority(setting);
        }

        private static List<FactionAlignment> FindAttackTargets(Newtonsoft.Json.Linq.JObject args)
        {
            var results = new List<FactionAlignment>();
            int? id = ToolUtil.GetInt(args, "id");
            int worldId = ToolUtil.ResolveWorldId(args);

            if (id.HasValue)
            {
                foreach (var target in Components.FactionAlignments.Items)
                {
                    var go = target?.gameObject;
                    if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                    var kpid = go.GetComponent<KPrefabID>();
                    if (kpid != null && kpid.InstanceID == id.Value)
                    {
                        results.Add(target);
                        break;
                    }
                }
                return results;
            }

            bool hasArea = HasRectInput(args);
            if (!hasArea && (args["x"] == null || args["y"] == null))
                return results;

            var rect = ToolUtil.GetRect(args);
            foreach (var target in Components.FactionAlignments.Items)
            {
                var go = target?.gameObject;
                if (go == null || !target.canBePlayerTargeted || !target.IsAlignmentActive()) continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;

                int cell = Grid.PosToCell(go);
                if (!Grid.IsValidCell(cell)) continue;
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                if (x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"])
                    results.Add(target);
            }

            return results;
        }

        private static Dictionary<string, object> TargetResult(GameObject go, FactionAlignment target, string status)
        {
            int cell = Grid.PosToCell(go);
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["faction"] = target.Alignment.ToString(),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static List<ObjectLayer> GetCutLayers(string value)
        {
            string type = string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim().ToLowerInvariant();
            var layers = new List<ObjectLayer>();
            if (type == "auto" || type == "all" || type == "gas")
                layers.AddRange(new[] { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit });
            if (type == "auto" || type == "all" || type == "liquid")
                layers.AddRange(new[] { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit });
            if (type == "auto" || type == "all" || type == "solid")
                layers.AddRange(new[] { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit });
            if (type == "all" || type == "wire")
                layers.AddRange(new[] { ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire });
            if (type == "all" || type == "logic")
                layers.AddRange(new[] { ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire });
            if (type == "all" || type == "travel_tube")
                layers.AddRange(new[] { ObjectLayer.TravelTubeTile, ObjectLayer.ReplacementTravelTube, ObjectLayer.Building });
            return layers;
        }

        private static List<ObjectLayer> GetEmptyLayers(string value)
        {
            string type = string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();
            var layers = new List<ObjectLayer>();
            if (type == "all" || type == "gas")
                layers.AddRange(new[] { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile });
            if (type == "all" || type == "liquid")
                layers.AddRange(new[] { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile });
            if (type == "all" || type == "solid")
                layers.AddRange(new[] { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile });
            return layers;
        }

        private static IEnumerable<GameObject> FindCuttableObjects(int cell, List<ObjectLayer> layers)
        {
            foreach (var layer in layers)
            {
                var go = Grid.Objects[cell, (int)layer];
                if (go == null)
                    continue;
                if (layer == ObjectLayer.Building && go.GetComponent<TravelTube>() == null)
                    continue;
                yield return go;
            }
        }

        private static bool QueueDeconstruct(GameObject go, Newtonsoft.Json.Linq.JObject args, out string error)
        {
            var deconstructable = go.GetComponent<Deconstructable>();
            if (deconstructable == null)
            {
                error = "Target is not deconstructable";
                return false;
            }
            if (!deconstructable.allowDeconstruction && !DebugHandler.InstantBuildMode)
            {
                error = "Target does not allow deconstruction";
                return false;
            }

            deconstructable.QueueDeconstruction(userTriggered: true);
            ApplyPriority(go, args);
            error = null;
            return true;
        }

        private static Dictionary<string, object> CutResult(GameObject go, int cell, string status, string error)
        {
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            var result = new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = kpid?.InstanceID ?? -1,
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.CellColumn(cell),
                ["y"] = Grid.CellRow(cell)
            };
            if (!string.IsNullOrEmpty(error))
                result["error"] = error;
            return result;
        }

        private static Dictionary<string, object> ObjectResult(GameObject go, string status)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static Dictionary<string, object> CellResult(int cell, string status)
        {
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["element"] = Grid.IsValidCell(cell) ? Grid.Element[cell].id.ToString() : "",
                ["massKg"] = Grid.IsValidCell(cell) ? Math.Round(Grid.Mass[cell], 3) : 0
            };
        }
    }
}
