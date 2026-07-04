using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private const int MaxConnectionDetailRows = 80;
        private const int MaxEndpointDetailRows = 40;

        private static void AppendConnectionDetails(
            StringBuilder sb,
            HashedString mode,
            int xMin,
            int xMax,
            int yMin,
            int yMax)
        {
            if (!TryGetConnectionProfile(mode, out string title, out string layerName, out ObjectLayer[] layers))
                return;

            var connections = new List<string>();
            var endpoints = new List<string>();
            var bridges = new List<string>();
            int connectionTotal = 0;
            int endpointTotal = 0;
            int bridgeTotal = 0;

            for (int y = yMax; y >= yMin; y--)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell))
                        continue;

                    if (HasLayer(cell, layers))
                    {
                        connectionTotal++;
                        if (connections.Count < MaxConnectionDetailRows)
                            connections.Add(ConnectionDetailLine(mode, cell, layerName, layers));
                    }

                    var endpoint = EndpointDetailLine(mode, cell);
                if (!string.IsNullOrEmpty(endpoint))
                {
                    endpointTotal++;
                    if (endpoints.Count < MaxEndpointDetailRows)
                        endpoints.Add(endpoint);
                }

                var bridge = BridgeDetailLine(mode, cell);
                if (!string.IsNullOrEmpty(bridge))
                {
                    bridgeTotal++;
                    if (bridges.Count < MaxEndpointDetailRows)
                        bridges.Add(bridge);
                }
            }
        }

            if (connectionTotal == 0 && endpointTotal == 0 && bridgeTotal == 0)
                return;

            sb.AppendLine("## Connection Details (" + title + ")");
            sb.AppendLine("- format: `(x,y): glyph=符号 dirs=UDLR links=U:(x,y),R:(x,y) open=U:(x,y) to=(neighbor...) extra`");
            sb.AppendLine("- legend: U=up D=down L=left R=right; `⌒`=bridge, `⊗`=input port, `⊙`=output port.");
            sb.AppendLine("- read: `dirs=.` means an endpoint or disconnected segment; compare `links`/`to` to verify actual neighbor cells instead of counting columns.");
            sb.AppendLine("- bridge: `bridgeRoute=from:(x,y) via:⌒ to:(x,y)` jumps through the building; do not infer direct wire/pipe connection across bridge footprint.");
            sb.AppendLine("- inspect: open `/active/map/cell_X_Y.md` for exact ports, building role, bridge, dropped items, and quick orders.");
            foreach (string line in connections.Distinct())
                sb.AppendLine(line);
            if (connectionTotal > connections.Count)
                sb.AppendLine("- ... " + (connectionTotal - connections.Count) + " more connection cells; use `/active/map/cell_X_Y.md` for exact detail.");

            if (bridges.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Bridges (" + title + ")");
                sb.AppendLine("- route: `from` is input side, `to` is output side, `via:⌒` means jump through bridge building.");
                foreach (string line in bridges.Distinct())
                    sb.AppendLine(line);
                if (bridgeTotal > bridges.Count)
                    sb.AppendLine("- ... " + (bridgeTotal - bridges.Count) + " more bridges in viewport.");
            }

            if (endpoints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Endpoint Anchors (" + title + ")");
                sb.AppendLine("- roles: power `in/out/consumer/generator/battery`; pipe/rail `in/out`; logic `logicIn/logicOut`.");
                foreach (string line in endpoints.Distinct())
                    sb.AppendLine(line);
                if (endpointTotal > endpoints.Count)
                    sb.AppendLine("- ... " + (endpointTotal - endpoints.Count) + " more endpoints in viewport.");
            }
            sb.AppendLine();
        }

        private static bool TryGetConnectionProfile(
            HashedString mode,
            out string title,
            out string layerName,
            out ObjectLayer[] layers)
        {
            if (mode == OverlayModes.Power.ID)
            {
                title = "Power";
                layerName = "wire";
                layers = PowerLayers;
                return true;
            }
            if (mode == OverlayModes.LiquidConduits.ID)
            {
                title = "Liquid Pipes";
                layerName = "liquid";
                layers = LiquidLayers;
                return true;
            }
            if (mode == OverlayModes.GasConduits.ID)
            {
                title = "Gas Pipes";
                layerName = "gas";
                layers = GasLayers;
                return true;
            }
            if (mode == OverlayModes.Logic.ID)
            {
                title = "Logic";
                layerName = "logic";
                layers = LogicLayers;
                return true;
            }
            if (mode == OverlayModes.SolidConveyor.ID)
            {
                title = "Conveyor";
                layerName = "rail";
                layers = ConveyorLayers;
                return true;
            }

            title = null;
            layerName = null;
            layers = null;
            return false;
        }

        private static string ConnectionDetailLine(
            HashedString mode,
            int cell,
            string layerName,
            ObjectLayer[] layers)
        {
            var dirs = ConnectionDirections(cell, layers, mode == OverlayModes.Power.ID);
            char glyph = mode == OverlayModes.Power.ID
                ? ResolvePowerConnectionSymbol(cell)
                : ResolveUtilityConnectionSymbol(cell, layers);
            string extra = mode == OverlayModes.Power.ID ? PowerCircuitText(cell) : string.Empty;
            string bridge = BridgeText(cell, mode);
            if (!string.IsNullOrEmpty(bridge))
                extra = AppendExtra(extra, bridge);

            return "- " + CellCoord(cell)
                + ": layer=" + layerName
                + " glyph=" + glyph
                + " dirs=" + (dirs.Count == 0 ? "." : string.Join("", dirs.Select(d => d.Dir).ToArray()))
                + " links=" + ConnectionLinkText(dirs)
                 + " open=" + OpenAdjacentConnectionText(cell, layers, dirs)
            + " to=" + (dirs.Count == 0 ? "." : string.Join(",", dirs.Select(d => CellCoord(d.Cell)).ToArray()))
                + (string.IsNullOrEmpty(extra) ? string.Empty : " " + extra);
        }

private static string ConnectionLinkText(List<ConnectionNeighbor> dirs)
{
if (dirs.Count == 0)
return ".";
return string.Join(",", dirs.Select(d => d.Dir + ":" + CellCoord(d.Cell)).ToArray());
}

private static string OpenAdjacentConnectionText(int cell, ObjectLayer[] layers, List<ConnectionNeighbor> dirs)
{
var open = new List<ConnectionNeighbor>();
AddOpenAdjacentIf(open, cell, "U", 0, 1, layers, dirs);
AddOpenAdjacentIf(open, cell, "D", 0, -1, layers, dirs);
AddOpenAdjacentIf(open, cell, "L", -1, 0, layers, dirs);
AddOpenAdjacentIf(open, cell, "R", 1, 0, layers, dirs);
return open.Count == 0 ? "." : ConnectionLinkText(open);
}

private static void AddOpenAdjacentIf(
List<ConnectionNeighbor> open,
int cell,
string dir,
int dx,
int dy,
ObjectLayer[] layers,
List<ConnectionNeighbor> linked)
{
if (linked.Any(d => d.Dir == dir))
return;
int neighbor = NeighborCell(cell, dx, dy);
if (Grid.IsValidCell(neighbor) && HasLayer(neighbor, layers))
open.Add(new ConnectionNeighbor(dir, neighbor));
}

private static List<ConnectionNeighbor> ConnectionDirections(
int cell,
            ObjectLayer[] layers,
            bool power)
        {
            var result = new List<ConnectionNeighbor>();
            if (TryGetUtilityConnections(cell, layers, out UtilityConnections connections))
            {
                AddConnectionIf(result, cell, "U", 0, 1, (connections & UtilityConnections.Up) != 0);
                AddConnectionIf(result, cell, "D", 0, -1, (connections & UtilityConnections.Down) != 0);
                AddConnectionIf(result, cell, "L", -1, 0, (connections & UtilityConnections.Left) != 0);
                AddConnectionIf(result, cell, "R", 1, 0, (connections & UtilityConnections.Right) != 0);
                return result;
            }

            ushort circuitId = power ? GetPowerCircuitId(cell) : ushort.MaxValue;
            AddFallbackConnection(result, cell, "U", 0, 1, layers, power, circuitId);
            AddFallbackConnection(result, cell, "D", 0, -1, layers, power, circuitId);
            AddFallbackConnection(result, cell, "L", -1, 0, layers, power, circuitId);
            AddFallbackConnection(result, cell, "R", 1, 0, layers, power, circuitId);
            return result;
        }

        private static void AddConnectionIf(
            List<ConnectionNeighbor> result,
            int cell,
            string dir,
            int dx,
            int dy,
            bool connected)
        {
            if (!connected)
                return;
            int neighbor = NeighborCell(cell, dx, dy);
            if (Grid.IsValidCell(neighbor))
                result.Add(new ConnectionNeighbor(dir, neighbor));
        }

        private static void AddFallbackConnection(
            List<ConnectionNeighbor> result,
            int cell,
            string dir,
            int dx,
            int dy,
            ObjectLayer[] layers,
            bool power,
            ushort circuitId)
        {
            int neighbor = NeighborCell(cell, dx, dy);
            if (!Grid.IsValidCell(neighbor) || !HasLayer(neighbor, layers))
                return;
            if (power && circuitId != ushort.MaxValue && GetPowerCircuitId(neighbor) != circuitId)
                return;
            result.Add(new ConnectionNeighbor(dir, neighbor));
        }

        private static int NeighborCell(int cell, int dx, int dy)
        {
            int x = Grid.CellColumn(cell) + dx;
            int y = Grid.CellRow(cell) + dy;
            return Grid.XYToCell(x, y);
        }

        private static string BridgeDetailLine(HashedString mode, int cell)
        {
            string bridge = BridgeText(cell, mode);
            if (string.IsNullOrEmpty(bridge))
                return null;
            var go = Grid.Objects[cell, (int)ObjectLayer.Building];
            var building = go != null ? go.GetComponent<Building>() : null;
            return EndpointPrefix(cell, building) + bridge;
        }

        private static string EndpointDetailLine(HashedString mode, int cell)
        {
            var go = Grid.Objects[cell, (int)ObjectLayer.Building];
            var building = go != null ? go.GetComponent<Building>() : null;
            if (building == null || building.Def == null)
                return null;

            if (mode == OverlayModes.Power.ID)
                return PowerEndpointLine(cell, building);
            if (mode == OverlayModes.LiquidConduits.ID || mode == OverlayModes.GasConduits.ID)
                return ConduitEndpointLine(mode, cell, go, building);
            if (mode == OverlayModes.Logic.ID)
                return LogicEndpointLine(cell, go);
            if (mode == OverlayModes.SolidConveyor.ID)
                return SolidEndpointLine(cell, go, building);
            return null;
        }

        private static string PowerEndpointLine(int cell, Building building)
        {
            var parts = new List<string>();
            if (building.Def.RequiresPowerInput)
                parts.Add(PortCellText("in", true, building.GetPowerInputCell()));
            if (building.Def.RequiresPowerOutput)
                parts.Add(PortCellText("out", false, building.GetPowerOutputCell()));
            var consumer = building.GetComponent<EnergyConsumer>();
            if (consumer != null)
            {
                parts.Add("role=consumer");
                parts.Add("load=" + consumer.WattsNeededWhenActive.ToString("F0") + "W");
            }
            var generator = building.GetComponent<Generator>();
            if (generator != null)
            {
                parts.Add("role=generator");
                parts.Add("gen=" + generator.WattageRating.ToString("F0") + "W");
            }
            var battery = building.GetComponent<Battery>();
            if (battery != null)
            {
                parts.Add("role=battery");
                parts.Add("battery=" + battery.JoulesAvailable.ToString("F0") + "/" + battery.Capacity.ToString("F0") + "J");
            }
            return parts.Count == 0 ? null : EndpointPrefix(cell, building) + string.Join(" ", parts.ToArray());
        }

        private static string ConduitEndpointLine(HashedString mode, int cell, GameObject go, Building building)
        {
            ConduitType type = mode == OverlayModes.GasConduits.ID ? ConduitType.Gas : ConduitType.Liquid;
            var parts = new List<string>();
            if (go.GetComponents<ConduitConsumer>().Any(c => c.ConduitType == type))
                parts.Add(PortCellText("in", true, building.GetUtilityInputCell()));
            if (go.GetComponents<ConduitDispenser>().Any(d => d.ConduitType == type))
                parts.Add(PortCellText("out", false, building.GetUtilityOutputCell()));
            return parts.Count == 0 ? null : EndpointPrefix(cell, building) + string.Join(" ", parts.ToArray());
        }

        private static string LogicEndpointLine(int cell, GameObject go)
        {
            var ports = go.GetComponent<LogicPorts>();
            if (ports == null)
                return null;
            int inputs = ports.inputPortInfo?.Length ?? 0;
            int outputs = ports.outputPortInfo?.Length ?? 0;
            if (inputs == 0 && outputs == 0)
                return null;
            var building = go.GetComponent<Building>();
            return EndpointPrefix(cell, building)
                + LogicPortText("logicIn", true, ports, ports.inputPortInfo)
                + " "
                + LogicPortText("logicOut", false, ports, ports.outputPortInfo);
        }

        private static string SolidEndpointLine(int cell, GameObject go, Building building)
        {
            var parts = new List<string>();
            if (go.GetComponents<SolidConduitConsumer>().Any())
                parts.Add(PortCellText("in", true, building.GetUtilityInputCell()));
            if (go.GetComponents<SolidConduitDispenser>().Any())
                parts.Add(PortCellText("out", false, building.GetUtilityOutputCell()));
            return parts.Count == 0 ? null : EndpointPrefix(cell, building) + string.Join(" ", parts.ToArray());
        }

        private static string EndpointPrefix(int cell, Building building)
        {
            string name = building != null ? StripLinkTags(building.gameObject.GetProperName()) : "Building";
            return "- " + MapTokenPart(name) + "@" + CellCoord(cell) + ": ";
        }

        private static string PortCellText(string label, bool input, int cell)
        {
            return label + "=" + (input ? "⊗" : "⊙") + CellCoord(cell);
        }

        private static string LogicPortText(string label, bool input, LogicPorts ports, LogicPorts.Port[] info)
        {
            int count = info?.Length ?? 0;
            if (count == 0)
                return label + "=.";
            int cell = FirstLogicPortCell(ports, info);
            return label + "=" + (input ? "⊗" : "⊙") + CellCoord(cell)
                + (count > 1 ? "x" + count : string.Empty);
        }

        private static string PowerCircuitText(int cell)
        {
            ushort id = GetPowerCircuitId(cell);
            return id == ushort.MaxValue ? string.Empty : "circuit=" + id;
        }

        private static string BridgeText(int cell)
        {
            return BridgeText(cell, default(HashedString));
        }

        private static string BridgeText(int cell, HashedString mode)
        {
            var go = Grid.Objects[cell, (int)ObjectLayer.Building];
            if (go == null)
                return string.Empty;
            string id = go.GetComponent<BuildingComplete>()?.name ?? go.name;
            if (id.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) < 0)
                return string.Empty;

            var building = go.GetComponent<Building>();
            string ports = BridgePortText(go, building, mode);
            return string.IsNullOrEmpty(ports)
                ? "bridge=" + id
                : "bridge=" + id + " bridgePorts=" + ports + " bridgeRoute=" + ports;
        }

        private static string BridgePortText(GameObject go, Building building, HashedString mode)
        {
            if (mode == OverlayModes.Logic.ID && go != null)
            {
                var ports = go.GetComponent<LogicPorts>();
                if (ports != null)
                {
                    int logicInput = FirstLogicPortCell(ports, ports.inputPortInfo);
                    int logicOutput = FirstLogicPortCell(ports, ports.outputPortInfo);
if (Grid.IsValidCell(logicInput) && Grid.IsValidCell(logicOutput))
return BridgePortRoute(logicInput, logicOutput);
                }
            }

            if (building == null)
                return string.Empty;

            int input = Grid.InvalidCell;
            int output = Grid.InvalidCell;
            if (mode == OverlayModes.Power.ID)
            {
                input = building.GetPowerInputCell();
                output = building.GetPowerOutputCell();
            }
            else
            {
                input = building.GetUtilityInputCell();
                output = building.GetUtilityOutputCell();
            }

            if (!Grid.IsValidCell(input) || !Grid.IsValidCell(output))
                return string.Empty;
return BridgePortRoute(input, output);
}

private static string BridgePortRoute(int input, int output)
{
        return "from:" + CellCoord(input) + " via:⌒ to:" + CellCoord(output);
}

private static int FirstLogicPortCell(LogicPorts ports, LogicPorts.Port[] info)
        {
            if (ports == null || info == null)
                return Grid.InvalidCell;
            foreach (var port in info)
            {
                int cell = ports.GetPortCell(port.id);
                if (Grid.IsValidCell(cell))
                    return cell;
            }
            return Grid.InvalidCell;
        }

        private static string AppendExtra(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
                return right ?? string.Empty;
            if (string.IsNullOrEmpty(right))
                return left;
            return left + " " + right;
        }

        private static string CellCoord(int cell)
        {
            return Grid.IsValidCell(cell)
                ? "(" + Grid.CellColumn(cell) + "," + Grid.CellRow(cell) + ")"
                : "(invalid)";
        }

        private sealed class ConnectionNeighbor
        {
            public readonly string Dir;
            public readonly int Cell;

            public ConnectionNeighbor(string dir, int cell)
            {
                Dir = dir;
                Cell = cell;
            }
        }
    }
}
