using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static List<McpResourceTemplateInfo> BuildResourceTemplates()
        {
            var templates = new List<McpResourceTemplateInfo>();
            AddWorldAndColonyResourceTemplates(templates);
            AddBuildingAndSpaceResourceTemplates(templates);
            AddDuplicantAndUiResourceTemplates(templates);
            return templates;
        }
    }
}
