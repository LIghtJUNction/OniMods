using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static readonly ObjectLayer[] BlueprintScanLayers = new[]
        {
            ObjectLayer.Building,
            ObjectLayer.FoundationTile,
            ObjectLayer.Wire,
            ObjectLayer.WireTile,
            ObjectLayer.LiquidConduit,
            ObjectLayer.LiquidConduitTile,
            ObjectLayer.GasConduit,
            ObjectLayer.GasConduitTile,
            ObjectLayer.LogicWire,
            ObjectLayer.LogicWireTile,
            ObjectLayer.SolidConduit,
            ObjectLayer.SolidConduitTile
        };

        private static CallToolResult BlueprintCapture(JObject args)
        {
            string name = Text(args, "name", "target");
            string category = Text(args, "category", "folder");
            if (string.IsNullOrWhiteSpace(name))
                return CallToolResult.Error("blueprint capture requires 'name'.");

            int? x1 = ToolUtil.GetInt(args, "x1") ?? ToolUtil.GetInt(args, "minX") ?? ToolUtil.GetInt(args, "x");
            int? y1 = ToolUtil.GetInt(args, "y1") ?? ToolUtil.GetInt(args, "minY") ?? ToolUtil.GetInt(args, "y");
            int? x2 = ToolUtil.GetInt(args, "x2") ?? ToolUtil.GetInt(args, "maxX");
            int? y2 = ToolUtil.GetInt(args, "y2") ?? ToolUtil.GetInt(args, "maxY");

            int? width = ToolUtil.GetInt(args, "width") ?? ToolUtil.GetInt(args, "w");
            int? height = ToolUtil.GetInt(args, "height") ?? ToolUtil.GetInt(args, "h");

            if (width.HasValue && !x2.HasValue && x1.HasValue) x2 = x1.Value + width.Value - 1;
            if (height.HasValue && !y2.HasValue && y1.HasValue) y2 = y1.Value + height.Value - 1;

            if (!x1.HasValue || !y1.HasValue || !x2.HasValue || !y2.HasValue)
                return CallToolResult.Error("blueprint capture requires bounding box (x1, y1, x2, y2) or (x, y, width, height).");

            int minX = Math.Min(x1.Value, x2.Value);
            int maxX = Math.Max(x1.Value, x2.Value);
            int minY = Math.Min(y1.Value, y2.Value);
            int maxY = Math.Max(y1.Value, y2.Value);

            string combined = !string.IsNullOrWhiteSpace(category) && !name.Contains("/")
                ? category.Trim().Trim('/', '\\') + "/" + name.Trim()
                : name.Trim();

            if (!TryGetBlueprintPath(combined, false, out string path, out string pathError))
                return CallToolResult.Error(pathError);

            if (File.Exists(path) && !ToolUtil.GetBool(args, "overwrite", false))
                return CallToolResult.Error("Blueprint already exists; pass overwrite=true or choose another name.");

            var capturedBuildings = new JArray();
            var capturedDigs = new JArray();
            var seenObjects = new HashSet<int>();

            try
            {
                if (Grid.IsValidCell(0))
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell))
                                continue;

                            foreach (var layer in BlueprintScanLayers)
                            {
                                var go = Grid.Objects[cell, (int)layer];
                                if (go == null)
                                    continue;

                                int id = go.GetInstanceID();
                                if (seenObjects.Contains(id))
                                    continue;
                                seenObjects.Add(id);

                                var building = go.GetComponent<Building>();
                                if (building == null)
                                    continue;

                                string prefab = building.Def?.PrefabID ?? go.name;
                                int offX = x - minX;
                                int offY = y - minY;

                                var bObj = new JObject
                                {
                                    ["buildingdef"] = prefab
                                };
                                if (offX != 0 || offY != 0)
                                    bObj["offset"] = new JObject { ["x"] = offX, ["y"] = offY };

                                var pe = go.GetComponent<PrimaryElement>();
                                if (pe != null && pe.Element != null)
                                    bObj["selectedElements"] = new JArray(pe.Element.tag.ToString());

                                capturedBuildings.Add(bObj);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[BlueprintCapture] Error scanning grid: " + ex.Message);
            }

            var root = new JObject
            {
                ["friendlyname"] = Path.GetFileNameWithoutExtension(path),
                ["blueprintVersion"] = 2,
                ["buildings"] = capturedBuildings
            };
            if (capturedDigs.Count > 0)
                root["digcommands"] = capturedDigs;

            if (!WorldEditorExecutionAllowed(args))
            {
                return WorldEditorPreview("blueprint_capture", "/active/blueprints/" + combined, new JObject
                {
                    ["wouldCreate"] = path,
                    ["bounds"] = new JObject { ["minX"] = minX, ["minY"] = minY, ["maxX"] = maxX, ["maxY"] = maxY, ["width"] = maxX - minX + 1, ["height"] = maxY - minY + 1 },
                    ["capturedBuildings"] = capturedBuildings.Count
                });
            }

            if (!AtomicWriteBlueprint(path, root.ToString(Formatting.Indented), out string writeErr))
                return CallToolResult.Error(writeErr);

            int rectW = maxX - minX + 1;
            int rectH = maxY - minY + 1;
            ShowBlueprintVisualFeedback($"📐 蓝图框选捕获: {Path.GetFileNameWithoutExtension(path)} ({rectW}x{rectH}, {capturedBuildings.Count}建筑)");

            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["created"] = path,
                ["bounds"] = new JObject { ["minX"] = minX, ["minY"] = minY, ["maxX"] = maxX, ["maxY"] = maxY, ["width"] = rectW, ["height"] = rectH },
                ["buildings"] = capturedBuildings.Count
            });
        }

        private static CallToolResult BlueprintPlace(JObject args)
        {
            string path = FindBlueprintPath(Text(args, "name", "path", "target"));
            if (path == null)
                return CallToolResult.Error("Blueprint not found. Pass name=...");

            int? x = ToolUtil.GetInt(args, "x") ?? ToolUtil.GetInt(args, "centerX");
            int? y = ToolUtil.GetInt(args, "y") ?? ToolUtil.GetInt(args, "centerY");
            if (!x.HasValue || !y.HasValue)
                return CallToolResult.Error("blueprint place requires x and y top-left or origin coordinates");

            JObject root = ReadBlueprintJson(path);
            BlueprintRect rect = BlueprintBounds(root);
            var buildings = root["buildings"] as JArray ?? new JArray();
            int count = buildings.Count;

            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (args["materialOverrides"] is JObject mo)
            {
                foreach (var p in mo.Properties())
                    replacements[p.Name] = p.Value?.ToString() ?? "";
            }

            var materialsSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject item in buildings.OfType<JObject>())
            {
                if (item["selectedElements"] is JArray elements)
                {
                    for (int i = 0; i < elements.Count; i++)
                    {
                        string el = elements[i]?.ToString();
                        if (el != null && replacements.TryGetValue(el, out string nextEl))
                        {
                            elements[i] = nextEl;
                            el = nextEl;
                        }
                        if (!string.IsNullOrEmpty(el))
                            materialsSummary[el] = materialsSummary.ContainsKey(el) ? materialsSummary[el] + 1 : 1;
                    }
                }
            }

            string baseName = Path.GetFileNameWithoutExtension(path);
            if (args["dryRun"] == null)
                args["dryRun"] = true;

            if (!WorldEditorExecutionAllowed(args))
            {
                ShowBlueprintVisualFeedback($"📐 手持蓝图 [ {baseName} ] 准备放置于 ({x.Value},{y.Value}) [预览]");
                return JsonResult(new JObject
                {
                    ["dryRun"] = true,
                    ["name"] = baseName,
                    ["origin"] = new JObject { ["x"] = x.Value, ["y"] = y.Value },
                    ["bounds"] = new JObject { ["width"] = rect.Width, ["height"] = rect.Height },
                    ["buildings"] = count,
                    ["materialsRequired"] = JObject.FromObject(materialsSummary),
                    ["next"] = "repeat with dryRun=false confirm=true to place blueprint"
                });
            }

            bool nativeOnly = ToolUtil.GetBool(args, "native", false);
            bool placedViaMod = false;
            string modError = null;

            if (!nativeOnly && replacements.Count == 0)
            {
                placedViaMod = InvokeBlueprintsExpandedUse(path, x.Value, y.Value, out modError);
            }

            int placedNatively = 0;
            if (!placedViaMod)
            {
                int priority = ToolUtil.GetInt(args, "priority") ?? 5;
                foreach (JObject item in buildings.OfType<JObject>())
                {
                    string prefabId = item.Value<string>("buildingdef");
                    if (string.IsNullOrEmpty(prefabId) || prefabId == "dig")
                        continue;

                    int offX = item.SelectToken("offset.x")?.Value<int>() ?? 0;
                    int offY = item.SelectToken("offset.y")?.Value<int>() ?? 0;
                    string mat = (item["selectedElements"] as JArray)?.FirstOrDefault()?.ToString();

                    var buildArgs = CopyPayload(args);
                    buildArgs["domain"] = "planning";
                    buildArgs["action"] = "build_area";
                    buildArgs["prefabId"] = prefabId;
                    buildArgs["anchors"] = new JArray(new JObject { ["x"] = x.Value + offX, ["y"] = y.Value + offY });
                    buildArgs["priority"] = priority;
                    buildArgs["confirm"] = true;
                    if (!string.IsNullOrEmpty(mat))
                        buildArgs["material"] = mat;

                    try
                    {
                        var result = BuildingControlTools.ControlBuildingFromVirtualFile(buildArgs);
                        if (!WorldEditorResultFailed(result, args))
                            placedNatively++;
                    }
                    catch { }
                }
            }

            ShowBlueprintVisualFeedback($"📐 已放置蓝图: {baseName} 于 ({x.Value},{y.Value})");
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["placed"] = Path.GetFileName(path),
                ["origin"] = new JObject { ["x"] = x.Value, ["y"] = y.Value },
                ["buildings"] = count,
                ["placedNatively"] = placedNatively,
                ["method"] = placedViaMod ? "BlueprintsExpandedMod" : "NativePlacement"
            });
        }

        private static CallToolResult BlueprintCancel(JObject args)
        {
            string name = Text(args, "name", "path", "target");
            int? x1 = ToolUtil.GetInt(args, "x1") ?? ToolUtil.GetInt(args, "minX") ?? ToolUtil.GetInt(args, "x");
            int? y1 = ToolUtil.GetInt(args, "y1") ?? ToolUtil.GetInt(args, "minY") ?? ToolUtil.GetInt(args, "y");
            int? x2 = ToolUtil.GetInt(args, "x2") ?? ToolUtil.GetInt(args, "maxX");
            int? y2 = ToolUtil.GetInt(args, "y2") ?? ToolUtil.GetInt(args, "maxY");

            if (!string.IsNullOrWhiteSpace(name) && (!x2.HasValue || !y2.HasValue))
            {
                string path = FindBlueprintPath(name);
                if (path != null && x1.HasValue && y1.HasValue)
                {
                    JObject bp = ReadBlueprintJson(path);
                    BlueprintRect rect = BlueprintBounds(bp);
                    x2 = x1.Value + rect.MaxX;
                    y2 = y1.Value + rect.MaxY;
                }
            }

            if (!x1.HasValue || !y1.HasValue || !x2.HasValue || !y2.HasValue)
                return CallToolResult.Error("blueprint cancel requires coordinates (x1, y1, x2, y2) or name with (x, y).");

            var orderArgs = CopyPayload(args);
            orderArgs["domain"] = "area";
            orderArgs["action"] = "cancel";
            orderArgs["x1"] = Math.Min(x1.Value, x2.Value);
            orderArgs["y1"] = Math.Min(y1.Value, y2.Value);
            orderArgs["x2"] = Math.Max(x1.Value, x2.Value);
            orderArgs["y2"] = Math.Max(y1.Value, y2.Value);
            orderArgs["confirm"] = true;

            var result = OrdersControlEntryTools.ControlOrders().Handler(orderArgs);
            string title = string.IsNullOrWhiteSpace(name) ? $"区域 ({orderArgs["x1"]},{orderArgs["y1"]})" : name;
            ShowBlueprintVisualFeedback($"📐 已取消蓝图建造订单: {title}");

            return JsonResult(new JObject
            {
                ["ok"] = !WorldEditorResultFailed(result, args),
                ["canceled"] = title,
                ["area"] = new JObject { ["x1"] = orderArgs["x1"], ["y1"] = orderArgs["y1"], ["x2"] = orderArgs["x2"], ["y2"] = orderArgs["y2"] },
                ["details"] = MapEditResultValue(result)
            });
        }
    }
}
