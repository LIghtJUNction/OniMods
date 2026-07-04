using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static List<McpResourceTemplateInfo> BuildResourceTemplates()
        {
            var templates = new List<McpResourceTemplateInfo>();
            AddResourceTemplatesPart1(templates);
            AddResourceTemplatesPart2(templates);
            AddResourceTemplatesPart3(templates);
            return templates;
        }
    }
}
