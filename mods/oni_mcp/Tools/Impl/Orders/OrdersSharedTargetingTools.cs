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
    }
}
