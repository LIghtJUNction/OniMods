using System;
using System.Collections.Generic;
using System.Linq;
using Klei.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
                private static JObject WithRoutedMode(JObject args)
                {
                    var routed = (JObject)args.DeepClone();
                    string mode = routed["mode"]?.ToString();
                    routed["action"] = string.IsNullOrWhiteSpace(mode) ? "mark" : mode;
                    return routed;
                }

                private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
                {
                    if (!Grid.IsValidCell(cell)) return false;
                    if (!ToolUtil.CellMatchesWorld(cell, worldId)) return false;
                    int x = Grid.CellColumn(cell);
                    int y = Grid.CellRow(cell);
                    return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
                }

                private static int SweepCell(Pickupable pickupable)
                {
                    if (pickupable == null)
                        return Grid.InvalidCell;
                    if (Grid.IsValidCell(pickupable.cachedCell))
                        return pickupable.cachedCell;
                    return Grid.PosToCell(pickupable.gameObject);
                }

                private static int GetTargetWorldId(GameObject go, int cell)
                {
                    if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                        return Grid.WorldIdx[cell];

                    var component = go.GetComponent<KMonoBehaviour>();
                    return component != null ? component.GetMyWorldId() : -1;
                }

                private static GameObject FindTarget(JObject args)
                {
                    int? id = ToolUtil.GetInt(args, "id");
                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    string queryRaw = args["query"]?.ToString()?.Trim();
                    bool explicitCell = x.HasValue && y.HasValue;
                    int? cell = explicitCell ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
                    if (!cell.HasValue && string.IsNullOrEmpty(queryRaw))
                    {
                        int searchX;
                        int searchY;
                        string searchError;
                        if (ToolUtil.TryResolveSearchCell(args, out searchX, out searchY, out searchError))
                            cell = Grid.XYToCell(searchX, searchY);
                    }
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
                        {
                            if (!string.IsNullOrEmpty(queryRaw) && !MatchesQuery(go, queryRaw))
                                continue;
                            return go;
                        }
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
                        {
                            if (!string.IsNullOrEmpty(queryRaw) && !MatchesQuery(go, queryRaw))
                                continue;
                            return go;
                        }
                    }

                    if (!id.HasValue && !string.IsNullOrEmpty(queryRaw))
                    {
                        foreach (var prioritizable in Components.Prioritizables.Items)
                        {
                            var go = prioritizable?.gameObject;
                            if (go == null) continue;
                            if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                            if (MatchesQuery(go, queryRaw))
                                return go;
                        }

                        foreach (var building in Components.BuildingCompletes.Items)
                        {
                            var go = building?.gameObject;
                            if (go == null) continue;
                            if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                            if (MatchesQuery(go, queryRaw))
                                return go;
                        }
                    }

                    return null;
                }
    }
}
