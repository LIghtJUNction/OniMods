using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static string ExistingConstructionMaterialTag(GameObject go)
        {
            if (go == null)
                return null;

            var constructable = go.GetComponent<Constructable>();
            var selected = constructable?.SelectedElementsTags;
            if (selected != null)
            {
                foreach (var tag in selected)
                {
                    if (tag.IsValid)
                        return tag.Name;
                }
            }

            var primary = go.GetComponent<PrimaryElement>();
            return primary == null ? null : primary.ElementID.ToString();
        }
    }
}
