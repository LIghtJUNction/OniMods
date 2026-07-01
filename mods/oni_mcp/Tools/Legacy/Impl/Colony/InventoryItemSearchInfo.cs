using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class InventoryTools
    {
        private static Dictionary<string, object> ItemSearchInfo(Pickupable pickupable)
        {
            var go = pickupable.gameObject;
            var kpid = pickupable.KPrefabID ?? go.GetComponent<KPrefabID>();
            var primary = pickupable.PrimaryElement ?? go.GetComponent<PrimaryElement>();
            var edible = go.GetComponent<Edible>();
            var storage = pickupable.storage;
            int cell = pickupable.cachedCell;
            if (!Grid.IsValidCell(cell))
                cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
            int worldId = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : pickupable.GetMyWorldId();
            bool stored = storage != null || (kpid != null && kpid.HasTag(GameTags.Stored));

            var result = new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(pickupable.GetProperName()),
                ["elementId"] = primary != null ? primary.ElementID.ToString() : null,
                ["massKg"] = primary != null ? (object)Math.Round(SafeFloat(primary.Mass), 3) : null,
                ["units"] = primary != null ? (object)Math.Round(SafeFloat(primary.Units), 3) : null,
                ["cell"] = Grid.IsValidCell(cell) ? (object)cell : null,
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = worldId,
                ["stored"] = stored,
                ["storage"] = storage != null ? StorageInfo(storage) : null
            };

            if (edible != null)
            {
                result["caloriesKcal"] = Math.Round(SafeFloat(edible.Calories) / 1000f, 1);
                result["foodQuality"] = edible.GetQuality();
            }

            AddItemSearchActionMetadata(result, cell, worldId, stored);
            return result.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static Dictionary<string, object> StorageInfo(Storage storage)
        {
            var go = storage?.gameObject;
            if (go == null)
                return null;
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["cell"] = Grid.IsValidCell(cell) ? (object)cell : null,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            }.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static bool ItemMatches(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            return Contains(Value(info, "prefabId"), q)
                || Contains(Value(info, "name"), q)
                || Contains(Value(info, "elementId"), q);
        }

        private static string Value(Dictionary<string, object> info, string key)
        {
            object value;
            return info != null && info.TryGetValue(key, out value) && value != null ? value.ToString() : null;
        }
    }
}
