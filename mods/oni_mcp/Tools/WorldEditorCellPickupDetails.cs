using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
private static void AppendCellPickupDetailSnapshot(StringBuilder sb, int cell, JObject args)
{
int limit = CellPickupItemLimit(args);
var entries = new List<CellPickupDetail>();
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                if (Grid.PosToCell(pickupable.gameObject) != cell)
                    continue;

                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                var prefab = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                int objectCell = Grid.PosToCell(pickupable.gameObject);
                bool stored = pickupable.storage != null
                    || (pickupable.KPrefabID != null && pickupable.KPrefabID.HasTag(GameTags.Stored));
                Vector3 pos = pickupable.gameObject.transform.position;
                entries.Add(new CellPickupDetail
                {
                    Name = StripLinkTags(pickupable.GetProperName()),
                    PrefabId = prefab != null && prefab.PrefabTag.IsValid ? prefab.PrefabTag.Name : pickupable.gameObject.name,
                    ElementId = primary != null ? primary.ElementID.ToString() : "?",
                    MassKg = primary != null ? primary.Mass : 0f,
                    Stored = stored,
                    CachedCell = pickupable.cachedCell,
                    ObjectCell = objectCell,
                    X = pos.x,
                    Y = pos.y
                });
            }

            if (entries.Count == 0)
                return;

            sb.AppendLine();
sb.AppendLine("## Pickup Summary");
            sb.AppendLine("- 掉落物: " + entries.Count + " 项, " + entries.Sum(item => item.MassKg).ToString("F2") + " kg"
                + ", loose=" + entries.Count(item => !item.Stored)
                + ", stored=" + entries.Count(item => item.Stored));
sb.AppendLine("- 明细: shown=" + Math.Min(entries.Count, limit)
+ ", truncated=" + (entries.Count > limit).ToString().ToLowerInvariant()
+ ", itemLimit=" + limit);
sb.AppendLine("- 聚合:");
            foreach (var group in entries
                         .GroupBy(item => item.PrefabId + "|" + item.ElementId + "|" + item.Name)
                         .OrderByDescending(group => group.Sum(item => item.MassKg))
.Take(limit))
            {
                CellPickupDetail first = group.First();
                sb.AppendLine("  - " + first.Name
                    + " (ID=" + first.PrefabId
                    + ", 元素=" + first.ElementId
                    + "): count=" + group.Count()
                    + ", mass=" + group.Sum(item => item.MassKg).ToString("F2") + " kg");
            }

sb.AppendLine("- 明细:");
foreach (CellPickupDetail item in entries.OrderByDescending(item => item.MassKg).Take(limit))
            {
                sb.AppendLine("  - " + item.Name
                    + " | ID=" + item.PrefabId
                    + " | 元素=" + item.ElementId
                    + " | " + item.MassKg.ToString("F2") + " kg"
                    + " | state=" + (item.Stored ? "stored" : "loose")
                    + " | cachedCell=" + item.CachedCell
                    + " | objectCell=" + item.ObjectCell
                    + " | pos=(" + item.X.ToString("F2") + "," + item.Y.ToString("F2") + ")");
            }
        }

        static int CellPickupItemLimit(JObject args)
        {
            if (ToolUtil.GetBool(args, "includeAllItems", false) || ToolUtil.GetBool(args, "fullItems", false))
                return 1000;
            return Math.Max(1, Math.Min(ToolUtil.GetInt(args, "itemLimit") ?? 8, 1000));
        }

        private sealed class CellPickupDetail
        {
            public string Name;
            public string PrefabId;
            public string ElementId;
            public float MassKg;
            public bool Stored;
            public int CachedCell;
            public int ObjectCell;
            public float X;
            public float Y;
        }
    }
}
