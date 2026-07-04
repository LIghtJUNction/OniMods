using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static class InfrastructurePortReadTools
    {
        private static readonly ObjectLayer[] PowerLayers = { ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire };
        private static readonly ObjectLayer[] LiquidLayers = { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit };
        private static readonly ObjectLayer[] GasLayers = { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit };
        private static readonly ObjectLayer[] LogicLayers = { ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire };
        private static readonly ObjectLayer[] RailLayers = { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit };

        public static CallToolResult ReadPorts(JObject args)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");

            args = args ?? new JObject();
            string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string kind = NormalizeKind(args["kind"]?.ToString() ?? args["type"]?.ToString());
            string query = args["query"]?.ToString();
            bool includeBlueprints = ToolUtil.GetBool(args, "includeBlueprints", true);
            bool hasRect = HasRectInput(args);
            bool hasPointRadius = IsNearbyAction(action) && HasPointInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : hasPointRadius ? PointRect(args) : null;
            int worldId = hasRect || hasPointRadius || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 120, 500));

            var results = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>();

            foreach (var building in Components.BuildingCompletes.Items)
            {
                AddBuildingPorts(results, seen, building?.gameObject, false, kind, query, rect, worldId, limit);
                if (results.Count >= limit)
                    break;
            }

            if (includeBlueprints && results.Count < limit)
            {
                foreach (var constructable in FindConstructables(worldId))
                {
                    AddBuildingPorts(results, seen, constructable?.gameObject, true, kind, query, rect, worldId, limit);
                    if (results.Count >= limit)
                        break;
                }
            }

            var summary = Summarize(results);
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["worldId"] = worldId,
                ["kind"] = kind,
                ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                ["point"] = hasPointRadius ? CellObject(Grid.XYToCell(ToolUtil.GetInt(args, "x") ?? 0, ToolUtil.GetInt(args, "y") ?? 0)) : null,
                ["rect"] = rect,
                ["returned"] = results.Count,
                ["summary"] = summary,
                ["tokenHint"] = "Use nearby_ports with x/y/radius for local wiring. Each port has cell, role, layer, hasLine, connected, and line.dirs/to/glyph.",
                ["ports"] = results
            }, McpJsonUtil.Settings));
        }

        private static void AddBuildingPorts(
            List<Dictionary<string, object>> results,
            HashSet<string> seen,
            GameObject go,
            bool blueprint,
            string kind,
            string query,
            Dictionary<string, int> rect,
            int worldId,
            int limit)
        {
            if (go == null || results.Count >= limit)
                return;
            if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                return;

            int anchorCell = Grid.PosToCell(go);
            if (rect != null && !CellInRect(anchorCell, rect, worldId))
                return;
            if (!MatchesQuery(go, query))
                return;

            var building = go.GetComponent<Building>();
            var def = building?.Def ?? ResolveBuildingDef(go);
            if (def == null)
                return;

            var ports = BuildPorts(go, building, def, kind).ToList();
            if (ports.Count == 0)
                return;

            string key = go.GetInstanceID() + ":" + anchorCell + ":" + blueprint;
            if (!seen.Add(key))
                return;

            results.Add(new Dictionary<string, object>
            {
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = def.PrefabID,
                ["blueprint"] = blueprint,
                ["anchor"] = CellObject(anchorCell),
                ["判定点"] = CellObject(anchorCell),
                ["ports"] = ports,
                ["summary"] = PortSummary(ports)
            });
        }

        private static IEnumerable<Dictionary<string, object>> BuildPorts(GameObject go, Building building, BuildingDef def, string kind)
        {
            if (Wants(kind, "power"))
            {
                foreach (var port in PowerPorts(go, building, def))
                    yield return port;
            }
            if (Wants(kind, "liquid") || Wants(kind, "gas"))
            {
                foreach (var port in ConduitPorts(go, building, kind))
                    yield return port;
            }
            if (Wants(kind, "logic"))
            {
                foreach (var port in LogicPorts(go))
                    yield return port;
            }
            if (Wants(kind, "rail"))
            {
                foreach (var port in RailPorts(go, building))
                    yield return port;
            }
        }

        private static IEnumerable<Dictionary<string, object>> PowerPorts(GameObject go, Building building, BuildingDef def)
        {
            if (building == null)
                yield break;
            if (def.RequiresPowerInput)
                yield return Port("power", "input", "电力输入", building.GetPowerInputCell(), PowerLayers, PowerStatus(go, "input"));
            if (def.RequiresPowerOutput)
                yield return Port("power", "output", "电力输出", building.GetPowerOutputCell(), PowerLayers, PowerStatus(go, "output"));

            var consumer = go.GetComponent<EnergyConsumer>();
            if (consumer != null && !def.RequiresPowerInput)
                yield return Port("power", "consumer", "耗电端", building.GetPowerInputCell(), PowerLayers, PowerStatus(go, "consumer", consumer));
            var generator = go.GetComponent<Generator>();
            if (generator != null && !def.RequiresPowerOutput)
                yield return Port("power", "generator", "发电端", building.GetPowerOutputCell(), PowerLayers, PowerStatus(go, "generator", null, generator));
        }

        private static IEnumerable<Dictionary<string, object>> ConduitPorts(GameObject go, Building building, string kind)
        {
            if (building == null)
                yield break;
            foreach (var consumer in go.GetComponents<ConduitConsumer>())
            {
                string layer = consumer.ConduitType == ConduitType.Gas ? "gas" : "liquid";
                if (!Wants(kind, layer))
                    continue;
                yield return Port(layer, "input", layer == "gas" ? "气体输入" : "液体输入", building.GetUtilityInputCell(), LayersFor(layer),
                    new Dictionary<string, object> { ["connected"] = consumer.IsConnected });
            }
            foreach (var dispenser in go.GetComponents<ConduitDispenser>())
            {
                string layer = dispenser.ConduitType == ConduitType.Gas ? "gas" : "liquid";
                if (!Wants(kind, layer))
                    continue;
                yield return Port(layer, "output", layer == "gas" ? "气体输出" : "液体输出", building.GetUtilityOutputCell(), LayersFor(layer),
                    new Dictionary<string, object> { ["connected"] = dispenser.IsConnected });
            }
        }

        private static IEnumerable<Dictionary<string, object>> LogicPorts(GameObject go)
        {
            var ports = go.GetComponent<LogicPorts>();
            if (ports == null)
                yield break;
            foreach (var port in ports.inputPortInfo)
            {
                int cell = ports.GetPortCell(port.id);
                yield return Port("logic", "input", "信号输入", cell, LogicLayers, new Dictionary<string, object>
                {
                    ["id"] = port.id.ToString(),
                    ["connected"] = ports.IsPortConnected(port.id),
                    ["value"] = ports.GetInputValue(port.id),
                    ["required"] = port.requiresConnection
                });
            }
            foreach (var port in ports.outputPortInfo)
            {
                int cell = ports.GetPortCell(port.id);
                yield return Port("logic", "output", "信号输出", cell, LogicLayers, new Dictionary<string, object>
                {
                    ["id"] = port.id.ToString(),
                    ["connected"] = ports.IsPortConnected(port.id),
                    ["value"] = ports.GetOutputValue(port.id),
                    ["required"] = port.requiresConnection
                });
            }
        }

        private static IEnumerable<Dictionary<string, object>> RailPorts(GameObject go, Building building)
        {
            if (building == null)
                yield break;
            foreach (var consumer in go.GetComponents<SolidConduitConsumer>())
            {
                yield return Port("rail", "input", "轨道输入", building.GetUtilityInputCell(), RailLayers,
                    new Dictionary<string, object> { ["connected"] = consumer.IsConnected });
            }
            foreach (var dispenser in go.GetComponents<SolidConduitDispenser>())
            {
                yield return Port("rail", "output", "轨道输出", building.GetUtilityOutputCell(), RailLayers,
                    new Dictionary<string, object> { ["connected"] = dispenser.IsConnected });
            }
        }

        private static Dictionary<string, object> Port(string layer, string role, string label, int cell, ObjectLayer[] layers, Dictionary<string, object> extra)
        {
            var result = new Dictionary<string, object>
            {
                ["layer"] = layer,
                ["role"] = role,
                ["label"] = label,
                ["cell"] = CellObject(cell),
                ["hasLine"] = HasLayer(cell, layers),
                ["line"] = LineObject(cell, layers)
            };
            foreach (var item in extra)
                result[item.Key] = item.Value;
            return result;
        }

        private static Dictionary<string, object> PowerStatus(GameObject go, string role, EnergyConsumer consumer = null, Generator generator = null)
        {
            var battery = go.GetComponent<Battery>();
            return new Dictionary<string, object>
            {
                ["connected"] = HasPowerConnection(go),
                ["circuitId"] = PowerCircuit(go),
                ["loadW"] = consumer != null ? (object)Math.Round(ToolUtil.SafeFloat(consumer.WattsNeededWhenActive), 1) : null,
                ["generatorW"] = generator != null ? (object)Math.Round(ToolUtil.SafeFloat(generator.WattageRating), 1) : null,
                ["batteryJ"] = battery != null ? (object)Math.Round(ToolUtil.SafeFloat(battery.JoulesAvailable), 1) : null,
                ["roleHint"] = role
            };
        }

        private static bool HasPowerConnection(GameObject go)
        {
            var consumer = go.GetComponent<EnergyConsumer>();
            if (consumer != null)
                return consumer.CircuitID != ushort.MaxValue;
            var generator = go.GetComponent<Generator>();
            if (generator != null)
                return generator.CircuitID != ushort.MaxValue;
            var battery = go.GetComponent<Battery>();
            return battery != null && battery.CircuitID != ushort.MaxValue;
        }

        private static object PowerCircuit(GameObject go)
        {
            var consumer = go.GetComponent<EnergyConsumer>();
            if (consumer != null)
                return consumer.CircuitID == ushort.MaxValue ? "-1" : consumer.CircuitID.ToString();
            var generator = go.GetComponent<Generator>();
            if (generator != null)
                return generator.CircuitID == ushort.MaxValue ? "-1" : generator.CircuitID.ToString();
            var battery = go.GetComponent<Battery>();
            return battery == null || battery.CircuitID == ushort.MaxValue ? "-1" : battery.CircuitID.ToString();
        }

        private static bool Wants(string kind, string layer)
        {
            return kind == "all" || kind == layer || (kind == "conduit" && (layer == "liquid" || layer == "gas"));
        }

        private static string NormalizeKind(string kind)
        {
            kind = (kind ?? "all").Trim().ToLowerInvariant();
            if (kind == "water" || kind == "liquid_conduit" || kind == "pipe") return "liquid";
            if (kind == "gas_conduit" || kind == "vent") return "gas";
            if (kind == "automation" || kind == "signal") return "logic";
            if (kind == "solid" || kind == "conveyor" || kind == "shipping") return "rail";
            if (kind == "electric" || kind == "wire") return "power";
            return string.IsNullOrEmpty(kind) ? "all" : kind;
        }

        private static ObjectLayer[] LayersFor(string layer)
        {
            return layer == "gas" ? GasLayers : LiquidLayers;
        }

        private static bool HasLayer(int cell, ObjectLayer[] layers)
        {
            return Grid.IsValidCell(cell) && layers.Any(layer => Grid.Objects[cell, (int)layer] != null);
        }

        private static Dictionary<string, object> LineObject(int cell, ObjectLayer[] layers)
        {
            var dirs = new List<string>();
            var to = new List<Dictionary<string, object>>();
            AddLineNeighbor(cell, layers, "U", 0, 1, dirs, to);
            AddLineNeighbor(cell, layers, "D", 0, -1, dirs, to);
            AddLineNeighbor(cell, layers, "L", -1, 0, dirs, to);
            AddLineNeighbor(cell, layers, "R", 1, 0, dirs, to);

            return new Dictionary<string, object>
            {
                ["glyph"] = LineGlyph(dirs),
                ["dirs"] = dirs.Count == 0 ? "." : string.Join("", dirs.ToArray()),
                ["to"] = to,
                ["bridge"] = BridgeId(cell)
            };
        }

        private static void AddLineNeighbor(
            int cell,
            ObjectLayer[] layers,
            string dir,
            int dx,
            int dy,
            List<string> dirs,
            List<Dictionary<string, object>> to)
        {
            int neighbor = Grid.XYToCell(Grid.CellColumn(cell) + dx, Grid.CellRow(cell) + dy);
            if (!HasLayer(neighbor, layers))
                return;
            dirs.Add(dir);
            to.Add(CellObject(neighbor));
        }

        private static string LineGlyph(List<string> dirs)
        {
            bool u = dirs.Contains("U");
            bool d = dirs.Contains("D");
            bool l = dirs.Contains("L");
            bool r = dirs.Contains("R");
            int count = (u ? 1 : 0) + (d ? 1 : 0) + (l ? 1 : 0) + (r ? 1 : 0);
            if (count == 0) return ".";
            if (count == 1) return "*";
            if (count == 4) return "十";
            if (u && d && !l && !r) return "|";
            if (l && r && !u && !d) return "一";
            if (u && r && !d && !l) return "└";
            if (u && l && !d && !r) return "┘";
            if (d && r && !u && !l) return "┌";
            if (d && l && !u && !r) return "┐";
            if (u && l && r && !d) return "┴";
            if (d && l && r && !u) return "┬";
            if (u && d && r && !l) return "├";
            if (u && d && l && !r) return "┤";
            return "?";
        }

        private static string BridgeId(int cell)
        {
            var go = Grid.IsValidCell(cell) ? Grid.Objects[cell, (int)ObjectLayer.Building] : null;
            if (go == null)
                return null;
            string id = go.GetComponent<BuildingComplete>()?.name ?? go.name;
            return id.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) >= 0 ? id : null;
        }

        private static Dictionary<string, object> CellObject(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return new Dictionary<string, object> { ["valid"] = false };
            return new Dictionary<string, object>
            {
                ["x"] = Grid.CellColumn(cell),
                ["y"] = Grid.CellRow(cell),
                ["cell"] = cell,
                ["valid"] = true
            };
        }

        private static BuildingDef ResolveBuildingDef(GameObject go)
        {
            var kpid = go.GetComponent<KPrefabID>();
            string id = kpid?.PrefabTag.Name ?? go.name;
            return string.IsNullOrWhiteSpace(id) ? null : Assets.GetBuildingDef(id);
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || args["x1"] != null || args["y1"] != null || args["x2"] != null || args["y2"] != null;
        }

        private static bool IsNearbyAction(string action)
        {
            return action == "nearby_ports" || action == "nearby" || action == "ports_nearby";
        }

        private static bool HasPointInput(JObject args)
        {
            return ToolUtil.GetInt(args, "x").HasValue && ToolUtil.GetInt(args, "y").HasValue;
        }

        private static Dictionary<string, int> PointRect(JObject args)
        {
            int x = ToolUtil.GetInt(args, "x") ?? 0;
            int y = ToolUtil.GetInt(args, "y") ?? 0;
            int radius = Math.Max(0, Math.Min(ToolUtil.GetInt(args, "radius") ?? 8, 80));
            return new Dictionary<string, int>
            {
                ["x1"] = x - radius,
                ["y1"] = y - radius,
                ["x2"] = x + radius,
                ["y2"] = y + radius
            };
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
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
            var def = go.GetComponent<Building>()?.Def ?? ResolveBuildingDef(go);
            string name = ToolUtil.CleanName(go.GetProperName());
            return Contains(name, q) || Contains(def?.PrefabID, q) || Contains(go.name, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<Constructable> FindConstructables(int worldId)
        {
            Constructable[] constructables = UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None);
            return constructables.Where(item => item != null && ToolUtil.GameObjectMatchesWorld(item.gameObject, worldId));
        }

        private static Dictionary<string, object> Summarize(List<Dictionary<string, object>> items)
        {
            var ports = items.SelectMany(item => item["ports"] as IEnumerable<Dictionary<string, object>> ?? Enumerable.Empty<Dictionary<string, object>>()).ToList();
            return new Dictionary<string, object>
            {
                ["buildings"] = items.Count,
                ["ports"] = ports.Count,
                ["power"] = ports.Count(p => (string)p["layer"] == "power"),
                ["liquid"] = ports.Count(p => (string)p["layer"] == "liquid"),
                ["gas"] = ports.Count(p => (string)p["layer"] == "gas"),
                ["logic"] = ports.Count(p => (string)p["layer"] == "logic"),
                ["rail"] = ports.Count(p => (string)p["layer"] == "rail")
            };
        }

        private static string PortSummary(List<Dictionary<string, object>> ports)
        {
            return string.Join(", ", ports.Select(p => p["layer"] + ":" + p["role"]).ToArray());
        }
    }
}
