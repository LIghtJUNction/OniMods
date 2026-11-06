using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Pure selection rules for TreeFilterable writes.
    // Source contract: Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636
    // TreeFilterableSideScreen.CreateCategories/AddRow and TreeFilterable.UpdateFilters.
    // This does not execute game code and does not prove a live colony will keep its contents.
    internal static class TreeFilterEligibility
    {
        internal static HashSet<string> Selectable(
            IEnumerable<string> storageFilters,
            bool acceptUndiscoveredCategories,
            Func<string, bool> isDiscovered,
            Func<string, IEnumerable<string>> discoveredChildren,
            IEnumerable<string> forbidden)
        {
            var blocked = new HashSet<string>(
                forbidden ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            var selectable = new HashSet<string>(StringComparer.Ordinal);
            if (storageFilters == null)
                return selectable;

            foreach (var category in storageFilters)
            {
                if (string.IsNullOrEmpty(category))
                    continue;
                if (!acceptUndiscoveredCategories
                    && (isDiscovered == null || !isDiscovered(category)))
                    continue;
                if (!blocked.Contains(category))
                    selectable.Add(category);
                if (discoveredChildren == null)
                    continue;
                foreach (var child in discoveredChildren(category) ?? Enumerable.Empty<string>())
                {
                    if (!string.IsNullOrEmpty(child) && !blocked.Contains(child))
                        selectable.Add(child);
                }
            }

            return selectable;
        }

        internal static bool TryApply(
            string mode,
            IEnumerable<string> current,
            IEnumerable<string> requested,
            ISet<string> selectable,
            out HashSet<string> next,
            out string error)
        {
            next = null;
            error = null;
            var normalized = string.IsNullOrWhiteSpace(mode)
                ? "replace"
                : mode.Trim().ToLowerInvariant();
            if (normalized != "replace" && normalized != "add"
                && normalized != "remove" && normalized != "clear")
            {
                error = "mode must be replace, add, remove, or clear";
                return false;
            }

            var asked = (requested ?? Enumerable.Empty<string>())
                .Where(tag => !string.IsNullOrEmpty(tag))
                .ToList();
            if ((normalized == "add" || normalized == "replace") && asked.Count == 0)
            {
                error = "tags must contain at least one tag unless mode=clear";
                return false;
            }

            if (normalized == "add" || normalized == "replace")
            {
                var rejected = asked
                    .Where(tag => selectable == null || !selectable.Contains(tag))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (rejected.Count > 0)
                {
                    error = "tree filter tag is not selectable: " + string.Join(", ", rejected);
                    return false;
                }
            }

            var selected = new HashSet<string>(
                current ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            next = normalized == "replace" || normalized == "clear"
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(selected, StringComparer.Ordinal);
            if (normalized == "remove")
            {
                foreach (var tag in asked)
                    next.Remove(tag);
            }
            else if (normalized != "clear")
            {
                foreach (var tag in asked)
                    next.Add(tag);
            }

            return true;
        }

        // SelectTag writes the accepted set on every call. Add retained tags
        // before removing the others so an intermediate update is never empty
        // solely because the old selection was cleared first.
        internal static List<KeyValuePair<string, bool>> PlanFlatUpdates(
            IEnumerable<string> current,
            IEnumerable<string> desired,
            IEnumerable<string> options)
        {
            var optionSet = new HashSet<string>(
                options ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            var currentSet = new HashSet<string>(
                current ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            var desiredSet = new HashSet<string>(
                desired ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            var plan = new List<KeyValuePair<string, bool>>();
            foreach (var tag in desiredSet.Where(optionSet.Contains).Except(currentSet).OrderBy(tag => tag, StringComparer.Ordinal))
                plan.Add(new KeyValuePair<string, bool>(tag, true));
            foreach (var tag in currentSet.Where(optionSet.Contains).Except(desiredSet).OrderBy(tag => tag, StringComparer.Ordinal))
                plan.Add(new KeyValuePair<string, bool>(tag, false));
            return plan;
        }
    }
}
