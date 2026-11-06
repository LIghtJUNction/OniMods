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
            {
                // Match the native side screen's visibility and editability.
                if (!flat.currentlyUserAssignable)
                    return options;
                var discovered = DiscoveredResources.Instance;
                options.UnionWith(flat.tagOptions.Where(tag => !flat.displayOnlyDiscoveredTags
                    || (discovered != null && discovered.IsDiscovered(tag))));
            }
            else
            {
                // Preserve native category rows, discovered children and the
                // CreatureDeliveryPoint exception already used by tree writes.
                options.UnionWith(TreeFilterSelectableTags.For(tree.gameObject, tree)
                    .Select(name => new Tag(name)));
            }
            options.ExceptWith(tree.ForbiddenTags);
            return options;
        }

        internal static bool TryApply(TreeFilterable tree, List<Tag> requested, string mode, out string error)
        {
            error = null;
            var flat = tree.GetComponent<FlatTagFilterable>();
            if (flat != null && !flat.currentlyUserAssignable)
            {
                error = "This flat filter is not currently user assignable";
                return false;
            }
            HashSet<string> selected;
            if (!TreeFilterEligibility.TryApply(mode,
                tree.GetTags().Select(tag => tag.Name),
                requested.Select(tag => tag.Name),
                new HashSet<string>(GetOptions(tree).Select(tag => tag.Name)),
                out selected, out error))
                return false;

            if (flat != null)
            {
                // SelectTag updates storage immediately. Keep existing selections
                // and add new ones before removing deselected tags so a temporary
                // empty filter cannot eject contents that should remain accepted.
                // Saved tags that no longer occur in tagOptions must not be sent
                // to SelectTag: the native method asserts that they are options.
                var plan = TreeFilterEligibility.PlanFlatUpdates(
                    flat.selectedTags.Select(tag => tag.Name), selected,
                    flat.tagOptions.Select(tag => tag.Name));
                foreach (var edit in plan)
                    flat.SelectTag(flat.tagOptions.First(tag => tag.Name == edit.Key), edit.Value);
                tree.UpdateFilters(new HashSet<Tag>(flat.selectedTags));
            }
            else
                tree.UpdateFilters(new HashSet<Tag>(selected.Select(name => new Tag(name))));
            return true;
        }
    }
}
