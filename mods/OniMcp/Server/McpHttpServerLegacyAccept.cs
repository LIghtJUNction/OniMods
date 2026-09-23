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

            foreach (string rawEntry in accept.Split(','))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0)
                    continue;

                string[] parts = entry.Split(';');
                string mediaRange = parts[0].Trim();
                if (!MatchesJsonMediaRange(mediaRange))
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

                if (validQuality && quality > 0.0)
                    return true;
            }

            return false;
        }

        private static bool MatchesJsonMediaRange(string mediaRange)
        {
            return string.Equals(mediaRange, "application/json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaRange, "application/*", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaRange, "*/*", StringComparison.OrdinalIgnoreCase);
        }
    }
}
