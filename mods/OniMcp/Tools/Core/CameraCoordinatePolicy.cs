using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class CameraCoordinatePolicy
    {
        internal static bool ExposesParameter(string tool, string parameter)
        {
            if (tool != "navigation_control")
                return false;
            return parameter == "x" || parameter == "y" || parameter == "dx" || parameter == "dy"
                || parameter == "x1" || parameter == "y1" || parameter == "x2" || parameter == "y2";
        }

        internal static bool AllowsArguments(string tool, JObject arguments)
        {
            if (tool != "navigation_control" || arguments == null)
                return false;
            string domain = Text(arguments, "domain");
            if (domain != "" && domain != "camera" && domain != "view" && domain != "screenshot")
                return false;

            string action = Text(arguments, "action");
            string mode = Text(arguments, "mode");
            foreach (var property in arguments.Properties())
            {
                if (OniToolRegistry.HasCoordinateArguments(property.Value))
                    return false;
                if (!OniToolRegistry.IsCoordinateParameter(property.Name))
                    continue;
                bool point = property.Name == "x" || property.Name == "y";
                bool delta = property.Name == "dx" || property.Name == "dy";
                bool rectangle = property.Name == "x1" || property.Name == "y1"
                    || property.Name == "x2" || property.Name == "y2";
                bool allowed = (point && (action == "set_view" || action == "focus_cell"
                        || (action == "move" && mode == "jump")))
                    || (delta && action == "move" && mode == "pan")
                    || (rectangle && (action == "coordinate_screenshot" || action == "coord_screenshot"));
                if (!allowed || (property.Value.Type != JTokenType.Integer && property.Value.Type != JTokenType.Float))
                    return false;
            }
            return true;
        }

        private static string Text(JObject args, string key)
        {
            return (args[key]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
