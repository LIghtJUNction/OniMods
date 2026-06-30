using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class SurfaceAuditUtil
    {
        private const string GenericReadResourceName = "tools_read_resource";

        public static List<string> MissingTools(IEnumerable<string> tools, HashSet<string> toolNames)
        {
            if (tools == null)
                return new List<string>();

            return tools
                .Where(tool => !string.IsNullOrWhiteSpace(tool) && (toolNames == null || !toolNames.Contains(ToolNameOnly(tool))))
                .ToList();
        }

        public static string ToolNameOnly(string tool)
        {
            if (string.IsNullOrWhiteSpace(tool))
                return string.Empty;

            var trimmed = tool.Trim();
            int space = trimmed.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            return space < 0 ? trimmed : trimmed.Substring(0, space);
        }

        public static List<string> MissingResources(IEnumerable<string> resources, HashSet<string> resourceNames)
        {
            if (resources == null)
                return new List<string>();

            return resources
                .Where(resource => !IsResourceCovered(resource, resourceNames))
                .ToList();
        }

        private static bool IsResourceCovered(string resource, HashSet<string> resourceNames)
        {
            if (string.IsNullOrWhiteSpace(resource))
                return true;

            if (resourceNames != null && resourceNames.Contains(resource))
                return true;

            if (resourceNames == null || !resourceNames.Contains(GenericReadResourceName))
                return false;

            McpTool tool;
            return OniToolRegistry.TryGetTool(resource, out tool)
                   && string.Equals(tool.Mode, "read", StringComparison.OrdinalIgnoreCase);
        }
    }
}
