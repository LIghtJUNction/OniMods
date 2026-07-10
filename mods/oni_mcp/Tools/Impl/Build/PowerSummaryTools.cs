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
                    var diagnostics = new List<Dictionary<string, object>>();

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
                        if (IsUnconnectedCircuit(consumer.CircuitID) && diagnostics.Count < limit)
                            diagnostics.Add(PowerIssueInfo(consumer.gameObject, "consumer_unconnected", "Consumer is on circuit -1; check its power input port cell for a built wire/blueprint and verify the wire is connected to a powered circuit."));
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
                    result["diagnostics"] = new Dictionary<string, object>
                    {
                        ["issueCount"] = diagnostics.Count,
                        ["items"] = diagnostics,
                        ["next"] = diagnostics.Count == 0
                            ? "No unconnected consumers detected in this summary."
                            : "Use building_power_ports on the returned coordinates or a small rect around the device to verify exact port cells and missing wires."
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

    }
}
