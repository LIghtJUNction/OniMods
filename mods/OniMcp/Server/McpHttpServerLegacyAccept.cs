using System;
using System.Net;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool AcceptsLegacyJsonResponse(HttpListenerRequest request)
        {
            string accept = request?.Headers["Accept"];
            if (string.IsNullOrWhiteSpace(accept))
                return true;

            int bestSpecificity = -1;
            decimal bestQuality = 0m;
            foreach (string rawEntry in accept.Split(','))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0)
                    continue;

                string[] parts = entry.Split(';');
                int specificity = GetJsonMediaRangeSpecificity(parts[0].Trim());
                if (specificity < 0)
                    continue;

                decimal quality = 1m;
                bool qualitySeen = false;
                bool validQuality = true;
                bool mediaParametersMatch = true;
                for (int i = 1; i < parts.Length; i++)
                {
                    string parameter = parts[i].Trim();
                    int separator = parameter.IndexOf('=');
                    if (separator <= 0
                        || !string.Equals(parameter.Substring(0, separator).Trim(), "q", StringComparison.OrdinalIgnoreCase))
                    {
                        mediaParametersMatch = false;
                        continue;
                    }

                    if (qualitySeen
                        || !TryParseHttpQualityValue(parameter.Substring(separator + 1), out quality))
                    {
                        validQuality = false;
                        break;
                    }
                    qualitySeen = true;
                }

                if (!validQuality || !mediaParametersMatch)
                    continue;

                if (specificity > bestSpecificity)
                {
                    bestSpecificity = specificity;
                    bestQuality = quality;
                }
                else if (specificity == bestSpecificity && quality > bestQuality)
                {
                    bestQuality = quality;
                }
            }

            return bestSpecificity >= 0 && bestQuality > 0m;
        }

        private static int GetJsonMediaRangeSpecificity(string mediaRange)
        {
            if (string.Equals(mediaRange, "application/json", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (string.Equals(mediaRange, "application/*", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(mediaRange, "*/*", StringComparison.OrdinalIgnoreCase))
                return 0;
            return -1;
        }
    }
}
