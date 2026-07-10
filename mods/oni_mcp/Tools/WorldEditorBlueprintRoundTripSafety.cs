using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool ValidateBlueprintMarkdownRoundTrip(JObject original, string currentMarkdown, out string error)
        {
            error = null;
            var buildingItems = (original["buildings"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            var digItems = (original["digcommands"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            if (buildingItems.Any(HasNegativeBlueprintOffset)
                || digItems.Any(item => (item.Value<int?>("x") ?? 0) < 0 || (item.Value<int?>("y") ?? 0) < 0))
            {
                error = "Blueprint contains negative coordinates that Markdown cannot represent losslessly. Edit the raw .blueprint JSON instead.";
                return false;
            }

            bool duplicateTokenAtCell = buildingItems
                .GroupBy(item => (item.SelectToken("offset.x")?.Value<int>() ?? 0) + ","
                    + (item.SelectToken("offset.y")?.Value<int>() ?? 0) + ":" + BlueprintToken(item, null))
                .Any(group => group.Count() > 1);
            if (duplicateTokenAtCell)
            {
                error = "Blueprint contains duplicate instances with the same token at one cell. Edit the raw .blueprint JSON instead.";
                return false;
            }

            JObject roundTrip = MarkdownToBlueprint(original, currentMarkdown, out string roundTripError);
            if (roundTrip == null || !JToken.DeepEquals(original, roundTrip))
            {
                error = "Blueprint contains instance-specific or ordering data that Markdown cannot preserve losslessly. Edit the raw .blueprint JSON instead."
                    + (string.IsNullOrWhiteSpace(roundTripError) ? string.Empty : " " + roundTripError);
                return false;
            }
            return true;
        }

        private static bool HasNegativeBlueprintOffset(JObject item)
        {
            return (item.SelectToken("offset.x")?.Value<int>() ?? 0) < 0
                || (item.SelectToken("offset.y")?.Value<int>() ?? 0) < 0;
        }
    }
}
