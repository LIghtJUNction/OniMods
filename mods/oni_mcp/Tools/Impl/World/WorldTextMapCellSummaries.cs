using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static CellSummary GetCellSummary(int cell, int x, int y, int worldId, bool visibleOnly, Dictionary<int, OverlaySummary> overlays, bool overlayView, string view)
        {
            var summary = new CellSummary { X = x, Y = y, Cell = cell };
            if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell) || Grid.WorldIdx[cell] != worldId)
            {
                summary.Symbol = '?';
                summary.Occupancy = "outside_world";
                summary.BlockReason = "outside_world";
                return summary;
            }

            summary.Valid = true;
            summary.Visible = Grid.IsVisible(cell);
            if (visibleOnly && !summary.Visible)
            {
                summary.Symbol = '?';
                summary.Occupancy = "unrevealed";
                summary.BlockReason = "unrevealed";
                return summary;
            }

            if (overlayView)
            {
                summary.ElementId = "OverlayEmpty";
                summary.ElementName = "OverlayEmpty";
                summary.State = "overlay";
                summary.Symbol = '.';
                summary.Occupancy = "empty";
                OverlaySummary overlayOnly;
                if (overlays.TryGetValue(cell, out overlayOnly))
                {
                    summary.Overlay = overlayOnly;
                    summary.Symbol = overlayOnly.Symbol;
                    summary.Occupancy = overlayOnly.Kind;
                    summary.BlockReason = "occupied_by_" + overlayOnly.Kind;
                }
                return summary;
            }

            var element = Grid.Element[cell];
            summary.ElementId = element?.id.ToString() ?? "Unknown";
            summary.ElementName = ToolUtil.CleanName(element?.name ?? summary.ElementId);
            summary.State = ToolUtil.GetElementState(element);
            summary.MassKg = SafeFloat(Grid.Mass[cell]);
            summary.TemperatureK = SafeFloat(Grid.Temperature[cell]);
            summary.DiseaseIdx = Grid.DiseaseIdx[cell];
            summary.DiseaseCount = Grid.DiseaseCount[cell];
            summary.Solid = Grid.Solid[cell];
            summary.Foundation = Grid.Foundation[cell];
            summary.Symbol = SymbolForView(view, element, summary);

            OverlaySummary overlay;
            if (overlays.TryGetValue(cell, out overlay))
            {
                summary.Overlay = overlay;
                summary.Symbol = overlay.Symbol;
            }

            SetCellPlanningState(summary, element);
            return summary;
        }

        private static void SetCellPlanningState(CellSummary summary, Element element)
        {
            if (summary.Overlay != null)
            {
                summary.Occupancy = summary.Overlay.Kind;
                summary.BlockReason = "occupied_by_" + summary.Overlay.Kind;
                summary.Buildable1x1 = false;
                return;
            }

            if (!summary.Visible)
            {
                summary.Occupancy = "unrevealed";
                summary.BlockReason = "unrevealed";
                summary.Buildable1x1 = false;
                return;
            }

            if (summary.Foundation)
            {
                summary.Occupancy = "foundation";
                summary.BlockReason = "constructed_tile";
                summary.Buildable1x1 = false;
                return;
            }

            if (summary.Solid || (element != null && element.IsSolid))
            {
                summary.Occupancy = "solid";
                summary.BlockReason = "solid_cell";
                summary.Buildable1x1 = false;
                return;
            }

            if (element != null && element.IsLiquid)
            {
                summary.Occupancy = "liquid";
                summary.BlockReason = "liquid_cell";
                summary.Buildable1x1 = false;
                return;
            }

            summary.Occupancy = "open";
            summary.BlockReason = null;
            summary.Buildable1x1 = true;
        }

        private static bool IsSparseRelevant(CellSummary summary, bool overlayView)
        {
            if (!summary.Valid || (summary.Symbol == '?' && summary.Overlay == null))
                return false;
            if (overlayView)
                return summary.Overlay != null;
            return summary.Symbol != '.' && summary.Symbol != 'O' && summary.Symbol != 'C' && summary.Symbol != 'P';
        }

        private static Dictionary<string, object> SparseCell(CellSummary summary, int originX = 0, int originY = 0)
        {
            var result = new Dictionary<string, object>
            {
                ["x"] = summary.X,
                ["y"] = summary.Y,
                ["rx"] = summary.X - originX,
                ["ry"] = summary.Y - originY,
                ["s"] = summary.Symbol.ToString()
            };
            if (summary.Overlay != null)
            {
                result["kind"] = summary.Overlay.Kind;
                result["id"] = summary.Overlay.Id;
                if (!string.IsNullOrWhiteSpace(summary.Overlay.Extra))
                    result["extra"] = summary.Overlay.Extra;
            }
            else
            {
                result["element"] = summary.ElementId;
                result["state"] = summary.State;
                result["kg"] = Math.Round(summary.MassKg, 2);
                result["c"] = Math.Round(summary.TemperatureK - 273.15f, 1);
            }
            result["occ"] = summary.Occupancy;
            if (!string.IsNullOrWhiteSpace(summary.BlockReason))
                result["block"] = summary.BlockReason;
            result["buildable1x1"] = summary.Buildable1x1;
            return result;
        }

        private static List<Dictionary<string, object>> SparseRuns(List<Dictionary<string, object>> cells)
        {
            var runs = new List<Dictionary<string, object>>();
            SparseRunBuilder current = null;

            foreach (var cell in cells)
            {
                int x = ToInt(cell, "x");
                int y = ToInt(cell, "y");
                string signature = SparseSignature(cell);
                if (current == null || !current.CanExtend(x, y, signature))
                {
                    if (current != null)
                        runs.Add(current.ToDictionary());
                    current = new SparseRunBuilder(cell, x, y, signature);
                    continue;
                }

                current.Add(cell, x);
            }

            if (current != null)
                runs.Add(current.ToDictionary());
            return runs;
        }

        private static string SparseSignature(Dictionary<string, object> item)
        {
            return string.Join("|", new[]
            {
                item.ContainsKey("s") ? item["s"]?.ToString() ?? "" : "",
                item.ContainsKey("kind") ? item["kind"]?.ToString() ?? "" : "",
                item.ContainsKey("id") ? item["id"]?.ToString() ?? "" : "",
                item.ContainsKey("extra") ? item["extra"]?.ToString() ?? "" : "",
                item.ContainsKey("element") ? item["element"]?.ToString() ?? "" : "",
                item.ContainsKey("state") ? item["state"]?.ToString() ?? "" : "",
                item.ContainsKey("occ") ? item["occ"]?.ToString() ?? "" : "",
                item.ContainsKey("block") ? item["block"]?.ToString() ?? "" : ""
            });
        }

        private static string SparseCellLine(Dictionary<string, object> item, bool minimal, string view = "base")
        {
            string xy = item["x"] + "," + item["y"];
            string symbol = TokenForSparseItem(item, view);
            string kind = item.ContainsKey("kind") ? item["kind"]?.ToString() : item.ContainsKey("element") ? item["element"]?.ToString() : "";
            string id = item.ContainsKey("id") ? item["id"]?.ToString() : "";
            string extra = item.ContainsKey("extra") ? " " + item["extra"] : "";
            return minimal
                ? $"{xy}:{symbol} {kind} {id}{extra}".TrimEnd()
                : $"- at=({xy}) token={symbol} kind={kind} id={id}{extra}".TrimEnd();
        }

        private static string SparseRunLine(Dictionary<string, object> item, bool minimal, string view = "base")
        {
            int x1 = ToInt(item, "x1");
            int x2 = ToInt(item, "x2");
            int rx1 = ToInt(item, "rx1");
            int rx2 = ToInt(item, "rx2");
            int ry = ToInt(item, "ry");
            string y = item["y"]?.ToString() ?? "?";
            string xPart = x1 == x2 ? x1.ToString() : x1 + ".." + x2;
            string rxPart = rx1 == rx2 ? rx1.ToString() : rx1 + ".." + rx2;
            string symbol = TokenForSparseItem(item, view);
            string kind = item.ContainsKey("kind") ? item["kind"]?.ToString() : item.ContainsKey("element") ? item["element"]?.ToString() : "";
            string id = item.ContainsKey("id") ? item["id"]?.ToString() : "";
            string extra = item.ContainsKey("extra") ? " " + item["extra"] : "";
            string averages = "";
            if (item.ContainsKey("kgAvg") || item.ContainsKey("cAvg"))
            {
                string kg = item.ContainsKey("kgAvg") ? " kg~" + item["kgAvg"] : "";
                string c = item.ContainsKey("cAvg") ? " c~" + item["cAvg"] : "";
                averages = kg + c;
            }
            return minimal
                ? $"r{rxPart},{ry} abs{xPart},{y}:{symbol} {kind} {id}{extra}{averages}".TrimEnd()
                : $"- ry={ry} absY={y} rx={rxPart} absX={xPart} n={item["n"]} token={symbol} kind={kind} id={id}{extra}{averages}".TrimEnd();
        }

        private static string TokenForSparseItem(Dictionary<string, object> item, string view)
        {
            string raw = item.ContainsKey("s") ? item["s"]?.ToString() ?? "?" : "?";
            char symbol = string.IsNullOrEmpty(raw) ? '?' : raw[0];
            return TokenForSymbol(symbol, view);
        }

        private static string CellDetailLine(CellSummary summary, int originX, int originY, string view)
        {
            if (!summary.Valid)
                return $"rxy={summary.X - originX},{summary.Y - originY} abs={summary.X},{summary.Y} token=unk";

            string overlay = summary.Overlay != null ? $" obj={summary.Overlay.Kind}:{summary.Overlay.Id}" : "";
            string block = string.IsNullOrWhiteSpace(summary.BlockReason) ? "" : $" block={summary.BlockReason}";
            return $"rxy={summary.X - originX},{summary.Y - originY} abs={summary.X},{summary.Y} token={TokenForSymbol(summary.Symbol, view, summary)} occ={summary.Occupancy} buildable1x1={summary.Buildable1x1}{block} elem={summary.ElementId} state={summary.State} kg={Math.Round(summary.MassKg, 3)} C={Math.Round(summary.TemperatureK - 273.15f, 1)} visible={summary.Visible} disease={summary.DiseaseIdx}:{summary.DiseaseCount}{overlay}";
        }

        private static int ToInt(Dictionary<string, object> item, string key)
        {
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return 0;
            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static double ToDouble(Dictionary<string, object> item, string key)
        {
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return 0d;
            double parsed;
            return double.TryParse(value.ToString(), out parsed) ? parsed : 0d;
        }

        private static char SymbolForView(string view, Element element, CellSummary summary)
        {
            if (view == "temperature")
                return SymbolForTemperature(summary.TemperatureK);
            return SymbolForCell(element, summary);
        }

        private static char SymbolForTemperature(float temperatureK)
        {
            float celsius = SafeFloat(temperatureK) - 273.15f;
            if (celsius < -20f)
                return 'F';
            if (celsius < 5f)
                return 'c';
            if (celsius < 35f)
                return 'm';
            if (celsius < 75f)
                return 'h';
            return 'X';
        }

        private static char SymbolForCell(Element element, CellSummary summary)
        {
            if (summary.Foundation)
                return 'T';
            if (element == null || element.IsVacuum)
                return '.';
            if (element.IsSolid || summary.Solid)
                return 'S';
            if (element.IsLiquid)
                return 'L';

            switch (element.id)
            {
                case SimHashes.Oxygen:
                    return 'O';
                case SimHashes.ContaminatedOxygen:
                    return 'P';
                case SimHashes.CarbonDioxide:
                    return 'C';
                case SimHashes.Hydrogen:
                    return 'H';
                default:
                    return char.ToLowerInvariant(element.id.ToString()[0]);
            }
        }

        private class CellSummary
        {
            public int Cell;
            public int X;
            public int Y;
            public bool Valid;
            public bool Visible;
            public string ElementId;
            public string ElementName;
            public string State;
            public float MassKg;
            public float TemperatureK;
            public int DiseaseIdx;
            public int DiseaseCount;
            public bool Solid;
            public bool Foundation;
            public char Symbol;
            public OverlaySummary Overlay;
            public string Occupancy;
            public string BlockReason;
            public bool Buildable1x1;

            public string ToDetailLine()
            {
                if (!Valid)
                    return $"({X},{Y}) ?";

                string overlay = Overlay != null ? $" obj={Overlay.Kind}:{Overlay.Id}" : "";
                string block = string.IsNullOrWhiteSpace(BlockReason) ? "" : $" block={BlockReason}";
                return $"({X},{Y}) {Symbol} occ={Occupancy} buildable1x1={Buildable1x1}{block} elem={ElementId} state={State} massKg={Math.Round(MassKg, 3)} tempC={Math.Round(TemperatureK - 273.15f, 1)} visible={Visible} disease={DiseaseIdx}:{DiseaseCount}{overlay}";
            }
        }


        private sealed class SparseRunBuilder
        {
            private readonly Dictionary<string, object> sample;
            private readonly string signature;
            private readonly int y;
            private readonly int ry;
            private int x1;
            private int x2;
            private int rx1;
            private int rx2;
            private int count;
            private double kgTotal;
            private double cTotal;
            private bool hasKg;
            private bool hasC;

            public SparseRunBuilder(Dictionary<string, object> item, int x, int y, string signature)
            {
                this.sample = item;
                this.signature = signature;
                this.y = y;
                this.ry = ToInt(item, "ry");
                x1 = x;
                x2 = x;
                rx1 = ToInt(item, "rx");
                rx2 = rx1;
                Add(item, x);
            }

            public bool CanExtend(int x, int nextY, string nextSignature)
            {
                return nextY == y && x == x2 + 1 && nextSignature == signature;
            }

            public void Add(Dictionary<string, object> item, int x)
            {
                x2 = x;
                rx2 = ToInt(item, "rx");
                count++;
                if (item.ContainsKey("kg"))
                {
                    kgTotal += ToDouble(item, "kg");
                    hasKg = true;
                }
                if (item.ContainsKey("c"))
                {
                    cTotal += ToDouble(item, "c");
                    hasC = true;
                }
            }

            public Dictionary<string, object> ToDictionary()
            {
                var result = new Dictionary<string, object>
                {
                    ["x1"] = x1,
                    ["x2"] = x2,
                    ["y"] = y,
                    ["rx1"] = rx1,
                    ["rx2"] = rx2,
                    ["ry"] = ry,
                    ["n"] = count,
                    ["s"] = sample.ContainsKey("s") ? sample["s"] : "?"
                };

                CopyIfPresent(sample, result, "kind");
                CopyIfPresent(sample, result, "id");
                CopyIfPresent(sample, result, "extra");
                CopyIfPresent(sample, result, "element");
                CopyIfPresent(sample, result, "state");
                if (hasKg)
                    result["kgAvg"] = Math.Round(kgTotal / Math.Max(1, count), 2);
                if (hasC)
                    result["cAvg"] = Math.Round(cTotal / Math.Max(1, count), 1);
                return result;
            }

            private static void CopyIfPresent(Dictionary<string, object> source, Dictionary<string, object> target, string key)
            {
                if (source.ContainsKey(key))
                    target[key] = source[key];
            }
        }
    }
}
