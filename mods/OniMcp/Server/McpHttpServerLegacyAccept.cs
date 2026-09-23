using System;
using System.Globalization;
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
            double bestQuality = 0.0;
            foreach (string rawEntry in accept.Split(','))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0)
                    continue;

                string[] parts = entry.Split(';');
                int specificity = GetJsonMediaRangeSpecificity(parts[0].Trim());
                if (specificity < 0)
                    continue;

                double quality = 1.0;
                bool validQuality = true;
                for (int i = 1; i < parts.Length; i++)
                {
                    string parameter = parts[i].Trim();
                    int separator = parameter.IndexOf('=');
                    if (separator <= 0
                        || !string.Equals(parameter.Substring(0, separator).Trim(), "q", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string rawQuality = parameter.Substring(separator + 1).Trim().Trim('"');
                    double parsedQuality;
                    if (!double.TryParse(rawQuality, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out parsedQuality)
                        || parsedQuality < 0.0 || parsedQuality > 1.0)
                    {
                        validQuality = false;
                        break;
                    }
                    quality = parsedQuality;
                }

                if (!validQuality)
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

            return bestSpecificity >= 0 && bestQuality > 0.0;
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
