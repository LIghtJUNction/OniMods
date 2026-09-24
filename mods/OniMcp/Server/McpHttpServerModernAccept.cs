using System;
using System.Net;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool AcceptsModernResponseMediaTypes(HttpListenerRequest httpRequest)
        {
            string acceptHeader = httpRequest.Headers["Accept"];
            return AcceptsModernMediaType(acceptHeader, "application/json")
                && AcceptsModernMediaType(acceptHeader, "text/event-stream");
        }

        private static bool AcceptsModernMediaType(string acceptHeader, string mediaType)
        {
            if (string.IsNullOrWhiteSpace(acceptHeader))
                return false;

            foreach (string rawEntry in acceptHeader.Split(','))
            {
                string[] segments = rawEntry.Split(';');
                if (segments.Length == 0
                    || !string.Equals(segments[0].Trim(), mediaType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                decimal quality = 1m;
                bool qualitySeen = false;
                bool valid = true;
                bool mediaParametersMatch = true;
                for (int index = 1; index < segments.Length; index++)
                {
                    string parameter = segments[index].Trim();
                    int equals = parameter.IndexOf('=');
                    if (equals <= 0
                        || !string.Equals(parameter.Substring(0, equals).Trim(), "q",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        mediaParametersMatch = false;
                        continue;
                    }

                    if (qualitySeen
                        || !TryParseHttpQualityValue(parameter.Substring(equals + 1), out quality))
                    {
                        valid = false;
                        break;
                    }
                    qualitySeen = true;
                }

                if (valid && mediaParametersMatch && quality > 0m)
                    return true;
            }

            return false;
        }
    }
}
