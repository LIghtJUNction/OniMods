using System;
using System.Collections.Generic;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static char ResolvePowerConnectionSymbol(int cell)
        {
            if (!HasLayer(cell, PowerLayers))
                return '.';

            if (TryGetUtilityConnectionGlyph(cell, PowerLayers, out char glyph))
                return glyph;

            ushort circuitId = GetPowerCircuitId(cell);
            if (circuitId != ushort.MaxValue)
                return GetConnectionGlyph(neighbor => PowerCellsShareCircuit(circuitId, neighbor), cell);

            return GetConnectionGlyph(neighbor => Grid.IsValidCell(neighbor) && HasLayer(neighbor, PowerLayers), cell);
        }

        private static char ResolveUtilityConnectionSymbol(int cell, ObjectLayer[] layers)
        {
            if (!HasLayer(cell, layers))
                return '.';

            if (TryGetUtilityConnectionGlyph(cell, layers, out char glyph))
                return glyph;

            return GetConnectionGlyph(neighbor => Grid.IsValidCell(neighbor) && HasLayer(neighbor, layers), cell);
        }

        private static bool TryGetUtilityConnectionGlyph(int cell, ObjectLayer[] layers, out char glyph)
        {
            glyph = '.';
            if (!TryGetUtilityConnections(cell, layers, out UtilityConnections connections))
                return false;

            glyph = GetConnectionGlyph(
                (connections & UtilityConnections.Up) != 0,
                (connections & UtilityConnections.Down) != 0,
                (connections & UtilityConnections.Left) != 0,
                (connections & UtilityConnections.Right) != 0);
            return true;
        }

        private static bool TryGetUtilityConnections(int cell, ObjectLayer[] layers, out UtilityConnections connections)
        {
            connections = (UtilityConnections)0;
            foreach (var layer in layers)
            {
                var go = Grid.Objects[cell, (int)layer];
                if (go == null)
                    continue;

                var provider = UtilityNetworkProvider(go);
                if (provider == null)
                    continue;

                var manager = provider.GetNetworkManager();
                if (manager == null)
                    continue;

                connections = manager.GetConnections(cell, is_physical_building: false);
                return true;
            }

            return false;
        }

        private static IHaveUtilityNetworkMgr UtilityNetworkProvider(GameObject go)
        {
            var provider = go.GetComponent<IHaveUtilityNetworkMgr>();
            if (provider != null)
                return provider;

            var building = go.GetComponent<Building>();
            if (building != null && building.Def != null && building.Def.BuildingComplete != null)
                return building.Def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>();

            return null;
        }

        private static bool PowerCellsShareCircuit(ushort circuitId, int neighbor)
        {
            return Grid.IsValidCell(neighbor)
                && HasLayer(neighbor, PowerLayers)
                && GetPowerCircuitId(neighbor) == circuitId;
        }

        private static ushort GetPowerCircuitId(int cell)
        {
            try
            {
                if (!Grid.IsValidCell(cell) || Game.Instance == null || Game.Instance.circuitManager == null)
                    return ushort.MaxValue;

                return Game.Instance.circuitManager.GetCircuitID(cell);
            }
            catch
            {
                return ushort.MaxValue;
            }
        }

        private static readonly HashSet<string> PowerAnchorExcludedBuildings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Tile", "MeshTile", "GasPermeableMembrane", "Ladder", "FirePole",
            "BunkerTile", "CarpetTile", "PlasticTile", "MetalTile", "WindowTile",
            "Wire", "WireRefined", "HighWattageWire",
            "LiquidConduit", "GasConduit", "SolidConduit", "LogicWire"
        };

        private static bool TryFormatPowerAnchorToken(
            HashedString mode,
            int x,
            int y,
            GameObject building,
            GameObject minion,
            string buildingId,
            string buildingName,
            string previousRunKey,
            out string token,
            out string runKey)
        {
            token = null;
            runKey = null;
            if (mode != OverlayModes.Power.ID)
                return false;

            int cell = Grid.XYToCell(x, y);
            if (!IsNearPowerWire(cell))
                return false;

            if (minion != null)
            {
                string name = MapTokenPart(StripLinkTags(minion.GetProperName()));
                runKey = "power-dupe:" + name;
                token = previousRunKey == runKey ? "人" : "人@" + name;
                return true;
            }

            if (string.IsNullOrEmpty(buildingId) || !IsPowerAnchorBuilding(buildingId))
                return false;

string displayName = !string.IsNullOrEmpty(buildingName) ? StripLinkTags(buildingName) : buildingId;
string safeName = MapTokenPart(displayName);
runKey = "power-building:" + buildingId + ":" + safeName;
char glyph = GetUniqueChar(buildingId, displayName);
string portPrefix = PowerPortPrefix(building, cell);
if (!IsBuildingAnchorCell(building, cell) && portPrefix.Length == 0)
return false;
token = portPrefix + (previousRunKey == runKey ? glyph.ToString() : safeName + "@(" + x + "," + y + ")");
return true;
}

private static string PowerPortPrefix(GameObject go, int cell)
{
var building = go != null ? go.GetComponent<Building>() : null;
if (building == null || building.Def == null)
return string.Empty;

bool input = building.Def.RequiresPowerInput && building.GetPowerInputCell() == cell;
bool output = building.Def.RequiresPowerOutput && building.GetPowerOutputCell() == cell;
return BridgeAnchorPrefix(go) + PortPrefix(input, output);
}

private static bool IsNearPowerWire(int cell)
{
            if (!Grid.IsValidCell(cell))
                return false;
            if (HasLayer(cell, PowerLayers))
                return true;

            int x = Grid.CellToXY(cell).x;
            int y = Grid.CellToXY(cell).y;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    int neighbor = Grid.XYToCell(x + dx, y + dy);
                    if (Grid.IsValidCell(neighbor) && HasLayer(neighbor, PowerLayers))
                        return true;
                }
            }

            return false;
        }

        private static bool IsPowerAnchorBuilding(string buildingId)
        {
            return !PowerAnchorExcludedBuildings.Contains(buildingId);
        }
    }
}
