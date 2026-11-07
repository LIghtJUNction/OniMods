using System;
using System.Text;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Appends a multi-species composition summary to HoverTextHelper.MassStringsReadOnly
    /// when a cell contains multiple coexisting species.
    /// </summary>
    [HarmonyPatch(typeof(HoverTextHelper), "MassStringsReadOnly")]
    public static class MulticomponentHoverPatches
    {
        public static string FormatCompositionSuffix(MulticomponentCellSnapshot snapshot)
        {
            if (snapshot.Components == null || snapshot.ComponentCount <= 1 || snapshot.TotalMass <= 0f)
                return string.Empty;

            var builder = new StringBuilder(" [");
            bool first = true;
            for (int i = 0; i < snapshot.ComponentCount; i++)
            {
                MulticomponentSpecies item = snapshot.Components[i];
                if (item.Mass <= 0f || item.Element == null)
                    continue;

                if (!first)
                    builder.Append(", ");
                first = false;

                int pct = (int)Math.Round((item.Mass / snapshot.TotalMass) * 100f);
                string label = !string.IsNullOrEmpty(item.Element.name)
                    ? item.Element.name
                    : item.Element.id.ToString();
                builder.Append(label).Append(' ').Append(pct).Append('%');
            }
            builder.Append(']');
            return first ? string.Empty : builder.ToString();
        }

        [HarmonyPostfix]
        private static void Postfix(int cell, string[] __result)
        {
            if (__result == null || __result.Length < 4)
                return;

            MulticomponentCellSnapshot snapshot;
            if (!MulticomponentCellAccess.TryGetCell(cell, out snapshot))
                return;

            string suffix = FormatCompositionSuffix(snapshot);
            if (!string.IsNullOrEmpty(suffix) && (__result[3] == null || !__result[3].EndsWith(suffix, StringComparison.Ordinal)))
                __result[3] = (__result[3] ?? string.Empty) + suffix;
        }
    }
}
