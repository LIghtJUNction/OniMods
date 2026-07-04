using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static Dictionary<string, object> BuildPlanningSummary(Dictionary<string, int> rect, int worldId, bool visibleOnly, string purpose, int limit, int? candidateWidth = null, int? candidateHeight = null, bool detailHazards = false)
        {
            var defaults = LayoutDefaults(NormalizeLayoutPurpose(purpose));
            int roomWidth = Math.Max(4, candidateWidth ?? defaults.Width);
            int roomHeight = Math.Max(3, candidateHeight ?? defaults.Height);
            var occupied = BuildOccupiedCells(rect, worldId);
            var hazards = new List<Dictionary<string, object>>();
            var hazardCells = new List<int[]>();
            var hazardByElement = new Dictionary<string, int>();
            int hazardCount = 0;
            var floorRuns = new List<Dictionary<string, object>>();
            var digRuns = new List<Dictionary<string, object>>();
            var candidates = new List<LayoutCandidate>();

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                AddRunsForRow(rect, worldId, visibleOnly, occupied, y, floorRuns, digRuns);
            }

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (IsHazardCell(cell, visibleOnly))
                    {
                        hazardCount++;
                        var element = Grid.Element[cell];
                        string elemName = element?.id.ToString() ?? "Unknown";
                        if (!hazardByElement.ContainsKey(elemName))
                            hazardByElement[elemName] = 0;
                        hazardByElement[elemName]++;

                        if (detailHazards)
                        {
                            hazards.Add(HazardInfo(cell, x, y));
                        }
                        else
                        {
                            hazardCells.Add(new[] { x, y });
                        }
                    }
                }
            }

            for (int y1 = rect["y1"]; y1 <= rect["y2"] - roomHeight + 1; y1++)
            {
                for (int x1 = rect["x1"]; x1 <= rect["x2"] - roomWidth + 1; x1++)
                {
                    var candidate = EvaluateLayoutCandidate(x1, y1, roomWidth, roomHeight, worldId, visibleOnly, occupied, NormalizeLayoutPurpose(purpose));
                    if (candidate != null)
                        candidates.Add(candidate);
                }
            }

            var top = candidates
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.RequiredDig)
                .ThenBy(item => item.RequiredTiles)
                .Take(Math.Max(1, limit))
                .Select(item => item.ToDictionary())
                .ToList();

            return new Dictionary<string, object>
            {
                ["purpose"] = NormalizeLayoutPurpose(purpose),
                ["candidateSize"] = new[] { roomWidth, roomHeight },
                ["legend"] = new Dictionary<string, object>
                {
                    ["floorRuns"] = "existing continuous standable support lines as [x1,y,x2,y]",
                    ["digRuns"] = "continuous visible natural-solid dig lines as [x1,y,x2,y]",
                    ["candidates"] = "candidate room rectangles with required dig/tile counts and hazard score"
                },
                ["counts"] = new Dictionary<string, object>
                {
                    ["floorRuns"] = floorRuns.Count,
                    ["digRuns"] = digRuns.Count,
                    ["hazards"] = hazardCount,
                    ["candidates"] = candidates.Count
                },
                ["floorRuns"] = floorRuns.Take(30).ToList(),
                ["digRuns"] = digRuns.Take(30).ToList(),
                ["hazards"] = detailHazards
                    ? (object)hazards.Take(40).ToList()
                    : new Dictionary<string, object>
                    {
                        ["totalCount"] = hazardCount,
                        ["byElement"] = hazardByElement.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value),
                        ["runs"] = CoordinateRuns(hazardCells, 80),
                        ["sampleCells"] = hazardCells.Take(40).ToList(),
                        ["truncatedSampleCells"] = Math.Max(0, hazardCells.Count - 40),
                        ["note"] = "runs are compact row spans [x1,y,x2,y]; sampleCells is intentionally capped for token efficiency"
                    },
                ["candidates"] = top
            };
        }

        private static void AddRunsForRow(Dictionary<string, int> rect, int worldId, bool visibleOnly, HashSet<int> occupied, int y, List<Dictionary<string, object>> floorRuns, List<Dictionary<string, object>> digRuns)
        {
            int floorStart = -1;
            int digStart = -1;
            for (int x = rect["x1"]; x <= rect["x2"]; x++)
            {
                int cell = Grid.XYToCell(x, y);
                bool floor = IsStandableCell(cell, worldId, visibleOnly, occupied);
                bool dig = IsDiggableCell(cell, worldId, visibleOnly);

                if (floor && floorStart < 0) floorStart = x;
                if (!floor && floorStart >= 0)
                {
                    AddRun(floorRuns, floorStart, x - 1, y, "floor");
                    floorStart = -1;
                }

                if (dig && digStart < 0) digStart = x;
                if (!dig && digStart >= 0)
                {
                    AddRun(digRuns, digStart, x - 1, y, "dig");
                    digStart = -1;
                }
            }

            if (floorStart >= 0)
                AddRun(floorRuns, floorStart, rect["x2"], y, "floor");
            if (digStart >= 0)
                AddRun(digRuns, digStart, rect["x2"], y, "dig");
        }

        private static void AddRun(List<Dictionary<string, object>> runs, int x1, int x2, int y, string kind)
        {
            int length = x2 - x1 + 1;
            if (length < 3)
                return;
            runs.Add(new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["line"] = new[] { x1, y, x2, y },
                ["length"] = length
            });
        }

        private static LayoutCandidate EvaluateLayoutCandidate(int x1, int y1, int width, int height, int worldId, bool visibleOnly, HashSet<int> occupied, string purpose)
        {
            int x2 = x1 + width - 1;
            int y2 = y1 + height - 1;
            int open = 0;
            int solid = 0;
            int unknown = 0;
            int hazards = 0;
            int occupiedCount = 0;
            int foundation = 0;
            int support = 0;
            bool reachable = false;

            for (int y = y1; y <= y2; y++)
            {
                for (int x = x1; x <= x2; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || (visibleOnly && !Grid.IsVisible(cell)))
                    {
                        unknown++;
                        continue;
                    }

                    if (occupied.Contains(cell))
                        occupiedCount++;
                    if (IsHazardCell(cell, visibleOnly))
                        hazards++;

                    var element = Grid.Element[cell];
                    if (Grid.Foundation[cell])
                    {
                        foundation++;
                        support++;
                    }
                    else if (element != null && (element.IsSolid || Grid.Solid[cell]))
                    {
                        solid++;
                    }
                    else
                    {
                        open++;
                    }

                    if (y == y1 && IsStandableCell(cell, worldId, visibleOnly, occupied))
                        reachable = true;
                }
            }

            if (unknown > width * height / 2)
                return null;
            if (occupiedCount > Math.Max(2, width / 3))
                return null;

            int requiredTiles = Math.Max(0, width - support);
            int requiredDig = solid;
            int score = 100;
            score -= requiredDig * 2;
            score -= requiredTiles * 3;
            score -= hazards * 12;
            score -= occupiedCount * 10;
            score -= unknown * 4;
            if (reachable) score += 15;
            if (purpose == "lab" || purpose == "power") score += support > 0 ? 8 : 0;
            if (purpose == "bathroom") score -= hazards * 6;
            if (purpose == "farm") score -= requiredTiles;
            if (score < 20 && hazards > 0)
                return null;

            return new LayoutCandidate
            {
                Purpose = purpose,
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Width = width,
                Height = height,
                Score = score,
                OpenCells = open,
                SolidCells = solid,
                UnknownCells = unknown,
                HazardCells = hazards,
                OccupiedCells = occupiedCount,
                ExistingSupportCells = support,
                RequiredDig = requiredDig,
                RequiredTiles = requiredTiles,
                Reachable = reachable,
                Classification = requiredDig == 0 && requiredTiles == 0 ? "open_ready" : requiredDig > open ? "excavate_room" : "mixed_platform"
            };
        }

        private static bool IsStandableCell(int cell, int worldId, bool visibleOnly, HashSet<int> occupied)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || (visibleOnly && !Grid.IsVisible(cell)))
                return false;
            if (occupied.Contains(cell))
                return false;
            if (!Grid.Foundation[cell] && !Grid.Solid[cell])
                return false;

            int above = Grid.CellAbove(cell);
            if (!Grid.IsValidCell(above) || !ToolUtil.CellMatchesWorld(above, worldId) || (visibleOnly && !Grid.IsVisible(above)))
                return false;
            return !Grid.Solid[above] && !Grid.Foundation[above];
        }

        private static bool IsDiggableCell(int cell, int worldId, bool visibleOnly)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || (visibleOnly && !Grid.IsVisible(cell)))
                return false;
            var element = Grid.Element[cell];
            return element != null && element.IsSolid && !Grid.Foundation[cell];
        }

        private static bool IsHazardCell(int cell, bool visibleOnly)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell) || (visibleOnly && !Grid.IsVisible(cell)))
                return false;
            var element = Grid.Element[cell];
            float tempC = SafeFloat(Grid.Temperature[cell]) - 273.15f;
            if (tempC > 55f || tempC < -20f)
                return true;
            if (Grid.DiseaseCount[cell] > 1000)
                return true;
            if (element == null)
                return false;
            return element.IsLiquid || element.id == SimHashes.ContaminatedOxygen || element.id == SimHashes.ChlorineGas;
        }

        private static Dictionary<string, object> HazardInfo(int cell, int x, int y)
        {
            var element = Grid.Element[cell];
            return new Dictionary<string, object>
            {
                ["xy"] = new[] { x, y },
                ["element"] = element?.id.ToString() ?? "Unknown",
                ["state"] = ToolUtil.GetElementState(element),
                ["kg"] = Math.Round(SafeFloat(Grid.Mass[cell]), 2),
                ["celsius"] = Math.Round(SafeFloat(Grid.Temperature[cell]) - 273.15f, 1),
                ["disease"] = Grid.DiseaseCount[cell]
            };
        }

        private static List<int[]> CoordinateRuns(List<int[]> cells, int limit)
        {
            var runs = new List<int[]>();
            if (cells == null || cells.Count == 0 || limit <= 0)
                return runs;

            var ordered = cells
                .OrderBy(cell => cell[1])
                .ThenBy(cell => cell[0])
                .ToList();
            int startX = ordered[0][0];
            int endX = startX;
            int y = ordered[0][1];

            for (int i = 1; i < ordered.Count; i++)
            {
                int x = ordered[i][0];
                int nextY = ordered[i][1];
                if (nextY == y && x == endX + 1)
                {
                    endX = x;
                    continue;
                }

                runs.Add(new[] { startX, y, endX, y });
                if (runs.Count >= limit)
                    return runs;
                startX = x;
                endX = x;
                y = nextY;
            }

            runs.Add(new[] { startX, y, endX, y });
            if (runs.Count > limit)
                return runs.Take(limit).ToList();
            return runs;
        }

        private static HashSet<int> BuildOccupiedCells(Dictionary<string, int> rect, int worldId)
        {
            var occupied = new HashSet<int>();
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.GetMyWorldId() != worldId)
                    continue;
                var def = building.Def;
                string prefabId = def?.PrefabID ?? building.name;
                if (IsTerrainSupportPrefab(prefabId))
                    continue;
                int cell = Grid.PosToCell(building.gameObject);
                if (!Grid.IsValidCell(cell))
                    continue;
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                if (!InRect(rect, x, y))
                    continue;
                occupied.Add(cell);
            }
            return occupied;
        }


        private sealed class LayoutCandidate
        {
            public string Purpose;
            public int X1;
            public int Y1;
            public int X2;
            public int Y2;
            public int Width;
            public int Height;
            public int Score;
            public int OpenCells;
            public int SolidCells;
            public int UnknownCells;
            public int HazardCells;
            public int OccupiedCells;
            public int ExistingSupportCells;
            public int RequiredDig;
            public int RequiredTiles;
            public bool Reachable;
            public string Classification;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["purpose"] = Purpose,
                    ["rect"] = new[] { X1, Y1, X2, Y2 },
                    ["size"] = new[] { Width, Height },
                    ["score"] = Score,
                    ["scoreExplanation"] = "Starts at 100, subtracts requiredDig*2, requiredTiles*3, hazardCells*12, occupiedCells*10, unknownCells*4, then adds reachability/purpose bonuses.",
                    ["classification"] = Classification,
                    ["classificationDescription"] = ClassificationDescription(Classification),
                    ["reachable"] = Reachable,
                    ["requiredDig"] = RequiredDig,
                    ["requiredTiles"] = RequiredTiles,
                    ["existingSupportCells"] = ExistingSupportCells,
                    ["hazardCells"] = HazardCells,
                    ["occupiedCells"] = OccupiedCells,
                    ["unknownCells"] = UnknownCells,
                    ["openCells"] = OpenCells,
                    ["solidCells"] = SolidCells,
                    ["suggestedFloorLine"] = new[] { X1, Y1, X2, Y1 },
                    ["suggestedDigRect"] = RequiredDig > 0 ? (object)new[] { X1, Y1, X2, Y2 } : null
                };
            }

            private static string ClassificationDescription(string classification)
            {
                switch (classification)
                {
                    case "open_ready":
                        return "Already open and has enough support/floor cells; likely ready for placement with little or no digging.";
                    case "excavate_room":
                        return "Mostly solid compared with open cells; plan a dig pass before using it as a room.";
                    case "mixed_platform":
                        return "Part open, part solid or missing floor; needs a mixed dig/build-floor plan.";
                    default:
                        return "Unclassified candidate.";
                }
            }
        }
    }
}
