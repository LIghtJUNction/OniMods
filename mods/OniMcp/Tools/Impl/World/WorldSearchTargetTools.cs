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
    public static partial class WorldSearchTools
    {
        private static IEnumerable<SearchHit> SearchCells(SearchRequest request)
        {
            var hits = new List<SearchHit>();
            int scanned = 0;
            foreach (int cell in request.Cells())
            {
                if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                    continue;
                if (request.VisibleOnly && !Grid.IsVisible(cell))
                    continue;
                scanned++;

                var element = Grid.Element[cell];
                string elementId = element?.id.ToString() ?? "Unknown";
                string elementName = ToolUtil.CleanName(element?.name ?? elementId);
                string state = ToolUtil.GetElementState(element);
                bool solid = Grid.Solid[cell];
                float mass = ToolUtil.SafeFloat(Grid.Mass[cell]);
                float tempC = ToolUtil.SafeFloat(Grid.Temperature[cell]) - 273.15f;

                if (!request.MatchesQuery(elementId, elementName, state))
                    continue;
                if (!request.MatchesState(state))
                    continue;
                if (request.Solid.HasValue && request.Solid.Value != solid)
                    continue;
                if (request.MinMassKg.HasValue && mass < request.MinMassKg.Value)
                    continue;
                if (request.MaxMassKg.HasValue && mass > request.MaxMassKg.Value)
                    continue;
                if (request.MinTempC.HasValue && tempC < request.MinTempC.Value)
                    continue;
                if (request.MaxTempC.HasValue && tempC > request.MaxTempC.Value)
                    continue;

                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                hits.Add(new SearchHit
                {
                    Kind = "cell",
                    Id = cell.ToString(),
                    Name = elementName,
                    PrefabId = elementId,
                    ElementId = elementId,
                    X = x,
                    Y = y,
                    WorldId = Grid.WorldIdx[cell],
                    MassKg = mass,
                    TemperatureC = tempC,
                    State = state,
                    Solid = solid,
                    Visible = Grid.IsVisible(cell),
                    Scanned = scanned
                });
            }
            return hits;
        }

        private static IEnumerable<SearchHit> SearchBuildings(SearchRequest request)
        {
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.gameObject == null)
                    continue;
                int cell = Grid.PosToCell(building.gameObject);
                if (!request.MatchesCell(cell))
                    continue;
                var def = building.Def;
                var kpid = building.GetComponent<KPrefabID>();
                string prefabId = def?.PrefabID ?? kpid?.PrefabTag.Name ?? building.name;
                string name = ToolUtil.CleanName(building.GetProperName());
                if (!request.MatchesQuery(name, prefabId, building.name))
                    continue;

                int x;
                int y;
                Grid.CellToXY(cell, out x, out y);
                yield return new SearchHit
                {
                    Kind = "building",
                    Id = (kpid?.InstanceID ?? building.gameObject.GetInstanceID()).ToString(),
                    Name = name,
                    PrefabId = prefabId,
                    X = x,
                    Y = y,
                    WorldId = Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : building.GetMyWorldId(),
                    Operational = building.GetComponent<Operational>()?.IsOperational,
                    Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell)
                };
            }
        }

        private static IEnumerable<SearchHit> SearchItems(SearchRequest request)
        {
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                int cell = pickupable.cachedCell;
                if (!request.MatchesCell(cell, pickupable.GetMyWorldId()))
                    continue;
                var kpid = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                string prefabId = kpid?.PrefabTag.Name ?? pickupable.name;
                string name = ToolUtil.CleanName(pickupable.GetProperName());
                string elementId = primary == null ? null : primary.ElementID.ToString();
                if (!request.MatchesQuery(name, prefabId, elementId, pickupable.name))
                    continue;

                int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
                int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
                yield return new SearchHit
                {
                    Kind = "item",
                    Id = (kpid?.InstanceID ?? pickupable.gameObject.GetInstanceID()).ToString(),
                    Name = name,
                    PrefabId = prefabId,
                    ElementId = elementId,
                    X = x,
                    Y = y,
                    WorldId = Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : pickupable.GetMyWorldId(),
                    MassKg = primary == null ? (float?)null : ToolUtil.SafeFloat(primary.Mass),
                    TemperatureC = primary == null ? (float?)null : ToolUtil.SafeFloat(primary.Temperature) - 273.15f,
                    Stored = pickupable.storage != null || kpid != null && kpid.HasTag(GameTags.Stored),
                    Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell)
                };
            }
        }

        private static IEnumerable<SearchHit> SearchDupes(SearchRequest request)
        {
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.gameObject == null)
                    continue;
                int cell = Grid.PosToCell(dupe.gameObject);
                if (!request.MatchesCell(cell, dupe.GetMyWorldId()))
                    continue;
                string name = ToolUtil.CleanName(dupe.GetProperName());
                if (!request.MatchesQuery(name, "dupe", "duplicant"))
                    continue;
                var kpid = dupe.GetComponent<KPrefabID>();
                int x;
                int y;
                Grid.CellToXY(cell, out x, out y);
                yield return new SearchHit
                {
                    Kind = "dupe",
                    Id = (kpid?.InstanceID ?? dupe.gameObject.GetInstanceID()).ToString(),
                    Name = name,
                    PrefabId = "Minion",
                    X = x,
                    Y = y,
                    WorldId = dupe.GetMyWorldId(),
                    Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell)
                };
            }
        }
    }
}
