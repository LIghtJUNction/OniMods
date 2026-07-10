using System;
using System.Collections.Generic;
using OniMcp.Support;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class InventoryTools
    {
        private static void AddItemSearchActionMetadata(Dictionary<string, object> result, int cell, int worldId, bool stored)
        {
            if (result == null || !Grid.IsValidCell(cell))
                return;

            bool hasNavigators = false;
            bool reachable = false;
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.GetMyWorldId() != worldId)
                    continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator == null)
                    continue;
                hasNavigators = true;
                if (SafeNavigatorCanReach(navigator, cell))
                    reachable = true;
            }

            var element = Grid.Element[cell];
            bool liquidRisk = element != null && element.IsLiquid && Grid.Mass[cell] > 1f;
            bool actionable = !stored && hasNavigators && reachable && !liquidRisk;
            result["hasActiveNavigators"] = hasNavigators;
            result["reachable"] = hasNavigators && reachable;
            result["cellElement"] = element?.id.ToString();
            result["cellMassKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3);
            result["liquidRisk"] = liquidRisk;
            result["actionableAsLooseMaterial"] = actionable;
            result["whyNotActionable"] = actionable ? null : WhyItemNotActionable(stored, hasNavigators, reachable, liquidRisk);

            if (!stored)
            {
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                result["sweepAction"] = new Dictionary<string, object>
                {
                    ["enabled"] = actionable,
                    ["disabledReason"] = actionable ? null : result["whyNotActionable"],
                    ["tool"] = "orders_control",
                    ["arguments"] = new Dictionary<string, object>
                    {
                        ["domain"] = "area",
                        ["action"] = "sweep",
                        ["x1"] = x,
                        ["y1"] = y,
                        ["x2"] = x,
                        ["y2"] = y,
                        ["worldId"] = worldId,
                        ["dryRun"] = true,
                        ["detail"] = true,
                        ["includeStored"] = false
                    }
                };
            }
        }

        private static string WhyItemNotActionable(bool stored, bool hasNavigators, bool reachable, bool liquidRisk)
        {
            var reasons = new List<string>();
            if (stored)
                reasons.Add("stored");
            if (!hasNavigators)
                reasons.Add("no_active_navigator");
            else if (!reachable)
                reasons.Add("unreachable");
            if (liquidRisk)
                reasons.Add("liquidRisk");
            return string.Join(",", reasons.ToArray());
        }

        private static bool SafeNavigatorCanReach(Navigator navigator, int cell)
        {
            try
            {
                return navigator != null && Grid.IsValidCell(cell) && navigator.CanReach(cell);
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, object> ItemSearchInfo(Pickupable pickupable)
        {
            var go = pickupable.gameObject;
            var kpid = pickupable.KPrefabID ?? go.GetComponent<KPrefabID>();
            var primary = pickupable.PrimaryElement ?? go.GetComponent<PrimaryElement>();
            var edible = go.GetComponent<Edible>();
            var storage = pickupable.storage;
            int cell = ToolUtil.PickupableCell(pickupable);
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
                result["isFood"] = true;
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
            if (IsFoodQuery(q) && IsFoodItem(info))
                return true;

            return Contains(Value(info, "prefabId"), q)
                || Contains(Value(info, "name"), q)
                || Contains(Value(info, "elementId"), q);
        }

        private static bool IsFoodQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;
            string q = query.Trim().ToLowerInvariant();
            return q == "food"
                || q == "foods"
                || q == "edible"
                || q == "edibles"
                || q == "harvestable"
                || q == "harvestables"
                || q == "食物"
                || q == "吃的"
                || q == "粮食"
                || q == "可收获";
        }

        private static bool IsFoodItem(Dictionary<string, object> info)
        {
            object value;
            return info != null
                && info.TryGetValue("isFood", out value)
                && value is bool isFood
                && isFood;
        }

        private static string Value(Dictionary<string, object> info, string key)
        {
            object value;
            return info != null && info.TryGetValue(key, out value) && value != null ? value.ToString() : null;
        }
    }
}
