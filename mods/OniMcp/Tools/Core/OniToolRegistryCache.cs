using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal sealed class OniToolRegistryCache
    {
        private List<McpTool> allTools;
        private List<McpTool> visibleTools;

        internal List<McpTool> GetTools(IEnumerable<McpTool> registeredTools)
        {
            Ensure(registeredTools);
            return new List<McpTool>(allTools);
        }

        internal List<McpTool> GetVisibleTools(
            IEnumerable<McpTool> registeredTools,
            IEnumerable<McpTool> visibleRegisteredTools)
        {
            Ensure(registeredTools, visibleRegisteredTools);
            return new List<McpTool>(visibleTools.Where(tool => !tool.Hidden));
        }

        internal List<McpTool> GetVisibleSnapshot()
        {
            return visibleTools;
        }

        internal void Clear()
        {
            allTools = null;
            visibleTools = null;
        }

        internal void Ensure(IEnumerable<McpTool> registeredTools)
        {
            Ensure(registeredTools, null);
        }

        private void Ensure(
            IEnumerable<McpTool> registeredTools,
            IEnumerable<McpTool> visibleRegisteredTools)
        {
            if (allTools != null && visibleTools != null)
                return;

            allTools = registeredTools
                .OrderBy(tool => tool.Group)
                .ThenBy(tool => tool.Name)
                .ToList();
            visibleTools = (visibleRegisteredTools ?? allTools.Where(tool => !tool.Hidden))
                .ToList();
        }
    }
}
