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
            if (constructable != null)
            {
                // The first selected element is the primary construction material.
                // An unknown primary must not borrow another slot or a prefab default.
                var selected = constructable.SelectedElementsTags;
                return selected != null && selected.Count > 0 && selected[0].IsValid
                    ? selected[0].Name
                    : null;
            }

            // Reconstructable stores a future replacement, not the current material.
            var primary = go.GetComponent<PrimaryElement>();
            return primary == null ? null : primary.ElementID.ToString();
        }
    }
}
