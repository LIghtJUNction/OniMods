using System;
using System.Collections.Generic;
using System.Linq;
using Database;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using TemplateClasses;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SandboxTools
    {
        private static Dictionary<string, object> CellSample(int cell)
        {
            var element = Grid.Element[cell];
            string diseaseId = null;
            if (Grid.DiseaseIdx[cell] != byte.MaxValue && Grid.DiseaseIdx[cell] >= 0)
                diseaseId = Db.Get().Diseases[Grid.DiseaseIdx[cell]]?.id.ToString();

            Grid.CellToXY(cell, out int x, out int y);
            return new Dictionary<string, object>
            {
                ["cell"] = cell,
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = Grid.WorldIdx[cell],
                ["isVisible"] = Grid.IsVisible(cell),
                ["element"] = element?.id.ToString() ?? "Unknown",
                ["elementName"] = ToolUtil.CleanName(element?.name ?? "Unknown"),
                ["state"] = ToolUtil.GetElementState(element),
                ["massKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                ["temperatureK"] = Math.Round(ToolUtil.SafeFloat(Grid.Temperature[cell]), 2),
                ["disease"] = diseaseId,
                ["diseaseCount"] = Grid.DiseaseCount[cell],
                ["paintArguments"] = new Dictionary<string, object>
                {
                    ["element"] = element?.id.ToString() ?? "Vacuum",
                    ["massKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                    ["temperatureK"] = Math.Round(ToolUtil.SafeFloat(Grid.Temperature[cell]), 2),
                    ["disease"] = diseaseId,
                    ["diseaseCount"] = Grid.DiseaseCount[cell]
                }
            };
        }

        private static Building FindBuilding(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var buildingComplete in Components.BuildingCompletes.Items)
            {
                var building = buildingComplete?.GetComponent<Building>();
                if (building == null || !ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;
                var kpid = building.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return building;
                if (cell.HasValue && Grid.PosToCell(building) == cell.Value)
                    return building;
            }
            return null;
        }

        private static Dictionary<string, object> BuildingInfo(Building building)
        {
            int cell = Grid.PosToCell(building);
            var kpid = building.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? building.gameObject.GetInstanceID(),
                ["prefabId"] = building.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? building.name,
                ["name"] = ToolUtil.CleanName(building.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static void SpawnDebugMinion(Building building)
        {
            var stats = new MinionStartingStats(is_starter_minion: false, null, null, isDebugMinion: true);
            GameObject prefab = Assets.GetPrefab(BaseMinionConfig.GetMinionIDForModel(stats.personality.model));
            GameObject minion = Util.KInstantiate(prefab);
            minion.name = prefab.name;
            Immigration.Instance.ApplyDefaultPersonalPriorities(minion);
            Vector3 position = Grid.CellToPos(Grid.PosToCell(building), CellAlignment.Bottom, Grid.SceneLayer.Move);
            minion.transform.SetLocalPosition(position);
            minion.SetActive(true);
            stats.Apply(minion);
        }

        private static bool StoryMatches(Story story, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(story.Id, q)
                || Contains(story.worldgenStoryTraitKey, q)
                || Contains(story.sandboxStampTemplateId, q)
                || Contains(story.StoryTrait?.name, q);
        }

        private static Dictionary<string, object> StoryInfo(Story story)
        {
            TemplateContainer template = string.IsNullOrWhiteSpace(story.sandboxStampTemplateId)
                ? null
                : TemplateCache.GetTemplate(story.sandboxStampTemplateId);
            return new Dictionary<string, object>
            {
                ["storyId"] = story.Id,
                ["traitKey"] = story.worldgenStoryTraitKey,
                ["traitName"] = ToolUtil.CleanName(story.StoryTrait?.name ?? story.Id),
                ["templateId"] = story.sandboxStampTemplateId,
                ["hasTemplate"] = template != null,
                ["size"] = template?.info == null ? null : new { x = template.info.size.X, y = template.info.size.Y },
                ["alreadyExists"] = StoryManager.Instance?.GetStoryInstance(story) != null,
                ["keepsakePrefabId"] = story.keepsakePrefabId
            };
        }

        private static string ValidateStoryTemplateCells(TemplateContainer template, int originX, int originY, int worldId)
        {
            if (template.cells == null)
                return null;
            foreach (var cellInfo in template.cells)
            {
                int cell = Grid.XYToCell(originX + cellInfo.location_x, originY + cellInfo.location_y);
                if (!Grid.IsValidBuildingCell(cell))
                    return "Template would place outside valid building cells";
                if (!ToolUtil.CellMatchesWorld(cell, worldId))
                    return $"Template would cross out of worldId={worldId}";
                if (Grid.Element[cell]?.id == SimHashes.Unobtanium)
                    return "Template would overwrite neutronium/unobtanium";
            }
            return null;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> ActionInfo(string name, string mode, string risk, string description)
        {
            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["mode"] = mode,
                ["risk"] = risk,
                ["description"] = description
            };
        }

        private class TokenGrid
        {
            public readonly List<string[]> Rows;
            public readonly int Width;
            public readonly int Height;
            public readonly string Error;

            public TokenGrid(List<string[]> rows)
            {
                Rows = rows;
                Height = rows.Count;
                Width = rows[0].Length;
            }

            private TokenGrid(string error)
            {
                Rows = new List<string[]>();
                Error = error;
            }

            public static TokenGrid Fail(string error)
            {
                return new TokenGrid(error);
            }
        }

        private class MapPatternMatch
        {
            public readonly int LeftX;
            public readonly int TopY;
            public readonly int Width;
            public readonly int Height;

            public MapPatternMatch(int leftX, int topY, int width, int height)
            {
                LeftX = leftX;
                TopY = topY;
                Width = width;
                Height = height;
            }

            public int RightX { get { return LeftX + Width - 1; } }
            public int BottomY { get { return TopY - Height + 1; } }
        }

        private class SelectedMapMatches
        {
            public readonly List<MapPatternMatch> Matches;
            public readonly string Error;

            public SelectedMapMatches(List<MapPatternMatch> matches)
            {
                Matches = matches;
            }

            private SelectedMapMatches(string error)
            {
                Matches = new List<MapPatternMatch>();
                Error = error;
            }

            public static SelectedMapMatches Fail(string error)
            {
                return new SelectedMapMatches(error);
            }
        }

        private class ReplacementChangeSet
        {
            public readonly List<MapReplacementChange> Items;
            public readonly string Error;

            public ReplacementChangeSet(List<MapReplacementChange> items)
            {
                Items = items;
            }

            private ReplacementChangeSet(string error)
            {
                Items = new List<MapReplacementChange>();
                Error = error;
            }

            public static ReplacementChangeSet Fail(string error)
            {
                return new ReplacementChangeSet(error);
            }
        }

        private class MapReplacementChange
        {
            public readonly int Cell;
            public readonly int X;
            public readonly int Y;
            public readonly string FromToken;
            public readonly Element Element;
            public readonly float MassKg;
            public readonly float TemperatureK;
            public readonly byte DiseaseIdx;
            public readonly int DiseaseCount;

            public MapReplacementChange(int cell, int x, int y, string fromToken, Element element, float massKg, float temperatureK, byte diseaseIdx, int diseaseCount)
            {
                Cell = cell;
                X = x;
                Y = y;
                FromToken = fromToken;
                Element = element;
                MassKg = massKg;
                TemperatureK = temperatureK;
                DiseaseIdx = diseaseIdx;
                DiseaseCount = diseaseCount;
            }
        }
    }
}
