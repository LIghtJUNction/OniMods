using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal sealed class OniToolRegistryCache
    {
        private List<McpTool> allTools;

        internal List<McpTool> GetTools(IEnumerable<McpTool> registeredTools)
        {
            Ensure(registeredTools);
            return new List<McpTool>(allTools);
        }

        internal List<McpTool> GetVisibleTools(IEnumerable<McpTool> registeredTools)
        {
            Ensure(registeredTools);
            return Sort(registeredTools.Where(tool => !tool.Hidden)).ToList();
        }

        internal void Clear()
        {
            allTools = null;
        }

        private void Ensure(IEnumerable<McpTool> registeredTools)
        {
            if (allTools != null)
                return;

            allTools = Sort(registeredTools).ToList();
        }

        private static IOrderedEnumerable<McpTool> Sort(IEnumerable<McpTool> tools)
        {
            return tools.OrderBy(tool => tool.Group, StringComparer.Ordinal)
                .ThenBy(tool => tool.Name, StringComparer.Ordinal);
        }
    }
}
