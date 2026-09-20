using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool ValidateModernClientInfo(JToken clientInfo, out string errorMessage)
        {
            errorMessage = null;
            if (clientInfo == null)
                return true;

            var clientInfoObject = clientInfo as JObject;
            if (clientInfoObject == null
                || clientInfoObject["name"]?.Type != JTokenType.String
                || clientInfoObject["version"]?.Type != JTokenType.String)
            {
                errorMessage =
                    "params._meta.io.modelcontextprotocol/clientInfo must contain string name and version when provided";
                return false;
            }

            var iconsToken = clientInfoObject["icons"];
            if (iconsToken == null)
                return true;

            var icons = iconsToken as JArray;
            if (icons == null)
            {
                errorMessage =
                    "params._meta.io.modelcontextprotocol/clientInfo.icons must be an array when provided";
                return false;
            }

            for (int i = 0; i < icons.Count; i++)
            {
                var icon = icons[i] as JObject;
                if (icon == null)
                {
                    errorMessage =
                        $"params._meta.io.modelcontextprotocol/clientInfo.icons[{i}] must be an object";
                    return false;
                }

                var src = icon["src"];
                Uri parsedSrc;
                if (src?.Type != JTokenType.String
                    || !Uri.TryCreate((string)src, UriKind.Absolute, out parsedSrc))
                {
                    errorMessage =
                        $"params._meta.io.modelcontextprotocol/clientInfo.icons[{i}].src must be an absolute URI string";
                    return false;
                }

                var mimeType = icon["mimeType"];
                if (mimeType != null && mimeType.Type != JTokenType.String)
                {
                    errorMessage =
                        $"params._meta.io.modelcontextprotocol/clientInfo.icons[{i}].mimeType must be a string when provided";
                    return false;
                }

                var sizesToken = icon["sizes"];
                if (sizesToken != null)
                {
                    var sizes = sizesToken as JArray;
                    if (sizes == null)
                    {
                        errorMessage =
                            $"params._meta.io.modelcontextprotocol/clientInfo.icons[{i}].sizes must be an array when provided";
                        return false;
                    }

                    foreach (var size in sizes)
                    {
                        if (size?.Type == JTokenType.String)
                            continue;

                        errorMessage =
                            $"params._meta.io.modelcontextprotocol/clientInfo.icons[{i}].sizes entries must be strings";
                        return false;
                    }
                }

                var theme = icon["theme"];
                if (theme != null
                    && (theme.Type != JTokenType.String
                        || (!string.Equals((string)theme, "light", StringComparison.Ordinal)
                            && !string.Equals((string)theme, "dark", StringComparison.Ordinal))))
                {
                    errorMessage =
                        $"params._meta.io.modelcontextprotocol/clientInfo.icons[{i}].theme must be 'light' or 'dark' when provided";
                    return false;
                }
            }

            return true;
        }
    }
}
