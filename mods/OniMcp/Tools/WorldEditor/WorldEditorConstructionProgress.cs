using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string ReadConstructionProgressMarkdown(JObject args)
        {
            if (!ConstructionProgressReport.TryReadPage(args, out int offset, out int limit, out string error))
                return ConstructionProgressReport.Unavailable(error);

            int worldId = ClusterManager.Instance?.activeWorldId ?? -1;
            if (worldId < 0 || Game.Instance == null)
                return ConstructionProgressReport.Unavailable("game_not_loaded");

            try
            {
                // One scan per explicit progress read, never one global scan per map cell.
                // Do not cache Unity objects across frames, world changes or save loads.
                var pending = UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None)
                    .Where(item => IsVisiblePendingConstruction(item, worldId))
                    .ToList();
                string query = (args?["query"]?.ToString() ?? string.Empty).Trim();
                var matches = pending.Where(item => MatchesConstructionQuery(item, query))
                    .OrderBy(item => item.GetInstanceID()).ToList();
                var rows = new List<ConstructionProgressRow>();
                foreach (var item in matches.Skip(offset).Take(limit))
                {
                    var go = item.gameObject;
                    var kpid = go.GetComponent<KPrefabID>();
                    int cell = Grid.PosToCell(go);
                    rows.Add(new ConstructionProgressRow
                    {
                        Id = kpid?.InstanceID ?? go.GetInstanceID(),
                        X = Grid.CellColumn(cell),
                        Y = Grid.CellRow(cell),
                        PrefabId = ConstructionPrefabId(item),
                        MainStatus = ReadConstructionMainStatus(go)
                    });
                }
                return ConstructionProgressReport.Render(worldId, pending.Count, matches.Count, offset, rows);
            }
            catch
            {
                // A failed observation must never look like zero unfinished buildings.
                return ConstructionProgressReport.Unavailable("construction_scan_failed");
            }
        }

        private static bool IsVisiblePendingConstruction(Constructable item, int worldId)
        {
            var go = item?.gameObject;
            if (go == null || go.GetComponent<BuildingComplete>() != null
                || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            int cell = Grid.PosToCell(go);
            return Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell)
                && Grid.WorldIdx[cell] == worldId && Grid.IsVisible(cell);
        }

        private static string ConstructionPrefabId(Constructable item)
        {
            var go = item.gameObject;
            return go.GetComponent<Building>()?.Def?.PrefabID
                ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
        }

        private static bool MatchesConstructionQuery(Constructable item, string query)
        {
            return query.Length == 0
                || ConstructionPrefabId(item).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || ToolUtil.CleanName(item.gameObject.GetProperName())
                    .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadConstructionMainStatus(GameObject go)
        {
            try
            {
                var selectable = go.GetComponent<KSelectable>();
                if (selectable == null || Db.Get() == null)
                    return null;
                var status = selectable.GetStatusItem(Db.Get().StatusItemCategories.Main);
                // Null status data is valid for static native status labels.
                return status.item == null ? null : ToolUtil.CleanName(status.item.GetName(status.data));
            }
            catch
            {
                return null;
            }
        }
    }
}
