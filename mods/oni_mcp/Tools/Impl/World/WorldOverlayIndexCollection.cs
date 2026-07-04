using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static Dictionary<int, OverlaySummary> BuildOverlayIndex(Dictionary<string, int> rect, int worldId, bool includeBuildings, bool includeItems, bool includeDupes)
        {
            var overlays = new Dictionary<int, OverlaySummary>();

            if (includeBuildings)
            {
                var seen = new HashSet<string>();
                foreach (var building in Components.BuildingCompletes.Items)
                {
                    if (building == null || building.GetMyWorldId() != worldId)
                        continue;
                    int cell = Grid.PosToCell(building.gameObject);
                    if (!Grid.IsValidCell(cell))
                        continue;
                    var def = building.Def;
                    string id = def?.PrefabID ?? building.name;
                    if (IsTerrainSupportPrefab(id))
                        continue;
                    var footprint = BuildFootprintObject(worldId, def, building.gameObject, "building", id, ToolUtil.CleanName(def?.Name ?? id), 'B', 'A');
                    if (footprint == null || !FootprintIntersectsRect(footprint, rect))
                        continue;
                    string key = footprint.Key;
                    if (!seen.Add(key))
                        continue;
                    AddBuildingFootprintOverlay(overlays, rect, footprint);
                }

                foreach (var constructable in FindConstructables(worldId))
                {
                    var go = constructable?.gameObject;
                    if (go == null)
                        continue;
                    int cell = Grid.PosToCell(go);
                    if (!Grid.IsValidCell(cell))
                        continue;
                    var building = go.GetComponent<Building>();
                    var kpid = go.GetComponent<KPrefabID>();
                    string id = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;
                    var footprint = BuildFootprintObject(worldId, building?.Def ?? Assets.GetBuildingDef(id), go, "blueprint", id, ToolUtil.CleanName(go.GetProperName()), 'b', '@');
                    if (footprint == null || !FootprintIntersectsRect(footprint, rect))
                        continue;
                    string key = footprint.Key;
                    if (!seen.Add(key))
                        continue;
                    AddBuildingFootprintOverlay(overlays, rect, footprint);
                }
            }

            if (includeDupes)
            {
                foreach (var dupe in Components.LiveMinionIdentities.Items)
                {
                    if (dupe == null || dupe.GetMyWorldId() != worldId)
                        continue;
                    var pos = dupe.transform.GetPosition();
                    int x = Mathf.RoundToInt(pos.x);
                    int y = Mathf.RoundToInt(pos.y);
                    if (!InRect(rect, x, y))
                        continue;
                    overlays[Grid.XYToCell(x, y)] = new OverlaySummary
                    {
                        Key = "duplicant|" + (dupe.GetComponent<KPrefabID>()?.InstanceID.ToString() ?? "unknown"),
                        Kind = "duplicant",
                        Id = dupe.GetComponent<KPrefabID>()?.InstanceID.ToString() ?? "unknown",
                        Name = dupe.GetProperName(),
                        X = x,
                        Y = y,
                        ObjectX = x,
                        ObjectY = y,
                        AnchorX = x,
                        AnchorY = y,
                        Width = 1,
                        Height = 1,
                        FootprintX1 = x,
                        FootprintY1 = y,
                        FootprintX2 = x,
                        FootprintY2 = y,
                        Symbol = 'D',
                        ObjectSymbol = 'D',
                        FootprintSymbol = 'D',
                        AnchorSymbol = 'D',
                        Priority = 80
                    };
                }
            }

            if (includeItems)
            {
                foreach (var pickupable in Components.Pickupables.Items)
                {
                    if (pickupable == null || pickupable.GetMyWorldId() != worldId)
                        continue;
                    var pos = pickupable.transform.GetPosition();
                    int x = Mathf.RoundToInt(pos.x);
                    int y = Mathf.RoundToInt(pos.y);
                    if (!InRect(rect, x, y))
                        continue;
                    int cell = Grid.XYToCell(x, y);
                    if (overlays.ContainsKey(cell))
                        continue;
                    var kpid = pickupable.GetComponent<KPrefabID>();
                    var pe = pickupable.GetComponent<PrimaryElement>();
                    overlays[cell] = new OverlaySummary
                    {
                        Key = "item|" + (kpid != null ? kpid.PrefabTag.Name : pickupable.name) + "|" + x + "|" + y,
                        Kind = "item",
                        Id = kpid != null ? kpid.PrefabTag.Name : pickupable.name,
                        Name = ToolUtil.CleanName(pickupable.GetProperName()),
                        X = x,
                        Y = y,
                        ObjectX = x,
                        ObjectY = y,
                        AnchorX = x,
                        AnchorY = y,
                        Width = 1,
                        Height = 1,
                        FootprintX1 = x,
                        FootprintY1 = y,
                        FootprintX2 = x,
                        FootprintY2 = y,
                        Symbol = 'i',
                        ObjectSymbol = 'i',
                        FootprintSymbol = 'i',
                        AnchorSymbol = 'i',
                        Priority = 10,
                        Extra = pe != null ? Math.Round(SafeFloat(pe.Mass), 2) + "kg" : null
                    };
                }
            }

            return overlays;
        }

        private static OverlaySummary BuildFootprintObject(int worldId, BuildingDef def, GameObject go, string kind, string id, string name, char footprintSymbol, char anchorSymbol)
        {
            int objectCell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(objectCell) || !ToolUtil.CellMatchesWorld(objectCell, worldId))
                return null;

            int objectX = Grid.CellColumn(objectCell);
            int objectY = Grid.CellRow(objectCell);
            int width = Math.Max(1, def?.WidthInCells ?? 1);
            int height = Math.Max(1, def?.HeightInCells ?? 1);
            var building = go.GetComponent<Building>();
            int anchorCell = building != null ? building.GetBottomLeftCell() : Grid.InvalidCell;
            int anchorX;
            int anchorY;
            if (Grid.IsValidCell(anchorCell))
            {
                anchorX = Grid.CellColumn(anchorCell);
                anchorY = Grid.CellRow(anchorCell);
            }
            else
            {
                anchorX = objectX - width / 2;
                anchorY = objectY - height / 2;
                anchorCell = Grid.XYToCell(anchorX, anchorY);
            }
            string key = kind + "|" + id + "|" + anchorX + "|" + anchorY + "|" + worldId;
            string rule = def?.BuildLocationRule.ToString();
            var missingSupport = MissingSupportCells(def, anchorX, anchorY, worldId).ToList();
            bool supportRequired = IsOnFloor(def);
            bool? supported = def == null ? (bool?)null : (supportRequired ? missingSupport.Count == 0 : true);
            return new OverlaySummary
            {
                Key = key,
                Kind = kind,
                Id = id,
                Name = name,
                X = anchorX,
                Y = anchorY,
                ObjectX = objectX,
                ObjectY = objectY,
                ObjectCell = objectCell,
                AnchorX = anchorX,
                AnchorY = anchorY,
                AnchorCell = anchorCell,
                Width = width,
                Height = height,
                FootprintX1 = anchorX,
                FootprintY1 = anchorY,
                FootprintX2 = anchorX + width - 1,
                FootprintY2 = anchorY + height - 1,
                Symbol = anchorSymbol,
                ObjectSymbol = footprintSymbol,
                FootprintSymbol = footprintSymbol,
                AnchorSymbol = anchorSymbol,
                IsAnchor = true,
                IsFootprint = true,
                Priority = kind == "building" ? 60 : 50,
                BuildLocationRule = rule,
                SupportRequired = supportRequired,
                Supported = supported,
                MissingSupportCells = missingSupport,
                ObstructedBy = FootprintObstructions(def, anchorX, anchorY, width, height, worldId),
                Extra = "object=" + objectX + "," + objectY
            };
        }

        private static void AddBuildingFootprintOverlay(Dictionary<int, OverlaySummary> overlays, Dictionary<string, int> rect, OverlaySummary source)
        {
            int added = 0;
            for (int dy = 0; dy < source.Height; dy++)
            {
                for (int dx = 0; dx < source.Width; dx++)
                {
                    int x = source.AnchorX + dx;
                    int y = source.AnchorY + dy;
                    if (!InRect(rect, x, y))
                        continue;
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell))
                        continue;
                    OverlaySummary existing;
                    if (overlays.TryGetValue(cell, out existing) && existing.Key != source.Key && existing.Priority >= source.Priority)
                    {
                        AddObjectObstruction(source, existing.Kind + ":" + existing.Id + "@" + x + "," + y);
                        continue;
                    }

                    overlays[cell] = new OverlaySummary
                    {
                        Key = source.Key,
                        Kind = source.Kind,
                        Id = source.Id,
                        Name = source.Name,
                        X = x,
                        Y = y,
                        ObjectX = source.ObjectX,
                        ObjectY = source.ObjectY,
                        ObjectCell = source.ObjectCell,
                        AnchorX = source.AnchorX,
                        AnchorY = source.AnchorY,
                        AnchorCell = source.AnchorCell,
                        Width = source.Width,
                        Height = source.Height,
                        FootprintX1 = source.FootprintX1,
                        FootprintY1 = source.FootprintY1,
                        FootprintX2 = source.FootprintX2,
                        FootprintY2 = source.FootprintY2,
                        Symbol = x == source.AnchorX && y == source.AnchorY ? source.AnchorSymbol : source.FootprintSymbol,
                        ObjectSymbol = source.ObjectSymbol,
                        FootprintSymbol = source.FootprintSymbol,
                        AnchorSymbol = source.AnchorSymbol,
                        IsAnchor = x == source.AnchorX && y == source.AnchorY,
                        IsFootprint = true,
                        Priority = source.Priority,
                        BuildLocationRule = source.BuildLocationRule,
                        SupportRequired = source.SupportRequired,
                        Supported = source.Supported,
                        MissingSupportCells = source.MissingSupportCells,
                        ObstructedBy = source.ObstructedBy,
                        Extra = source.Extra
                    };
                    added++;
                }
            }

            if (added == 0)
                overlays[HiddenOverlayKey(source.Key)] = source;
        }

        private static IEnumerable<Dictionary<string, object>> MissingSupportCells(BuildingDef def, int anchorX, int anchorY, int worldId)
        {
            if (!IsOnFloor(def))
                yield break;

            int width = Math.Max(1, def.WidthInCells);
            int supportY = anchorY - 1;
            for (int dx = 0; dx < width; dx++)
            {
                int x = anchorX + dx;
                int cell = Grid.XYToCell(x, supportY);
                if (IsSupportCell(cell, worldId))
                    continue;
                yield return new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = supportY
                };
            }
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

        private static bool IsTerrainSupportPrefab(string prefabId)
        {
            if (string.IsNullOrWhiteSpace(prefabId))
                return false;

            return string.Equals(prefabId, "Tile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "MeshTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "GasPermeableMembrane", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "AirflowTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "BunkerTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "GlassTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "InsulationTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "PlasticTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "MetalTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "CarpetTile", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<int, OverlaySummary> BuildViewOverlayIndex(Dictionary<string, int> rect, int worldId, string view)
        {
            var overlays = new Dictionary<int, OverlaySummary>();
            if (view == "power")
            {
                AddLayerOverlays(overlays, rect, worldId, 'w', "wire", ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire);
                AddPowerDevices(overlays, rect, worldId);
                return overlays;
            }
            if (view == "gas_conduits")
            {
                AddLayerOverlays(overlays, rect, worldId, 'g', "gas_conduit", ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit);
                return overlays;
            }
            if (view == "liquid_conduits")
            {
                AddLayerOverlays(overlays, rect, worldId, 'l', "liquid_conduit", ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit);
                return overlays;
            }
            if (view == "solid_conveyor")
            {
                AddLayerOverlays(overlays, rect, worldId, 's', "solid_conveyor", ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit);
                return overlays;
            }
            if (view == "logic")
            {
                AddLayerOverlays(overlays, rect, worldId, 'a', "logic_wire", ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire);
                return overlays;
            }
            return overlays;
        }

        private static Dictionary<string, Dictionary<int, OverlaySummary>> BuildSnapshotOverlayIndexes(Dictionary<string, int> rect, int worldId, List<string> views)
        {
            var indexes = views.Distinct().ToDictionary(view => view, view => new Dictionary<int, OverlaySummary>());
            if (indexes.Count == 0)
                return indexes;

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;

                    AddLayerOverlayIfRequested(indexes, "power", cell, x, y, 'w', "wire", ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire);
                    AddLayerOverlayIfRequested(indexes, "gas_conduits", cell, x, y, 'g', "gas_conduit", ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit);
                    AddLayerOverlayIfRequested(indexes, "liquid_conduits", cell, x, y, 'l', "liquid_conduit", ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit);
                    AddLayerOverlayIfRequested(indexes, "solid_conveyor", cell, x, y, 's', "solid_conveyor", ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit);
                    AddLayerOverlayIfRequested(indexes, "logic", cell, x, y, 'a', "logic_wire", ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire);
                }
            }

            Dictionary<int, OverlaySummary> power;
            if (indexes.TryGetValue("power", out power))
                AddPowerDevices(power, rect, worldId);

            return indexes;
        }

        private static void AddLayerOverlayIfRequested(Dictionary<string, Dictionary<int, OverlaySummary>> indexes, string view, int cell, int x, int y, char symbol, string kind, params ObjectLayer[] layers)
        {
            Dictionary<int, OverlaySummary> overlays;
            if (!indexes.TryGetValue(view, out overlays))
                return;

            foreach (var layer in layers)
            {
                var go = Grid.Objects[cell, (int)layer];
                if (go == null)
                    continue;
                AddOverlay(overlays, cell, go, kind, symbol, x, y);
                return;
            }
        }

        private static void AddLayerOverlays(Dictionary<int, OverlaySummary> overlays, Dictionary<string, int> rect, int worldId, char symbol, string kind, params ObjectLayer[] layers)
        {
            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;

                    foreach (var layer in layers)
                    {
                        var go = Grid.Objects[cell, (int)layer];
                        if (go == null)
                            continue;
                        AddOverlay(overlays, cell, go, kind, symbol, x, y);
                        break;
                    }
                }
            }
        }

        private static void AddPowerDevices(Dictionary<int, OverlaySummary> overlays, Dictionary<string, int> rect, int worldId)
        {
            foreach (var battery in Components.Batteries.Items)
                AddPowerDevice(overlays, rect, worldId, battery != null ? battery.gameObject : null, "battery", battery != null ? battery.CircuitID.ToString() : null);
            foreach (var generator in Components.Generators.Items)
                AddPowerDevice(overlays, rect, worldId, generator != null ? generator.gameObject : null, "generator", generator != null ? generator.CircuitID.ToString() : null);
            foreach (var consumer in Components.EnergyConsumers.Items)
                AddPowerDevice(overlays, rect, worldId, consumer != null ? consumer.gameObject : null, "consumer", consumer != null ? consumer.CircuitID.ToString() : null);
        }

        private static void AddPowerDevice(Dictionary<int, OverlaySummary> overlays, Dictionary<string, int> rect, int worldId, GameObject go, string role, string circuitId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return;
            int cell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            if (!InRect(rect, x, y))
                return;

            AddOverlay(overlays, cell, go, "power_" + role, 'p', x, y, string.IsNullOrWhiteSpace(circuitId) ? null : "circuit=" + circuitId);
        }

        private static void AddOverlay(Dictionary<int, OverlaySummary> overlays, int cell, GameObject go, string kind, char symbol, int x, int y, string extra = null)
        {
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            overlays[cell] = new OverlaySummary
            {
                Key = kind + "|" + (building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name) + "|" + x + "|" + y,
                Kind = kind,
                Id = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                Name = ToolUtil.CleanName(go.GetProperName()),
                X = x,
                Y = y,
                ObjectX = x,
                ObjectY = y,
                ObjectCell = cell,
                AnchorX = x,
                AnchorY = y,
                AnchorCell = cell,
                Width = 1,
                Height = 1,
                FootprintX1 = x,
                FootprintY1 = y,
                FootprintX2 = x,
                FootprintY2 = y,
                Symbol = symbol,
                ObjectSymbol = symbol,
                FootprintSymbol = symbol,
                AnchorSymbol = symbol,
                Priority = 30,
                Extra = extra
            };
        }

    }
}
