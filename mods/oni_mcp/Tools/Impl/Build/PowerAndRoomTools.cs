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
        public static McpTool InfrastructureReadControl()
        {
            return new McpTool
            {
                Name = "infrastructure_read_control",
                Group = "infrastructure",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "infrastructure_read" },
                Tags = new List<string> { "power", "electricity", "rooms", "infrastructure", "电力", "房间", "基础设施" },
                Description = "电力/房间只读聚合入口：action=power_summary/power_ports/ports/rooms；ports 返回电力、液管、气管、信号、运输端口。",
                Parameters = InfrastructureReadParams(),
                Handler = args =>
                {
                    args = args ?? new JObject();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var forwarded = new JObject(args);
                    forwarded.Remove("action");

                    switch (action)
                    {
                        case "power_summary":
                            return GetPowerSummary().Handler(forwarded);
                        case "power_ports":
                            return GetBuildingPowerPorts().Handler(forwarded);
                        case "ports":
                        case "utility_ports":
                        case "all_ports":
                            return InfrastructurePortReadTools.ReadPorts(forwarded);
                        case "rooms":
                            return ListRooms().Handler(forwarded);
                        default:
                            return CallToolResult.Error("Unsupported action. Use power_summary, power_ports, ports, or rooms.");
                    }
                }
            };
        }

        public static McpTool GetBuildingPowerPortsCompat()
        {
            var tool = GetBuildingPowerPorts();
            tool.Hidden = true;
            tool.Description = "兼容入口：请使用 read_control domain=infrastructure action=power_ports。";
            return tool;
        }

        public static McpTool GetPowerSummaryCompat()
        {
            var tool = GetPowerSummary();
            tool.Hidden = true;
            tool.Description = "兼容入口：请使用 read_control domain=infrastructure action=power_summary。";
            return tool;
        }

        public static McpTool ListRoomsCompat()
        {
            var tool = ListRooms();
            tool.Hidden = true;
            tool.Description = "兼容入口：请使用 read_control domain=infrastructure action=rooms。";
            return tool;
        }

        public static McpTool GetBuildingPowerPorts()
        {
            return new McpTool
            {
                Name = "building_power_ports",
                Group = "power",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "power_ports_list", "building_power_connection_points", "power_connection_points" },
                Tags = new List<string> { "power", "electricity", "ports", "connector", "wire", "电力", "接口", "接线" },
                Description = "列出指定区域内已建建筑和蓝图建筑的电力接口格：锚点、输入/输出端口、相对偏移和可接线状态，方便直接接线而不是猜文本地图",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；默认当前激活世界", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 关键词筛选，例如 battery、generator、transformer、wire", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 120，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 120, 500);

                    var results = new List<Dictionary<string, object>>();
                    var seen = new HashSet<string>();
                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        var go = building?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (rect != null && !CellInRect(cell, rect, worldId))
                            continue;
                        if (!MatchesQuery(go, query))
                            continue;

                        var def = building.Def;
                        if (def == null)
                            continue;

                        bool hasInput = def.RequiresPowerInput;
                        bool hasOutput = def.RequiresPowerOutput;
                        if (!hasInput && !hasOutput)
                            continue;

                        results.Add(BuildingPowerPortInfo(go, def, hasInput, hasOutput, "built"));
                        seen.Add(BuildingPortKey(go, def));
                        if (results.Count >= limit)
                            break;
                    }

                    if (results.Count < limit)
                    {
                        foreach (var constructable in FindConstructables(worldId))
                        {
                            var go = constructable?.gameObject;
                            if (go == null)
                                continue;
                            int cell = Grid.PosToCell(go);
                            if (rect != null && !CellInRect(cell, rect, worldId))
                                continue;
                            if (!MatchesQuery(go, query))
                                continue;

                            var building = go.GetComponent<Building>();
                            var kpid = go.GetComponent<KPrefabID>();
                            var def = building?.Def ?? Assets.GetBuildingDef(kpid?.PrefabTag.Name ?? go.name);
                            if (def == null)
                                continue;
                            if (seen.Contains(BuildingPortKey(go, def)))
                                continue;

                            bool hasInput = def.RequiresPowerInput;
                            bool hasOutput = def.RequiresPowerOutput;
                            if (!hasInput && !hasOutput)
                                continue;

                            results.Add(BuildingPowerPortInfo(go, def, hasInput, hasOutput, "blueprint"));
                            if (results.Count >= limit)
                                break;
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                        ["rect"] = rect,
                        ["buildings"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

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
