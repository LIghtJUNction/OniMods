using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static class ColonyPlumbingLogicDataCollector
    {
        public static JObject BuildPlumbingDetailed(ConduitType conduitType, int worldId, int targetNetworkId = -1)
        {
            if (Game.Instance == null)
                return new JObject { ["status"] = "offline", ["reason"] = "Game not initialized" };

            int activeWorld = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0);
            var flow = conduitType == ConduitType.Liquid ? Game.Instance.liquidConduitFlow : Game.Instance.gasConduitFlow;
            if (flow == null)
                return new JObject { ["status"] = "unavailable", ["reason"] = "Flow manager not ready" };

            ObjectLayer layer = conduitType == ConduitType.Liquid ? ObjectLayer.LiquidConduit : ObjectLayer.GasConduit;
            int total = 0, flowing = 0, blocked = 0, empty = 0;
            float totalMass = 0f;
            var elementMass = new Dictionary<string, float>();
            var networkMap = new Dictionary<int, JObject>();
            var bottlenecks = new JArray();

            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != activeWorld))
                    continue;

                var go = Grid.Objects[cell, (int)layer];
                if (go == null && !flow.HasConduit(cell))
                    continue;

                total++;
                var conduit = flow.GetConduit(cell);
                var contents = conduit.GetContents(flow);
                var permitted = flow.GetPermittedFlow(cell);
                int netId = flow.GetNetwork(conduit)?.id ?? -1;

                if (targetNetworkId >= 0 && netId != targetNetworkId)
                    continue;

                bool isCellEmpty = contents.mass <= 0f || contents.element == SimHashes.Vacuum;
                bool isCellFull = flow.IsConduitFull(cell);
                bool isCellBlocked = isCellFull && (permitted == ConduitFlow.FlowDirections.None);

                if (isCellEmpty) empty++;
                else if (isCellBlocked) blocked++;
                else flowing++;

                if (!isCellEmpty)
                {
                    totalMass += contents.mass;
                    string elemName = contents.element.ToString();
                    if (!elementMass.ContainsKey(elemName)) elementMass[elemName] = 0f;
                    elementMass[elemName] += contents.mass;
                }

                if (netId >= 0)
                {
                    if (!networkMap.TryGetValue(netId, out var netObj))
                    {
                        netObj = new JObject
                        {
                            ["networkId"] = netId,
                            ["segmentCount"] = 0,
                            ["totalMassKg"] = 0.0,
                            ["elements"] = new JObject(),
                            ["sources"] = new JArray(),
                            ["sinks"] = new JArray()
                        };
                        networkMap[netId] = netObj;
                    }

                    netObj["segmentCount"] = (int)netObj["segmentCount"] + 1;
                    if (!isCellEmpty)
                    {
                        netObj["totalMassKg"] = Math.Round((double)netObj["totalMassKg"] + contents.mass, 2);
                        var elemObj = (JObject)netObj["elements"];
                        string en = contents.element.ToString();
                        elemObj[en] = Math.Round((elemObj[en]?.Value<double>() ?? 0.0) + contents.mass, 2);
                    }

                    InspectCellPorts(cell, conduitType, (JArray)netObj["sources"], (JArray)netObj["sinks"]);
                }

                if (isCellBlocked && bottlenecks.Count < 20)
                {
                    Vector2I xy = Grid.CellToXY(cell);
                    bottlenecks.Add(new JObject
                    {
                        ["cell"] = cell,
                        ["x"] = xy.x,
                        ["y"] = xy.y,
                        ["element"] = contents.element.ToString(),
                        ["massKg"] = Math.Round(contents.mass, 2),
                        ["reason"] = "Pipe is full with no permitted forward flow"
                    });
                }
            }

            var networksArr = new JArray();
            foreach (var kv in networkMap.Values)
                networksArr.Add(kv);

            var elemSummary = new JObject();
            foreach (var kv in elementMass)
                elemSummary[kv.Key] = Math.Round(kv.Value, 1);

            return new JObject
            {
                ["worldId"] = activeWorld,
                ["conduitType"] = conduitType == ConduitType.Liquid ? "liquid" : "gas",
                ["status"] = "operational",
                ["summary"] = new JObject
                {
                    ["totalSegments"] = total,
                    ["flowingSegments"] = flowing,
                    ["blockedSegments"] = blocked,
                    ["emptySegments"] = empty,
                    ["totalMassKg"] = Math.Round(totalMass, 1)
                },
                ["elements"] = elemSummary,
                ["networks"] = networksArr,
                ["bottlenecks"] = bottlenecks
            };
        }

        public static JObject BuildConduitCellDetail(int cell, ConduitType conduitType)
        {
            if (Game.Instance == null || !Grid.IsValidCell(cell))
                return new JObject { ["status"] = "invalid_cell" };

            var flow = conduitType == ConduitType.Liquid ? Game.Instance.liquidConduitFlow : Game.Instance.gasConduitFlow;
            ObjectLayer layer = conduitType == ConduitType.Liquid ? ObjectLayer.LiquidConduit : ObjectLayer.GasConduit;
            var go = Grid.Objects[cell, (int)layer];
            if (go == null && !flow.HasConduit(cell))
                return new JObject { ["cell"] = cell, ["hasConduit"] = false };

            var conduit = flow.GetConduit(cell);
            var contents = conduit.GetContents(flow);
            var permitted = flow.GetPermittedFlow(cell);
            var dirs = FlowDirectionsToList(permitted);
            Vector2I xy = Grid.CellToXY(cell);

            return new JObject
            {
                ["cell"] = cell,
                ["x"] = xy.x,
                ["y"] = xy.y,
                ["worldId"] = Grid.WorldIdx[cell],
                ["conduitType"] = go?.name ?? (conduitType == ConduitType.Liquid ? "LiquidConduit" : "GasConduit"),
                ["networkId"] = flow.GetNetwork(conduit)?.id ?? -1,
                ["flow"] = new JObject
                {
                    ["directions"] = new JArray(dirs),
                    ["glyph"] = FlowGlyph(permitted),
                    ["vector"] = FlowVector(permitted),
                    ["isFull"] = flow.IsConduitFull(cell),
                    ["isEmpty"] = contents.mass <= 0f
                },
                ["contents"] = new JObject
                {
                    ["element"] = contents.element != SimHashes.Vacuum ? contents.element.ToString() : "None",
                    ["massKg"] = Math.Round(contents.mass, 2),
                    ["tempK"] = Math.Round(contents.temperature, 2),
                    ["disease"] = contents.diseaseCount > 0 && contents.diseaseIdx != byte.MaxValue ? Db.Get().Diseases[contents.diseaseIdx].Id : null,
                    ["diseaseCount"] = contents.diseaseCount
                }
            };
        }

        public static JObject BuildLogicCircuitsDetailed(int worldId, int targetCircuitId = -1)
        {
            if (Game.Instance == null || Game.Instance.logicCircuitSystem == null)
                return new JObject { ["status"] = "offline", ["reason"] = "Logic system not initialized" };

            int activeWorld = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0);
            var networks = Game.Instance.logicCircuitSystem.GetNetworks();
            var circuitsArr = new JArray();
            int activeCircuits = 0;

            if (networks != null)
            {
                foreach (var net in networks)
                {
                    if (!(net is LogicCircuitNetwork logicNet)) continue;
                    if (targetCircuitId >= 0 && logicNet.id != targetCircuitId) continue;

                    int bitsUsed = logicNet.GetBitsUsed();
                    bool isRibbon = bitsUsed > 1;
                    int outVal = logicNet.OutputValue;
                    bool isActive = outVal > 0;
                    if (isActive) activeCircuits++;

                    var ribbonBits = new JArray();
                    for (int bit = 0; bit < 4; bit++)
                        ribbonBits.Add(logicNet.IsBitActive(bit) ? 1 : 0);

                    circuitsArr.Add(new JObject
                    {
                        ["circuitId"] = logicNet.id,
                        ["isRibbon"] = isRibbon,
                        ["bitsUsed"] = bitsUsed,
                        ["signal"] = isActive ? 1 : 0,
                        ["state"] = isActive ? "GREEN" : "RED",
                        ["outputValue"] = outVal,
                        ["ribbonBits"] = ribbonBits
                    });
                }
            }

            int logicWireCount = 0;
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != activeWorld))
                    continue;
                if (Grid.Objects[cell, (int)ObjectLayer.LogicWire] != null)
                    logicWireCount++;
            }

            return new JObject
            {
                ["worldId"] = activeWorld,
                ["status"] = "operational",
                ["totalWires"] = logicWireCount,
                ["totalCircuits"] = circuitsArr.Count,
                ["activeCircuits"] = activeCircuits,
                ["circuits"] = circuitsArr
            };
        }

        public static JObject BuildLogicCellDetail(int cell)
        {
            if (Game.Instance == null || !Grid.IsValidCell(cell))
                return new JObject { ["status"] = "invalid_cell" };

            var wire = Grid.Objects[cell, (int)ObjectLayer.LogicWire];
            var net = Game.Instance.logicCircuitManager?.GetNetworkForCell(cell);
            Vector2I xy = Grid.CellToXY(cell);

            var res = new JObject
            {
                ["cell"] = cell,
                ["x"] = xy.x,
                ["y"] = xy.y,
                ["hasWire"] = wire != null,
                ["wireType"] = wire?.name
            };

            if (net != null)
            {
                res["circuitId"] = net.id;
                res["outputValue"] = net.OutputValue;
                res["signal"] = net.OutputValue > 0 ? 1 : 0;
                res["state"] = net.OutputValue > 0 ? "GREEN" : "RED";
                res["bitsUsed"] = net.GetBitsUsed();
                var bits = new JArray();
                for (int i = 0; i < 4; i++) bits.Add(net.IsBitActive(i) ? 1 : 0);
                res["ribbonBits"] = bits;
            }

            return res;
        }

        public static JObject BuildPowerCircuitsDetailed(int worldId)
        {
            if (Game.Instance == null || Game.Instance.circuitManager == null)
                return new JObject { ["status"] = "offline", ["reason"] = "CircuitManager not initialized" };

            int activeWorld = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0);
            var cm = Game.Instance.circuitManager;
            var circuitMap = new Dictionary<ushort, JObject>();

            foreach (var battery in Components.Batteries.Items)
            {
                if (battery == null || (worldId >= 0 && !ToolUtil.GameObjectMatchesWorld(battery.gameObject, activeWorld)))
                    continue;
                ushort cid = battery.CircuitID;
                if (cid == ushort.MaxValue) continue;
                EnsurePowerCircuit(circuitMap, cid);
                circuitMap[cid]["storedJoules"] = (double)circuitMap[cid]["storedJoules"] + battery.JoulesAvailable;
                circuitMap[cid]["capacityJoules"] = (double)circuitMap[cid]["capacityJoules"] + battery.Capacity;
            }

            foreach (var gen in Components.Generators.Items)
            {
                if (gen == null || (worldId >= 0 && !ToolUtil.GameObjectMatchesWorld(gen.gameObject, activeWorld)))
                    continue;
                ushort cid = gen.CircuitID;
                if (cid == ushort.MaxValue) continue;
                EnsurePowerCircuit(circuitMap, cid);
                circuitMap[cid]["generatedWatts"] = (double)circuitMap[cid]["generatedWatts"] + gen.WattageRating;
            }

            foreach (var con in Components.EnergyConsumers.Items)
            {
                if (con == null || (worldId >= 0 && !ToolUtil.GameObjectMatchesWorld(con.gameObject, activeWorld)))
                    continue;
                ushort cid = con.CircuitID;
                if (cid == ushort.MaxValue) continue;
                EnsurePowerCircuit(circuitMap, cid);
                circuitMap[cid]["consumedWatts"] = (double)circuitMap[cid]["consumedWatts"] + con.WattsUsed;
                circuitMap[cid]["potentialWatts"] = (double)circuitMap[cid]["potentialWatts"] + con.WattsNeededWhenActive;
            }

            var arr = new JArray();
            foreach (var kv in circuitMap.Values)
            {
                double potential = (double)kv["potentialWatts"];
                double maxSafe = (double)kv["maxSafeWatts"];
                kv["isOverloaded"] = potential > maxSafe;
                arr.Add(kv);
            }

            return new JObject
            {
                ["worldId"] = activeWorld,
                ["status"] = "operational",
                ["circuitsCount"] = arr.Count,
                ["circuits"] = arr
            };
        }

        private static void EnsurePowerCircuit(Dictionary<ushort, JObject> map, ushort cid)
        {
            if (!map.ContainsKey(cid))
            {
                map[cid] = new JObject
                {
                    ["circuitId"] = cid,
                    ["generatedWatts"] = 0.0,
                    ["consumedWatts"] = 0.0,
                    ["potentialWatts"] = 0.0,
                    ["maxSafeWatts"] = 1000.0,
                    ["storedJoules"] = 0.0,
                    ["capacityJoules"] = 0.0
                };
            }
        }

        private static void InspectCellPorts(int cell, ConduitType type, JArray sources, JArray sinks)
        {
            var go = Grid.Objects[cell, (int)ObjectLayer.Building];
            if (go == null) return;

            Vector2I xy = Grid.CellToXY(cell);
            foreach (var c in go.GetComponents<ConduitConsumer>())
            {
                if (c != null && c.ConduitType == type && sinks.Count < 50)
                {
                    sinks.Add(new JObject { ["cell"] = cell, ["x"] = xy.x, ["y"] = xy.y, ["building"] = go.name, ["port"] = "white_input" });
                    break;
                }
            }
            foreach (var d in go.GetComponents<ConduitDispenser>())
            {
                if (d != null && d.ConduitType == type && sources.Count < 50)
                {
                    sources.Add(new JObject { ["cell"] = cell, ["x"] = xy.x, ["y"] = xy.y, ["building"] = go.name, ["port"] = "green_output" });
                    break;
                }
            }
        }

        private static List<string> FlowDirectionsToList(ConduitFlow.FlowDirections dirs)
        {
            var res = new List<string>();
            if ((dirs & ConduitFlow.FlowDirections.Left) != 0) res.Add("Left");
            if ((dirs & ConduitFlow.FlowDirections.Right) != 0) res.Add("Right");
            if ((dirs & ConduitFlow.FlowDirections.Up) != 0) res.Add("Up");
            if ((dirs & ConduitFlow.FlowDirections.Down) != 0) res.Add("Down");
            return res;
        }

        private static string FlowGlyph(ConduitFlow.FlowDirections dirs)
        {
            bool l = (dirs & ConduitFlow.FlowDirections.Left) != 0;
            bool r = (dirs & ConduitFlow.FlowDirections.Right) != 0;
            bool u = (dirs & ConduitFlow.FlowDirections.Up) != 0;
            bool d = (dirs & ConduitFlow.FlowDirections.Down) != 0;

            if (l && r) return "↔";
            if (u && d) return "↕";
            if (r) return "→";
            if (l) return "←";
            if (u) return "↑";
            if (d) return "↓";
            return "·";
        }

        private static JObject FlowVector(ConduitFlow.FlowDirections dirs)
        {
            int dx = 0, dy = 0;
            if ((dirs & ConduitFlow.FlowDirections.Right) != 0) dx += 1;
            if ((dirs & ConduitFlow.FlowDirections.Left) != 0) dx -= 1;
            if ((dirs & ConduitFlow.FlowDirections.Up) != 0) dy += 1;
            if ((dirs & ConduitFlow.FlowDirections.Down) != 0) dy -= 1;
            return new JObject { ["dx"] = dx, ["dy"] = dy };
        }
    }
}
