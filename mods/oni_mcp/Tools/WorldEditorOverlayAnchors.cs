using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool TryFormatOverlayAnchorToken(
            HashedString mode,
            char symbol,
            int cell,
            int x,
            int y,
            GameObject building,
            GameObject minion,
            GameObject critter,
            string buildingId,
            string buildingName,
            string previousRunKey,
            out string token,
            out string runKey)
        {
            token = null;
            runKey = null;
            if (!ShouldShowOverlayAnchor(mode, cell, building, minion, critter, buildingId))
                return false;

            if (minion != null)
            {
                string name = MapTokenPart(StripLinkTags(minion.GetProperName()));
                runKey = "overlay:" + mode + ":dupe:" + name;
                string kind = GetDupeMapKind(minion);
                token = MergeOverlaySymbol(symbol, previousRunKey == runKey ? kind : kind + "@" + name);
                return true;
            }

            if (critter != null)
            {
                string name = MapTokenPart(StripLinkTags(critter.GetProperName()));
                runKey = "overlay:" + mode + ":critter:" + name;
                token = MergeOverlaySymbol(symbol, previousRunKey == runKey ? "物" : "物@" + name);
                return true;
            }

            if (building == null || string.IsNullOrEmpty(buildingId))
                return false;

            string displayName = !string.IsNullOrEmpty(buildingName) ? StripLinkTags(buildingName) : buildingId;
            string full = MapTokenPart(displayName);
            string shortToken = GetUniqueChar(buildingId, displayName).ToString();
            runKey = "overlay:" + mode + ":building:" + buildingId + ":" + full;
            string anchor = OverlayAnchorPrefix(mode, building, cell)
                + (previousRunKey == runKey ? shortToken : full + "@(" + x + "," + y + ")");
            token = MergeOverlaySymbol(symbol, anchor);
            return true;
        }

        private static string OverlayAnchorPrefix(HashedString mode, GameObject go, int cell)
        {
            return BridgeAnchorPrefix(go) + OverlayPortPrefix(mode, go, cell);
        }

        private static string BridgeAnchorPrefix(GameObject go)
        {
            return IsBridgeBuilding(go) ? "⌒" : string.Empty;
        }

        private static bool IsBridgeBuilding(GameObject go)
        {
            if (go == null)
                return false;
            string id = go.GetComponent<BuildingComplete>()?.name ?? go.name ?? string.Empty;
            return id.IndexOf("Bridge", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string OverlayPortPrefix(HashedString mode, GameObject go, int cell)
        {
            var building = go != null ? go.GetComponent<Building>() : null;
            if (building == null)
                return string.Empty;

            if (mode == OverlayModes.Power.ID)
                return PowerPortPrefix(go, cell);
            if (mode == OverlayModes.LiquidConduits.ID)
                return ConduitPortPrefix(go, building, cell, ConduitType.Liquid);
            if (mode == OverlayModes.GasConduits.ID)
                return ConduitPortPrefix(go, building, cell, ConduitType.Gas);
            if (mode == OverlayModes.SolidConveyor.ID)
                return SolidPortPrefix(go, building, cell);
            if (mode == OverlayModes.Logic.ID)
                return LogicPortPrefix(go, cell);
            return string.Empty;
        }

        private static string ConduitPortPrefix(GameObject go, Building building, int cell, ConduitType type)
        {
            bool input = false;
            foreach (var consumer in go.GetComponents<ConduitConsumer>())
                input |= consumer.ConduitType == type && building.GetUtilityInputCell() == cell;
            bool output = false;
            foreach (var dispenser in go.GetComponents<ConduitDispenser>())
                output |= dispenser.ConduitType == type && building.GetUtilityOutputCell() == cell;
            return PortPrefix(input, output);
        }

        private static string SolidPortPrefix(GameObject go, Building building, int cell)
        {
            bool input = go.GetComponents<SolidConduitConsumer>().Length > 0 && building.GetUtilityInputCell() == cell;
            bool output = go.GetComponents<SolidConduitDispenser>().Length > 0 && building.GetUtilityOutputCell() == cell;
            return PortPrefix(input, output);
        }

        private static string LogicPortPrefix(GameObject go, int cell)
        {
            var ports = go.GetComponent<LogicPorts>();
            if (ports == null)
                return string.Empty;

            bool input = LogicPortsContainCell(ports, ports.inputPortInfo, cell);
            bool output = LogicPortsContainCell(ports, ports.outputPortInfo, cell);
            return PortPrefix(input, output);
        }

        private static bool LogicPortsContainCell(LogicPorts ports, LogicPorts.Port[] info, int cell)
        {
            if (ports == null || info == null)
                return false;
            foreach (var port in info)
                if (ports.GetPortCell(port.id) == cell)
                    return true;
            return false;
        }

        private static string PortPrefix(bool input, bool output)
        {
            if (input && output)
                return "⊗⊙";
            if (output)
                return "⊙";
            return input ? "⊗" : string.Empty;
        }

        private static bool ShouldShowOverlayAnchor(
            HashedString mode,
            int cell,
            GameObject building,
            GameObject minion,
            GameObject critter,
            string buildingId)
        {
            if (mode == OverlayModes.None.ID)
                return false;
            if (minion != null || critter != null)
                return true;
            if (building == null || string.IsNullOrEmpty(buildingId) || !IsPowerAnchorBuilding(buildingId))
                return false;

            if (IsInfrastructureOverlayMode(mode))
                return IsBuildingAnchorCell(building, cell)
                    || OverlayAnchorPrefix(mode, building, cell).Length > 0;

            return IsBuildingAnchorCell(building, cell)
                && (mode == OverlayModes.Temperature.ID
                    || mode == OverlayModes.Oxygen.ID
                    || mode == OverlayModes.Light.ID
                    || mode == OverlayModes.Decor.ID
                    || mode == OverlayModes.Disease.ID
                    || mode == OverlayModes.Radiation.ID
                    || mode == OverlayModes.TileMode.ID
                    || mode == OverlayModes.Crop.ID
                    || mode == OverlayModes.Harvest.ID
                    || mode == OverlayModes.Rooms.ID);
        }

        private static bool IsInfrastructureOverlayMode(HashedString mode)
        {
            return mode == OverlayModes.Power.ID
                || mode == OverlayModes.LiquidConduits.ID
                || mode == OverlayModes.GasConduits.ID
                || mode == OverlayModes.Logic.ID
                || mode == OverlayModes.SolidConveyor.ID;
        }

        private static bool IsBuildingAnchorCell(GameObject go, int cell)
        {
            if (go == null || !Grid.IsValidCell(cell))
                return false;
            var building = go.GetComponent<Building>();
            if (building != null)
            {
                int bottomLeft = building.GetBottomLeftCell();
                if (Grid.IsValidCell(bottomLeft))
                    return bottomLeft == cell;
            }
            int objectCell = Grid.PosToCell(go);
            return Grid.IsValidCell(objectCell) && objectCell == cell;
        }

        private static bool IsNearLayer(int cell, ObjectLayer[] layers)
        {
            if (!Grid.IsValidCell(cell))
                return false;
            if (HasLayer(cell, layers))
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
                    if (Grid.IsValidCell(neighbor) && HasLayer(neighbor, layers))
                        return true;
                }
            }
            return false;
        }

        private static string MergeOverlaySymbol(char symbol, string anchor)
        {
            if (string.IsNullOrEmpty(anchor) || symbol == '.')
                return anchor;
            string prefix = symbol.ToString();
            return anchor.StartsWith(prefix) ? anchor : prefix + anchor;
        }
    }
}
