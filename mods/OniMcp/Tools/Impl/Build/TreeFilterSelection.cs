using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class TreeFilterSelection
    {
        internal static HashSet<Tag> GetOptions(TreeFilterable tree)
        {
            var options = new HashSet<Tag>();
            var flat = tree.GetComponent<FlatTagFilterable>();
            if (flat != null)
                options.UnionWith(flat.tagOptions);
            else
            {
                var categories = tree.GetFilterStorage()?.storageFilters;
                var discovered = DiscoveredResources.Instance;
                if (categories != null && discovered != null)
                {
                    // Native tree rows select discovered resources under this
                    // storage's categories, not arbitrary category/tag names.
                    foreach (var category in categories)
                        options.UnionWith(discovered.GetDiscoveredResourcesFromTag(category));
                }
            }
            options.ExceptWith(tree.ForbiddenTags);
            return options;
        }

        internal static bool TryApply(TreeFilterable tree, List<Tag> requested, string mode, out string error)
        {
            error = null;
            if (mode != "replace" && mode != "add" && mode != "remove" && mode != "clear")
                error = "mode must be replace, add, remove or clear";
            else if (mode != "clear" && requested.Count == 0)
                error = "tags must contain at least one tag unless mode=clear";
            else if (mode == "replace" || mode == "add")
            {
                var options = GetOptions(tree);
                foreach (var tag in requested)
                {
                    if (!options.Contains(tag))
                    {
                        error = "Tag is not selectable for this filter: " + tag.Name;
                        break;
                    }
                }
            }
            if (error != null)
                return false;

            var next = mode == "replace" || mode == "clear"
                ? new HashSet<Tag>() : new HashSet<Tag>(tree.GetTags());
            if (mode == "remove")
                next.ExceptWith(requested);
            else if (mode != "clear")
                next.UnionWith(requested);

            var flat = tree.GetComponent<FlatTagFilterable>();
            if (flat != null)
            {
                next.IntersectWith(flat.tagOptions);
                // SelectTag updates storage immediately. Keep existing selections
                // and add new ones before removing deselected tags so a temporary
                // empty filter cannot eject contents that should remain accepted.
                foreach (var tag in next)
                    if (!flat.selectedTags.Contains(tag))
                        flat.SelectTag(tag, true);
                foreach (var tag in flat.selectedTags.ToList())
                    if (!next.Contains(tag))
                        flat.SelectTag(tag, false);
                next = new HashSet<Tag>(flat.selectedTags);
            }
            tree.UpdateFilters(next);
            return true;
        }
    }
}
