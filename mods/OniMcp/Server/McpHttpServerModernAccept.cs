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
                if (!string.Equals(segments[0].Trim(), mediaType, StringComparison.OrdinalIgnoreCase))
                    continue;

                decimal quality;
                if (TryParseAcceptParameters(segments, out quality) && quality > 0m)
                    return true;
            }

            return false;
        }
    }
}
