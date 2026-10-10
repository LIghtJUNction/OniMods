using System;

namespace OniMcp.Tools
{
    internal static class BuildPlanningExistingMaterialPolicy
    {
        internal static bool IsExplicitRequest(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return false;

            string value = requested.Trim();
            return !value.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("default", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool RequestMatchesExisting(
            string requested,
            string existingTag,
            string existingName,
            bool requestedCategoryMatchesExisting)
        {
            if (!IsExplicitRequest(requested))
                return true;
            if (string.IsNullOrWhiteSpace(existingTag))
                return false;

            string value = requested.Trim();
            return value.Equals(existingTag, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(existingName)
                    && value.Equals(existingName, StringComparison.OrdinalIgnoreCase))
                || requestedCategoryMatchesExisting;
        }

        internal static bool SelectedMaterialMatchesExisting(string selectedTag, string existingTag)
        {
            return !string.IsNullOrWhiteSpace(selectedTag)
                && !string.IsNullOrWhiteSpace(existingTag)
                && selectedTag.Equals(existingTag, StringComparison.OrdinalIgnoreCase);
        }
    }
}
