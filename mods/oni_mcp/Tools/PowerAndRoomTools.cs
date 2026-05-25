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
    public static class PowerAndRoomTools
    {
        public static McpTool GetPowerSummary()
        {
            return new McpTool
            {
                Name = "power_summary",
                Group = "power",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "power_circuits_summary", "power_status" },
                Tags = new List<string> { "power", "electricity", "circuits", "generators", "batteries", "consumers", "电力", "电路" },
                Description = "汇总电力系统：发电机额定功率、消费者负载、电池容量/电量，并按 circuitId 聚合，适合快速发现供电缺口",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；默认当前激活世界，传 -1 汇总全部世界", Required = false },
                    ["includeDetails"] = new McpToolParameter { Type = "boolean", Description = "是否返回发电机/电池/消费者明细，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "明细最多返回多少项，默认 80，最大 300", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int worldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? -1);
                    bool includeDetails = ToolUtil.GetBool(args, "includeDetails", false);
                    int limit = ToolUtil.ClampLimit(args, 80, 300);

                    var circuits = new Dictionary<ushort, CircuitAggregate>();
                    var batteries = new List<Dictionary<string, object>>();
                    var generators = new List<Dictionary<string, object>>();
                    var consumers = new List<Dictionary<string, object>>();

                    int batteryCount = 0;
                    float batteryCapacityJ = 0f;
                    float batteryStoredJ = 0f;
                    float batteryWattsUsed = 0f;

                    foreach (var battery in Components.Batteries.Items)
                    {
                        if (battery == null || !ToolUtil.GameObjectMatchesWorld(battery.gameObject, worldId))
                            continue;

                        batteryCount++;
                        float capacity = ToolUtil.SafeFloat(battery.Capacity);
                        float stored = ToolUtil.SafeFloat(battery.JoulesAvailable);
                        float wattsUsed = ToolUtil.SafeFloat(battery.WattsUsed);
                        batteryCapacityJ += capacity;
                        batteryStoredJ += stored;
                        batteryWattsUsed += wattsUsed;

                        var circuit = GetCircuit(circuits, battery.CircuitID);
                        circuit.BatteryCount++;
                        circuit.BatteryCapacityJ += capacity;
                        circuit.BatteryStoredJ += stored;

                        if (includeDetails && batteries.Count < limit)
                            batteries.Add(PowerDeviceInfo(battery.gameObject, "battery", battery.CircuitID, capacity, stored, wattsUsed, null, IsOperational(battery.gameObject)));
                    }

                    int generatorCount = 0;
                    int generatorOperational = 0;
                    int generatorEmpty = 0;
                    float generatorWatts = 0f;
                    float generatorStoredJ = 0f;

                    foreach (var generator in Components.Generators.Items)
                    {
                        if (generator == null || !ToolUtil.GameObjectMatchesWorld(generator.gameObject, worldId))
                            continue;

                        generatorCount++;
                        bool operational = IsOperational(generator.gameObject);
                        if (operational)
                            generatorOperational++;
                        if (generator.IsEmpty)
                            generatorEmpty++;

                        float rating = ToolUtil.SafeFloat(generator.WattageRating);
                        float stored = ToolUtil.SafeFloat(generator.JoulesAvailable);
                        generatorWatts += rating;
                        generatorStoredJ += stored;

                        var circuit = GetCircuit(circuits, generator.CircuitID);
                        circuit.GeneratorCount++;
                        circuit.GeneratorOperational++;
                        circuit.GeneratorWatts += rating;

                        if (includeDetails && generators.Count < limit)
                            generators.Add(PowerDeviceInfo(generator.gameObject, "generator", generator.CircuitID, generator.Capacity, stored, null, rating, operational, generator.IsEmpty));
                    }

                    int consumerCount = 0;
                    int consumerOperational = 0;
                    int consumerPowered = 0;
                    float consumerBaseWatts = 0f;
                    float consumerNeededWatts = 0f;
                    float consumerUsedWatts = 0f;

                    foreach (var consumer in Components.EnergyConsumers.Items)
                    {
                        if (consumer == null || !ToolUtil.GameObjectMatchesWorld(consumer.gameObject, worldId))
                            continue;

                        consumerCount++;
                        bool operational = IsOperational(consumer.gameObject);
                        if (operational)
                            consumerOperational++;
                        if (consumer.IsPowered)
                            consumerPowered++;

                        float baseWatts = ToolUtil.SafeFloat(consumer.BaseWattageRating);
                        float neededWatts = ToolUtil.SafeFloat(consumer.WattsNeededWhenActive);
                        float usedWatts = ToolUtil.SafeFloat(consumer.WattsUsed);
                        consumerBaseWatts += baseWatts;
                        consumerNeededWatts += neededWatts;
                        consumerUsedWatts += usedWatts;

                        var circuit = GetCircuit(circuits, consumer.CircuitID);
                        circuit.ConsumerCount++;
                        circuit.ConsumerOperational++;
                        circuit.ConsumerBaseWatts += baseWatts;
                        circuit.ConsumerNeededWatts += neededWatts;
                        circuit.ConsumerUsedWatts += usedWatts;

                        if (includeDetails && consumers.Count < limit)
                            consumers.Add(PowerDeviceInfo(consumer.gameObject, "consumer", consumer.CircuitID, null, null, usedWatts, baseWatts, operational, false, consumer.IsPowered));
                    }

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["batteryCount"] = batteryCount,
                            ["batteryCapacityJ"] = Round(batteryCapacityJ, 1),
                            ["batteryStoredJ"] = Round(batteryStoredJ, 1),
                            ["batteryStoredPercent"] = batteryCapacityJ > 0f ? Round(batteryStoredJ / batteryCapacityJ * 100f, 2) : 0,
                            ["generatorCount"] = generatorCount,
                            ["generatorOperational"] = generatorOperational,
                            ["generatorEmpty"] = generatorEmpty,
                            ["generatorWatts"] = Round(generatorWatts, 1),
                            ["generatorStoredJ"] = Round(generatorStoredJ, 1),
                            ["consumerCount"] = consumerCount,
                            ["consumerOperational"] = consumerOperational,
                            ["consumerPowered"] = consumerPowered,
                            ["consumerBaseWatts"] = Round(consumerBaseWatts, 1),
                            ["consumerNeededWatts"] = Round(consumerNeededWatts, 1),
                            ["consumerUsedWatts"] = Round(consumerUsedWatts, 1),
                            ["netRatedWatts"] = Round(generatorWatts - consumerBaseWatts, 1),
                            ["netActiveWatts"] = Round(generatorWatts - consumerNeededWatts, 1),
                            ["batteryWattsUsed"] = Round(batteryWattsUsed, 1)
                        },
                        ["circuits"] = circuits.Values
                            .OrderByDescending(item => item.ConsumerBaseWatts + item.GeneratorWatts + item.BatteryCapacityJ)
                            .Select(item => item.ToDictionary())
                            .ToList()
                    };

                    if (includeDetails)
                    {
                        result["batteries"] = batteries;
                        result["generators"] = generators;
                        result["consumers"] = consumers;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListRooms()
        {
            return new McpTool
            {
                Name = "rooms_list",
                Group = "rooms",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "room_list", "rooms_overview" },
                Tags = new List<string> { "rooms", "morale", "room-type", "base", "房间", "士气" },
                Description = "列出房间系统状态：房间类型、大小、边界、对象计数和房间效果，适合检查士气房间是否成型",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；默认当前激活世界，传 -1 返回全部世界", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "按房间类型 id/name 过滤，例如 Barracks、GreatHall、PowerPlant", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "是否返回房间内建筑明细，默认 false", Required = false },
                    ["includeCriteria"] = new McpToolParameter { Type = "boolean", Description = "是否返回房间条件文本，默认 false", Required = false },
                    ["includeNeutral"] = new McpToolParameter { Type = "boolean", Description = "是否返回 Neutral/杂间；默认 false，type 明确过滤时不强制隐藏", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回房间数量，默认 80，最大 300", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance?.roomProber == null)
                        return CallToolResult.Error("Room system not initialized");

                    int worldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? -1);
                    string typeFilter = args["type"]?.ToString();
                    bool includeBuildings = ToolUtil.GetBool(args, "includeBuildings", false);
                    bool includeCriteria = ToolUtil.GetBool(args, "includeCriteria", false);
                    bool includeNeutral = ToolUtil.GetBool(args, "includeNeutral", false) || !string.IsNullOrWhiteSpace(typeFilter);
                    int limit = ToolUtil.ClampLimit(args, 80, 300);

                    var rooms = new List<Dictionary<string, object>>();
                    var typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    foreach (var room in Game.Instance.roomProber.rooms)
                    {
                        if (room == null || room.IsNull())
                            continue;

                        int roomWorldId = GetRoomWorldId(room);
                        if (worldId >= 0 && roomWorldId != worldId)
                            continue;
                        if (!MatchesRoomType(room, typeFilter))
                            continue;

                        string typeId = RoomTypeId(room.roomType);
                        if (!typeCounts.ContainsKey(typeId))
                            typeCounts[typeId] = 0;
                        typeCounts[typeId]++;
                        if (!includeNeutral && IsNeutralRoom(typeId))
                            continue;

                        if (rooms.Count < limit)
                            rooms.Add(RoomInfo(room, roomWorldId, includeBuildings, includeCriteria));
                    }

                    int matched = typeCounts.Values.Sum();
                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["returned"] = rooms.Count,
                        ["matched"] = matched,
                        ["typeCounts"] = typeCounts.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value),
                        ["defaultFilter"] = includeNeutral ? "none" : "neutral rooms hidden; pass includeNeutral=true to list them",
                        ["rooms"] = rooms
                    };

                    if (rooms.Count == 0 && matched > 0)
                        result["note"] = "Neutral rooms are hidden by default; set includeNeutral=true to see all rooms.";

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

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

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.IsNullOrWhiteSpace(query)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
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
