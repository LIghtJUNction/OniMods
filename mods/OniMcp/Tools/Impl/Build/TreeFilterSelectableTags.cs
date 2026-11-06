using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class TreeFilterSelectableTags
    {
        internal static HashSet<string> For(GameObject go, TreeFilterable tree)
        {
            var filters = tree?.GetFilterStorage()?.storageFilters;
            var discovered = DiscoveredResources.Instance;
            return TreeFilterEligibility.Selectable(
                filters == null ? null : filters.Select(tag => tag.Name),
                go != null && go.GetComponent<CreatureDeliveryPoint>() != null,
                name => discovered != null && discovered.IsDiscovered(new Tag(name)),
                name => discovered == null
                    ? Enumerable.Empty<string>()
                    : discovered.GetDiscoveredResourcesFromTag(new Tag(name)).Select(tag => tag.Name),
                tree?.ForbiddenTags?.Select(tag => tag.Name));
        }
    }
}
