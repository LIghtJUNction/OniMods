using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendCellPickupDetailSnapshot(StringBuilder sb, int cell)
        {
            var entries = new List<CellPickupDetail>();
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                if (Grid.PosToCell(pickupable.gameObject) != cell)
                    continue;

                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                var prefab = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                entries.Add(new CellPickupDetail
                {
                    Name = StripLinkTags(pickupable.GetProperName()),
                    PrefabId = prefab != null && prefab.PrefabTag.IsValid ? prefab.PrefabTag.Name : pickupable.gameObject.name,
                    ElementId = primary != null ? primary.ElementID.ToString() : "?",
                    MassKg = primary != null ? primary.Mass : 0f
                });
            }

            if (entries.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine("## Pickup Summary");
            sb.AppendLine("- 掉落物: " + entries.Count + " 项, " + entries.Sum(item => item.MassKg).ToString("F2") + " kg");
            sb.AppendLine("- 聚合:");
            foreach (var group in entries
                         .GroupBy(item => item.PrefabId + "|" + item.ElementId + "|" + item.Name)
                         .OrderByDescending(group => group.Sum(item => item.MassKg))
                         .Take(8))
            {
                CellPickupDetail first = group.First();
                sb.AppendLine("  - " + first.Name
                    + " (ID=" + first.PrefabId
                    + ", 元素=" + first.ElementId
                    + "): count=" + group.Count()
                    + ", mass=" + group.Sum(item => item.MassKg).ToString("F2") + " kg");
            }

            sb.AppendLine("- 明细(前8):");
            foreach (CellPickupDetail item in entries.OrderByDescending(item => item.MassKg).Take(8))
            {
                sb.AppendLine("  - " + item.Name
                    + " | ID=" + item.PrefabId
                    + " | 元素=" + item.ElementId
                    + " | " + item.MassKg.ToString("F2") + " kg");
            }
        }

        private sealed class CellPickupDetail
        {
            public string Name;
            public string PrefabId;
            public string ElementId;
            public float MassKg;
        }
    }
}
