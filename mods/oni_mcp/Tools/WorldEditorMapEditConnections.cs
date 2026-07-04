using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool IsConnectionEdit(MapEditCell cell)
        {
            return cell != null
                && TryGetConnectionGlyph(cell.ToToken, out char glyph)
                && IsConnectionGlyph(glyph);
        }

        private static CallToolResult ApplyConnectionMapEdit(JObject parentArgs, IEnumerable<MapEditCell> cells)
        {
            var results = new JArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool anyError = false;
            string prefabId = PrefabForConnectionMap(parentArgs["sourcePath"]?.ToString());

            foreach (var cell in cells)
            {
                if (!TryGetConnectionGlyph(cell.ToToken, out char glyph))
                    continue;
                foreach (var dir in DirectionsForConnectionGlyph(glyph))
                {
                    int nx = cell.X + dir.Dx;
                    int ny = cell.Y + dir.Dy;
                    string key = PairKey(cell.X, cell.Y, nx, ny);
                    if (!seen.Add(key))
                        continue;

                    var call = CopyPayload(parentArgs);
                    call["domain"] = "planning";
                    call["action"] = "auto_connect";
                    call["prefabId"] = prefabId;
                    call["material"] = parentArgs["material"] ?? "auto";
                    call["confirm"] = ToolUtil.GetBool(parentArgs, "confirm", false);
                    call["nativePathPlacement"] = true;
                    call["allowCellFallback"] = false;
                    call["points"] = new JArray
                    {
                        new JArray(cell.X, cell.Y),
                        new JArray(nx, ny)
                    };

                    var result = BuildingControlTools.ControlBuilding().Handler(call);
                    anyError = anyError || result.IsError;
                    results.Add(new JObject
                    {
                        ["from"] = new JObject { ["x"] = cell.X, ["y"] = cell.Y },
                        ["to"] = new JObject { ["x"] = nx, ["y"] = ny },
                        ["glyph"] = glyph.ToString(),
                        ["dir"] = dir.Name,
                        ["prefabId"] = prefabId,
                        ["ok"] = !result.IsError,
                        ["error"] = result.IsError ? result.Content?.FirstOrDefault()?.Text ?? string.Empty : string.Empty,
                        ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty
                    });
                }
            }

            return JsonResult(new JObject
            {
                ["ok"] = !anyError,
                ["kind"] = "connection",
                ["prefabId"] = prefabId,
                ["segments"] = results.Count,
                ["results"] = results
            });
        }

        private static bool TryGetConnectionGlyph(string token, out char glyph)
        {
            glyph = '\0';
            token = (token ?? string.Empty).Trim();
            if (token.Length == 0)
                return false;
            glyph = token[0];
            return IsConnectionGlyph(glyph);
        }

        private static IEnumerable<ConnectionGlyphDirection> DirectionsForConnectionGlyph(char glyph)
        {
            switch (glyph)
            {
case '─':
case '一':
                    yield return new ConnectionGlyphDirection("L", -1, 0);
                    yield return new ConnectionGlyphDirection("R", 1, 0);
                    break;
case '│':
case '|':
                    yield return new ConnectionGlyphDirection("U", 0, 1);
                    yield return new ConnectionGlyphDirection("D", 0, -1);
                    break;
case '┼':
case '十':
case '●':
yield return new ConnectionGlyphDirection("U", 0, 1);
yield return new ConnectionGlyphDirection("D", 0, -1);
yield return new ConnectionGlyphDirection("L", -1, 0);
                    yield return new ConnectionGlyphDirection("R", 1, 0);
                    break;
                case '┌':
                    yield return new ConnectionGlyphDirection("D", 0, -1);
                    yield return new ConnectionGlyphDirection("R", 1, 0);
                    break;
                case '┐':
                    yield return new ConnectionGlyphDirection("D", 0, -1);
                    yield return new ConnectionGlyphDirection("L", -1, 0);
                    break;
                case '└':
                    yield return new ConnectionGlyphDirection("U", 0, 1);
                    yield return new ConnectionGlyphDirection("R", 1, 0);
                    break;
                case '┘':
                    yield return new ConnectionGlyphDirection("U", 0, 1);
                    yield return new ConnectionGlyphDirection("L", -1, 0);
                    break;
                case '┬':
                    yield return new ConnectionGlyphDirection("L", -1, 0);
                    yield return new ConnectionGlyphDirection("R", 1, 0);
                    yield return new ConnectionGlyphDirection("D", 0, -1);
                    break;
                case '┴':
                    yield return new ConnectionGlyphDirection("L", -1, 0);
                    yield return new ConnectionGlyphDirection("R", 1, 0);
                    yield return new ConnectionGlyphDirection("U", 0, 1);
                    break;
                case '├':
                    yield return new ConnectionGlyphDirection("U", 0, 1);
                    yield return new ConnectionGlyphDirection("D", 0, -1);
                    yield return new ConnectionGlyphDirection("R", 1, 0);
                    break;
                case '┤':
                    yield return new ConnectionGlyphDirection("U", 0, 1);
                    yield return new ConnectionGlyphDirection("D", 0, -1);
                    yield return new ConnectionGlyphDirection("L", -1, 0);
                    break;
                case '←':
                    yield return new ConnectionGlyphDirection("L", -1, 0);
                    break;
                case '→':
                    yield return new ConnectionGlyphDirection("R", 1, 0);
                    break;
                case '↑':
                    yield return new ConnectionGlyphDirection("U", 0, 1);
                    break;
                case '↓':
                    yield return new ConnectionGlyphDirection("D", 0, -1);
                    break;
            }
        }

        private static string PrefabForConnectionMap(string sourcePath)
        {
            sourcePath = (sourcePath ?? string.Empty).ToLowerInvariant();
            if (sourcePath.Contains("liquid_conduits"))
                return "LiquidConduit";
            if (sourcePath.Contains("gas_conduits"))
                return "GasConduit";
            if (sourcePath.Contains("logic"))
                return "LogicWire";
            if (sourcePath.Contains("solid_conveyor"))
                return "SolidConduit";
            return "Wire";
        }

        private static string PairKey(int x1, int y1, int x2, int y2)
        {
            if (x2 < x1 || (x2 == x1 && y2 < y1))
            {
                int tx = x1;
                int ty = y1;
                x1 = x2;
                y1 = y2;
                x2 = tx;
                y2 = ty;
            }
            return x1 + "," + y1 + "-" + x2 + "," + y2;
        }

        private sealed class ConnectionGlyphDirection
        {
            public readonly string Name;
            public readonly int Dx;
            public readonly int Dy;

            public ConnectionGlyphDirection(string name, int dx, int dy)
            {
                Name = name;
                Dx = dx;
                Dy = dy;
            }
        }
    }
}
