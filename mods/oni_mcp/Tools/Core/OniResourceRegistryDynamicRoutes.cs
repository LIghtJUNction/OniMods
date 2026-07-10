using System;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static ReadResourceResult ReadDynamicResource(string uri)
        {
            Uri parsed;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsed) || parsed.Scheme != "oni")
                return null;

            ReadResourceResult result;
            result = ReadWorldAndColonyResourceRoutes(uri, parsed);
            if (result != null)
                return result;

            result = ReadBuildingAndSpaceResourceRoutes(uri, parsed);
            if (result != null)
                return result;

            result = ReadDuplicantAndUiResourceRoutes(uri, parsed);
            if (result != null)
                return result;

            return null;
        }
    }
}
