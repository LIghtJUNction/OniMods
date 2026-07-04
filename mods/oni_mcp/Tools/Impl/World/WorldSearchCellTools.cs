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

    }
}
