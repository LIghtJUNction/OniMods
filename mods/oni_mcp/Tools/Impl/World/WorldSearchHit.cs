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
        private sealed class SearchHit
        {
            public string Kind;
            public string Id;
            public string Name;
            public string PrefabId;
            public string ElementId;
            public int X;
            public int Y;
            public int WorldId;
            public float? MassKg;
            public float? TemperatureC;
            public string State;
            public bool? Solid;
            public bool? Stored;
            public bool? Operational;
            public bool Visible;
            public int Scanned;

            public Dictionary<string, object> ToDictionary(SearchRequest request)
            {
                var result = new Dictionary<string, object>
                {
                    ["kind"] = Kind,
                    ["id"] = Id,
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["elementId"] = ElementId,
                    ["x"] = X,
                    ["y"] = Y,
                    ["worldId"] = WorldId,
                    ["visible"] = Visible
                };
                if (MassKg.HasValue)
                    result["massKg"] = Math.Round(MassKg.Value, 3);
                if (TemperatureC.HasValue)
                    result["temperatureC"] = Math.Round(TemperatureC.Value, 2);
                if (!string.IsNullOrWhiteSpace(State))
                    result["state"] = State;
                if (Solid.HasValue)
                    result["solid"] = Solid.Value;
                if (Stored.HasValue)
                    result["stored"] = Stored.Value;
                if (Operational.HasValue)
                    result["operational"] = Operational.Value;
                if (request.HasNear)
                    result["distance"] = Math.Round(Math.Sqrt(request.DistanceSquared(X, Y)), 2);
                return result;
            }
        }
    }
}
