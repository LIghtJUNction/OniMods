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
    public static partial class PowerAndRoomTools
    {
        private static CircuitAggregate GetCircuit(Dictionary<ushort, CircuitAggregate> circuits, ushort id)
        {
            CircuitAggregate aggregate;
            if (!circuits.TryGetValue(id, out aggregate))
            {
                aggregate = new CircuitAggregate { CircuitId = id };
                circuits[id] = aggregate;
            }
            return aggregate;
        }

        private static Dictionary<string, McpToolParameter> InfrastructureReadParams()
        {
            return RectParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "读取类型：power_summary 电力摘要；power_ports 建筑电力接口；rooms 房间列表", Required = true, EnumValues = new List<string> { "power_summary", "power_ports", "rooms" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=power_ports 时按名称或 prefabId 关键词筛选", Required = false },
                ["type"] = new McpToolParameter { Type = "string", Description = "action=rooms 时按房间类型 id/name 过滤，例如 Barracks、GreatHall、PowerPlant", Required = false },
                ["includeDetails"] = new McpToolParameter { Type = "boolean", Description = "action=power_summary 时是否返回发电机/电池/消费者明细，默认 false", Required = false },
                ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "action=rooms 时是否返回房间内建筑明细，默认 false", Required = false },
                ["includeCriteria"] = new McpToolParameter { Type = "boolean", Description = "action=rooms 时是否返回房间条件文本，默认 false", Required = false },
                ["includeNeutral"] = new McpToolParameter { Type = "boolean", Description = "action=rooms 时是否返回 Neutral/杂间，默认 false", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量；沿用对应旧工具默认值和上限", Required = false }
            });
        }

        private static Dictionary<string, object> PowerDeviceInfo(GameObject go, string role, ushort circuitId, float? capacityJ, float? storedJ, float? wattsUsed, float? wattsRating, bool operational, bool empty = false, bool powered = false)
        {
            int cell = go != null ? Grid.PosToCell(go) : Grid.InvalidCell;
            var building = go != null ? go.GetComponent<Building>() : null;
            var kpid = go != null ? go.GetComponent<KPrefabID>() : null;

            int x;
            int y;
            CellXY(cell, out x, out y);

            var result = new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go?.GetInstanceID() ?? -1,
                ["name"] = go != null ? ToolUtil.CleanName(go.GetProperName()) : null,
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go?.name,
                ["role"] = role,
                ["cell"] = cell,
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? (object)Grid.WorldIdx[cell] : null,
                ["circuitId"] = IsUnconnectedCircuit(circuitId) ? "-1" : circuitId.ToString(),
                ["circuitKey"] = IsUnconnectedCircuit(circuitId) ? "-1" : circuitId.ToString(),
                ["capacityJ"] = capacityJ.HasValue ? (object)Round(capacityJ.Value, 1) : null,
                ["storedJ"] = storedJ.HasValue ? (object)Round(storedJ.Value, 1) : null,
                ["storedPercent"] = capacityJ.HasValue && capacityJ.Value > 0f && storedJ.HasValue ? (object)Round(storedJ.Value / capacityJ.Value * 100f, 2) : null,
                ["wattsUsed"] = wattsUsed.HasValue ? (object)Round(wattsUsed.Value, 1) : null,
                ["wattsRating"] = wattsRating.HasValue ? (object)Round(wattsRating.Value, 1) : null,
                ["operational"] = operational,
                ["empty"] = empty,
                ["powered"] = powered,
                ["isConnected"] = !IsUnconnectedCircuit(circuitId)
            };
            return result;
        }

        private static Dictionary<string, object> PowerIssueInfo(GameObject go, string reasonCode, string reason)
        {
            var building = go != null ? go.GetComponent<Building>() : null;
            var def = building?.Def;
            var info = TargetInfo(go);
            info["reasonCode"] = reasonCode;
            info["reason"] = reason;
            info["ports"] = def != null
                ? BuildingPowerPortInfo(go, def, def.RequiresPowerInput, def.RequiresPowerOutput)["ports"]
                : null;
            return info;
        }

        private static Dictionary<string, object> BuildingPowerPortInfo(GameObject go, BuildingDef def, bool hasInput, bool hasOutput, string state = "built")
        {
            int anchorCell = Grid.PosToCell(go);
            int anchorX;
            int anchorY;

            var building = go.GetComponent<Building>();
            int bottomLeft = building != null ? building.GetBottomLeftCell() : Grid.InvalidCell;
            if (Grid.IsValidCell(bottomLeft))
                anchorCell = bottomLeft;
            CellXY(anchorCell, out anchorX, out anchorY);

            var rotatable = go.GetComponent<Rotatable>();
            Orientation orientation = rotatable != null ? rotatable.GetOrientation() : Orientation.Neutral;

            var inputPort = BuildPortInfo(def, anchorCell, anchorX, anchorY, orientation, def.PowerInputOffset, "input", hasInput, hasInput ? building?.GetPowerInputCell() ?? Grid.InvalidCell : Grid.InvalidCell);
            var outputPort = BuildPortInfo(def, anchorCell, anchorX, anchorY, orientation, def.PowerOutputOffset, "output", hasOutput, hasOutput ? building?.GetPowerOutputCell() ?? Grid.InvalidCell : Grid.InvalidCell);

            var ports = new List<Dictionary<string, object>>();
            if (inputPort != null)
                ports.Add(inputPort);
            if (outputPort != null)
                ports.Add(outputPort);

            var result = TargetInfo(go);
            result["state"] = state;
            result["isBlueprint"] = state == "blueprint";
            result["worldId"] = Grid.IsValidCell(anchorCell) && Grid.IsWorldValidCell(anchorCell) ? (object)Grid.WorldIdx[anchorCell] : null;
            result["anchorCell"] = anchorCell;
            result["anchorX"] = anchorX;
            result["anchorY"] = anchorY;
            result["orientation"] = orientation.ToString();
            result["requiresPowerInput"] = def.RequiresPowerInput;
            result["requiresPowerOutput"] = def.RequiresPowerOutput;
            result["powerWatts"] = Math.Round(def.EnergyConsumptionWhenActive, 1);
            result["ports"] = ports;
            return result;
        }

        private static string BuildingPortKey(GameObject go, BuildingDef def)
        {
            if (go == null)
                return "";
            var building = go.GetComponent<Building>();
            int anchorCell = building != null ? building.GetBottomLeftCell() : Grid.PosToCell(go);
            int worldId = Grid.IsValidCell(anchorCell) && Grid.IsWorldValidCell(anchorCell) ? Grid.WorldIdx[anchorCell] : -1;
            return (def?.PrefabID ?? go.name) + "|" + anchorCell + "|" + worldId;
        }

        private static IEnumerable<Constructable> FindConstructables(int worldId)
        {
            Constructable[] constructables;
            try
            {
                constructables = UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None);
            }
            catch
            {
                yield break;
            }

            foreach (var constructable in constructables)
            {
                if (constructable == null || constructable.gameObject == null)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(constructable.gameObject, worldId))
                    continue;
                yield return constructable;
            }
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var building = go.GetComponent<Building>();
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

        private static Dictionary<string, object> BuildPortInfo(BuildingDef def, int anchorCell, int anchorX, int anchorY, Orientation orientation, CellOffset offset, string role, bool required, int absoluteCell)
        {
            if (!required)
                return null;

            CellOffset rotated = RotatedOffset(def, orientation, offset);
            int computedCell = Grid.IsValidCell(anchorCell) ? Grid.OffsetCell(anchorCell, rotated) : Grid.InvalidCell;
            int cell = Grid.IsValidCell(absoluteCell) ? absoluteCell : computedCell;
            int x;
            int y;
            CellXY(cell, out x, out y);
            var wire = PowerWireAtCell(cell);

            return new Dictionary<string, object>
            {
                ["role"] = role,
                ["required"] = true,
                ["offset"] = new Dictionary<string, object>
                {
                    ["x"] = rotated.x,
                    ["y"] = rotated.y
                },
                ["cell"] = cell,
                ["x"] = x,
                ["y"] = y,
                ["dx"] = x - anchorX,
                ["dy"] = y - anchorY,
                ["hasWire"] = wire != null,
                ["wire"] = wire
            };
        }

        private static Dictionary<string, object> PowerWireAtCell(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return null;

            return LayerObjectInfo(cell, ObjectLayer.Wire, "wire")
                ?? LayerObjectInfo(cell, ObjectLayer.WireTile, "wire_tile")
                ?? LayerObjectInfo(cell, ObjectLayer.ReplacementWire, "replacement_wire");
        }

        private static Dictionary<string, object> LayerObjectInfo(int cell, ObjectLayer layer, string kind)
        {
            var go = Grid.Objects[cell, (int)layer];
            if (go == null)
                return null;

            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            string prefabId = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;
            return new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["layer"] = layer.ToString(),
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = prefabId,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["ratingW"] = InferWireRating(prefabId),
                ["ratingNote"] = "ratingW is inferred from prefab id; use as overload triage, not an exact circuit simulation."
            };
        }

        private static int? InferWireRating(string prefabId)
        {
            if (string.IsNullOrWhiteSpace(prefabId))
                return null;
            if (prefabId.IndexOf("HighWattage", StringComparison.OrdinalIgnoreCase) >= 0)
                return 20000;
            if (prefabId.IndexOf("Conductive", StringComparison.OrdinalIgnoreCase) >= 0)
                return 2000;
            if (prefabId.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1000;
            return null;
        }

        private static CellOffset RotatedOffset(BuildingDef def, Orientation orientation, CellOffset offset)
        {
            if (def == null)
                return offset;
            try
            {
                return Rotatable.GetRotatedCellOffset(offset, orientation);
            }
            catch
            {
                return offset;
            }
        }

        private static bool IsNeutralRoom(string typeId)
        {
            return string.Equals(typeId, "Neutral", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnconnectedCircuit(ushort circuitId)
        {
            return circuitId == ushort.MaxValue;
        }

        private static bool IsOperational(GameObject go)
        {
            var operational = go != null ? go.GetComponent<Operational>() : null;
            return operational != null && operational.IsOperational;
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell))
                return false;
            if (!ToolUtil.CellMatchesWorld(cell, worldId))
                return false;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static bool MatchesQuery(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            var kpid = go.GetComponent<KPrefabID>();
            var building = go.GetComponent<Building>();
            string prefabId = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;
            string name = ToolUtil.CleanName(go.GetProperName());
            return Contains(name, q) || Contains(prefabId, q) || Contains(go.name, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(query)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

    }
}
