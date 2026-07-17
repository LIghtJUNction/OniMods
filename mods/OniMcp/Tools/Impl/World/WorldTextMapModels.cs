using System;
using System.Collections.Generic;
using System.Text;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static void AddElementAggregate(Dictionary<string, ElementAggregate> groups, CellSummary summary)
        {
            if (!summary.Valid || string.IsNullOrEmpty(summary.ElementId))
                return;

            ElementAggregate aggregate;
            if (!groups.TryGetValue(summary.ElementId, out aggregate))
            {
                aggregate = new ElementAggregate
                {
                    Id = summary.ElementId,
                    Name = summary.ElementName,
                    State = summary.State
                };
                groups[summary.ElementId] = aggregate;
            }

            float weight = Math.Max(summary.MassKg, 0.001f);
            aggregate.CellCount++;
            aggregate.TotalMassKg += summary.MassKg;
            aggregate.WeightedTemperatureK += summary.TemperatureK * weight;
            aggregate.TemperatureWeight += weight;
        }

        private class OverlaySummary
        {
            public string Key;
            public string Kind;
            public string Id;
            public string Name;
            public int X;
            public int Y;
            public int ObjectX;
            public int ObjectY;
            public int ObjectCell;
            public int AnchorX;
            public int AnchorY;
            public int AnchorCell;
            public int Width;
            public int Height;
            public int FootprintX1;
            public int FootprintY1;
            public int FootprintX2;
            public int FootprintY2;
            public char Symbol;
            public char ObjectSymbol;
            public char FootprintSymbol;
            public char AnchorSymbol;
            public bool IsAnchor;
            public bool IsFootprint;
            public int Priority;
            public string BuildLocationRule;
            public bool SupportRequired;
            public bool? Supported;
            public List<Dictionary<string, object>> MissingSupportCells;
            public List<string> ObstructedBy;
            public string Extra;
        }

        private class SnapshotMapAccumulator
        {
            public readonly string View;
            public readonly bool Sparse;
            public readonly bool OverlayView;
            public readonly bool IncludeElements;
            public readonly int ElementLimit;
            public readonly int ObjectLimit;
            public readonly string Encoding;
            private readonly int originX;
            private readonly int originY;
            public readonly Dictionary<int, OverlaySummary> Overlays;
            public readonly List<Dictionary<string, object>> Rows = new List<Dictionary<string, object>>();
            public readonly List<Dictionary<string, object>> SparseCells = new List<Dictionary<string, object>>();
            public readonly Dictionary<string, ElementAggregate> ElementCounts = new Dictionary<string, ElementAggregate>();
            public int ValidCells;
            public int VisibleCells;
            public int OpenCells;
            public int OccupiedCells;
            public int BlockedCells;
            public int BuildableCells;
            private readonly StringBuilder rowSymbols = new StringBuilder();
            private int currentY;

            public SnapshotMapAccumulator(string view, bool sparse, bool visibleOnly, string encoding, int originX, int originY, Dictionary<int, OverlaySummary> overlays, bool includeElements, int elementLimit, int objectLimit)
            {
                View = view;
                Sparse = sparse;
                OverlayView = IsUtilityOverlayView(view);
                Encoding = encoding;
                this.originX = originX;
                this.originY = originY;
                Overlays = overlays;
                IncludeElements = includeElements;
                ElementLimit = elementLimit;
                ObjectLimit = objectLimit;
            }

            public void StartRow(int y)
            {
                currentY = y;
                rowSymbols.Length = 0;
            }

            public void Add(CellSummary summary)
            {
                rowSymbols.Append(summary.Symbol);
                if (summary.Valid)
                {
                    ValidCells++;
                    if (summary.Occupancy == "open")
                        OpenCells++;
                    if (!string.IsNullOrWhiteSpace(summary.BlockReason))
                        BlockedCells++;
                    if (summary.Overlay != null)
                        OccupiedCells++;
                    if (summary.Buildable1x1)
                        BuildableCells++;
                }
                if (summary.Visible)
                    VisibleCells++;
                AddElementAggregate(ElementCounts, summary);
                if (Sparse && IsSparseRelevant(summary, OverlayView))
                    SparseCells.Add(SparseCell(summary, originX, originY));
            }

            public void EndRow()
            {
                if (!Sparse)
                    Rows.Add(MapRow(currentY, originY, rowSymbols.ToString(), Encoding));
            }
        }


        private class ElementAggregate
        {
            public string Id;
            public string Name;
            public string State;
            public int CellCount;
            public float TotalMassKg;
            public float WeightedTemperatureK;
            public float TemperatureWeight;

            public Dictionary<string, object> ToDictionary()
            {
                float avgK = TemperatureWeight > 0f ? WeightedTemperatureK / TemperatureWeight : 0f;
                return new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["name"] = Name,
                    ["state"] = State,
                    ["cellCount"] = CellCount,
                    ["totalMassKg"] = Math.Round(TotalMassKg, 3),
                    ["averageTemperatureK"] = Math.Round(avgK, 2),
                    ["averageTemperatureC"] = Math.Round(avgK - 273.15f, 2)
                };
            }
        }

        private struct LayoutSize
        {
            public readonly int Width;
            public readonly int Height;

            public LayoutSize(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }
    }
}
