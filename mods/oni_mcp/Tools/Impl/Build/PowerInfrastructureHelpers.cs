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
        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2/worldId", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y", Required = false }
            };
            if (extra != null)
            {
                foreach (var pair in extra)
                    parameters[pair.Key] = pair.Value;
            }
            return parameters;
        }

        private static Dictionary<string, object> RoomInfo(Room room, int worldId, bool includeBuildings, bool includeCriteria)
        {
            var cavity = room.cavity;
            var info = new Dictionary<string, object>
            {
                ["name"] = ToolUtil.CleanName(room.GetProperName()),
                ["worldId"] = worldId,
                ["typeId"] = RoomTypeId(room.roomType),
                ["typeName"] = RoomTypeName(room.roomType),
                ["category"] = room.roomType?.category != null ? RoomTypeId(room.roomType.category) : null,
                ["size"] = cavity != null ? cavity.NumCells : 0,
                ["bounds"] = cavity != null ? new Dictionary<string, int>
                {
                    ["minX"] = cavity.minX,
                    ["minY"] = cavity.minY,
                    ["maxX"] = cavity.maxX,
                    ["maxY"] = cavity.maxY
                } : null,
                ["buildingCount"] = room.buildings?.Count ?? 0,
                ["plantCount"] = room.plants?.Count ?? 0,
                ["creatureCount"] = room.creatures?.Count ?? 0,
                ["otherEntityCount"] = room.otherEntities?.Count ?? 0,
                ["ownerCount"] = room.NumOwners(),
                ["effect"] = room.roomType?.effect,
                ["effects"] = room.roomType?.effects
            };

            if (includeCriteria && room.roomType != null)
            {
                info["criteria"] = RoomConstraints.RoomCriteriaString(room);
                info["effectText"] = room.roomType.GetRoomEffectsString();
            }

            if (includeBuildings)
                info["buildings"] = EntitySummaries(room.buildings, 40);

            return info;
        }

        private static List<Dictionary<string, object>> EntitySummaries(List<KPrefabID> entities, int limit)
        {
            var results = new List<Dictionary<string, object>>();
            if (entities == null)
                return results;

            foreach (var entity in entities)
            {
                if (entity == null || entity.gameObject == null)
                    continue;
                var go = entity.gameObject;
                int cell = Grid.PosToCell(go);
                int x;
                int y;
                CellXY(cell, out x, out y);
                var building = go.GetComponent<Building>();
                results.Add(new Dictionary<string, object>
                {
                    ["id"] = entity.InstanceID,
                    ["name"] = ToolUtil.CleanName(go.GetProperName()),
                    ["prefabId"] = building?.Def?.PrefabID ?? entity.PrefabTag.Name ?? go.name,
                    ["cell"] = cell,
                    ["x"] = x,
                    ["y"] = y
                });
                if (results.Count >= limit)
                    break;
            }

            return results;
        }

        private static int GetRoomWorldId(Room room)
        {
            var cells = room?.cavity?.cells;
            if (cells != null)
            {
                foreach (int cell in cells)
                {
                    if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                        return Grid.WorldIdx[cell];
                }
            }

            return -1;
        }

        private static bool MatchesRoomType(Room room, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            string needle = filter.Trim();
            return Contains(RoomTypeId(room.roomType), needle)
                || Contains(RoomTypeName(room.roomType), needle)
                || Contains(room.GetProperName(), needle);
        }

        private static string RoomTypeId(Resource resource)
        {
            return resource?.Id ?? "Unknown";
        }

        private static string RoomTypeName(Resource resource)
        {
            return ToolUtil.CleanName(resource?.Name ?? RoomTypeId(resource));
        }

        private static double Round(float value, int digits)
        {
            return Math.Round(ToolUtil.SafeFloat(value), digits);
        }

        private static void CellXY(int cell, out int x, out int y)
        {
            if (!Grid.IsValidCell(cell))
            {
                x = -1;
                y = -1;
                return;
            }

            Grid.CellToXY(cell, out x, out y);
        }

        private sealed class CircuitAggregate
        {
            public ushort CircuitId;
            public int BatteryCount;
            public float BatteryCapacityJ;
            public float BatteryStoredJ;
            public int GeneratorCount;
            public int GeneratorOperational;
            public float GeneratorWatts;
            public int ConsumerCount;
            public int ConsumerOperational;
            public float ConsumerBaseWatts;
            public float ConsumerNeededWatts;
            public float ConsumerUsedWatts;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["circuitId"] = IsUnconnectedCircuit(CircuitId) ? "-1" : CircuitId.ToString(),
                    ["circuitKey"] = IsUnconnectedCircuit(CircuitId) ? "-1" : CircuitId.ToString(),
                    ["isConnected"] = !IsUnconnectedCircuit(CircuitId),
                    ["batteryCount"] = BatteryCount,
                    ["batteryCapacityJ"] = Round(BatteryCapacityJ, 1),
                    ["batteryStoredJ"] = Round(BatteryStoredJ, 1),
                    ["batteryStoredPercent"] = BatteryCapacityJ > 0f ? Round(BatteryStoredJ / BatteryCapacityJ * 100f, 2) : 0,
                    ["generatorCount"] = GeneratorCount,
                    ["generatorOperational"] = GeneratorOperational,
                    ["generatorWatts"] = Round(GeneratorWatts, 1),
                    ["consumerCount"] = ConsumerCount,
                    ["consumerOperational"] = ConsumerOperational,
                    ["consumerBaseWatts"] = Round(ConsumerBaseWatts, 1),
                    ["consumerNeededWatts"] = Round(ConsumerNeededWatts, 1),
                    ["consumerUsedWatts"] = Round(ConsumerUsedWatts, 1),
                    ["netRatedWatts"] = Round(GeneratorWatts - ConsumerBaseWatts, 1),
                    ["netActiveWatts"] = Round(GeneratorWatts - ConsumerNeededWatts, 1)
                };
            }
        }
    }
}
