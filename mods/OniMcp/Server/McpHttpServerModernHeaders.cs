using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private const long ModernHeaderMaxSafeInteger = 9007199254740991L;
        private const string ModernParameterHeaderPrefix = "Mcp-Param-";

        private sealed class ModernToolHeaderBinding
        {
            public string HeaderSuffix { get; set; }
            public string HeaderName { get; set; }
            public string Type { get; set; }
            public string[] Path { get; set; }
        }

        private static bool TryGetModernReadOnlyToolInfo(out McpToolInfo toolInfo,
            out List<ModernToolHeaderBinding> bindings)
        {
            toolInfo = OniToolRegistry.GetToolInfos()
                .FirstOrDefault(item => string.Equals(item.Name, ModernReadOnlyToolName, StringComparison.Ordinal));
            if (toolInfo == null)
            {
                bindings = null;
                return false;
            }

            if (!TryBuildModernToolHeaderBindings(toolInfo, out bindings))
            {
                toolInfo = null;
                return false;
            }

            return true;
        }

        private static bool TryBuildModernToolHeaderBindings(McpToolInfo toolInfo,
            out List<ModernToolHeaderBinding> bindings)
        {
            bindings = new List<ModernToolHeaderBinding>();
            var properties = toolInfo?.InputSchema?.Properties;
            if (properties == null || properties.Count == 0)
                return true;

            var seenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return TryCollectModernToolHeaderBindings(properties, new List<string>(), seenHeaders, bindings);
        }

        private static bool TryCollectModernToolHeaderBindings(Dictionary<string, SchemaProperty> properties,
            List<string> parentPath, HashSet<string> seenHeaders, List<ModernToolHeaderBinding> bindings)
        {
            foreach (var pair in properties.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var property = pair.Value;
                if (property == null)
                    continue;

                var path = new List<string>(parentPath) { pair.Key };
                if (property.McpHeader != null)
                {
                    if (!IsValidModernHeaderSuffix(property.McpHeader)
                        || !IsSupportedModernHeaderType(property.Type)
                        || !seenHeaders.Add(property.McpHeader))
                    {
                        return false;
                    }

                    bindings.Add(new ModernToolHeaderBinding
                    {
                        HeaderSuffix = property.McpHeader,
                        HeaderName = ModernParameterHeaderPrefix + property.McpHeader,
                        Type = property.Type,
                        Path = path.ToArray()
                    });
                }

                if (property.Properties != null && property.Properties.Count > 0
                    && !TryCollectModernToolHeaderBindings(property.Properties, path, seenHeaders, bindings))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSupportedModernHeaderType(string type)
        {
            return string.Equals(type, "string", StringComparison.Ordinal)
                || string.Equals(type, "integer", StringComparison.Ordinal)
                || string.Equals(type, "boolean", StringComparison.Ordinal);
        }

        private static bool IsValidModernHeaderSuffix(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (char ch in value)
            {
                if ((ch >= '0' && ch <= '9')
                    || (ch >= 'A' && ch <= 'Z')
                    || (ch >= 'a' && ch <= 'z'))
                {
                    continue;
                }

                switch (ch)
                {
                    case '!':
                    case '#':
                    case '$':
                    case '%':
                    case '&':
                    case '\'':
                    case '*':
                    case '+':
                    case '-':
                    case '.':
                    case '^':
                    case '_':
                    case '`':
                    case '|':
                    case '~':
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool ValidateModernToolParameterHeaders(HttpListenerRequest httpRequest, JObject rawMessage,
            out JsonRpcResponse error)
        {
            error = null;
            var @params = rawMessage["params"] as JObject;
            string toolName = @params?["name"]?.Type == JTokenType.String ? (string)@params["name"] : null;
            if (!string.Equals(toolName, ModernReadOnlyToolName, StringComparison.Ordinal))
                return true;

            McpToolInfo toolInfo;
            List<ModernToolHeaderBinding> bindings;
            if (!TryGetModernReadOnlyToolInfo(out toolInfo, out bindings))
                return true;

            var arguments = @params["arguments"] as JObject;
            foreach (var binding in bindings)
            {
                JToken bodyValue;
                bool hasBodyValue = TryGetModernArgumentValue(arguments, binding.Path, out bodyValue)
                    && bodyValue != null && bodyValue.Type != JTokenType.Null && bodyValue.Type != JTokenType.Undefined;
                string headerValue = httpRequest.Headers[binding.HeaderName];

                if (!hasBodyValue)
                {
                    if (headerValue != null)
                    {
                        error = HeaderMismatch(rawMessage["id"],
                            $"{binding.HeaderName} must be omitted when its tool argument is null or absent");
                        return false;
                    }
                    continue;
                }

                if (headerValue == null)
                {
                    error = HeaderMismatch(rawMessage["id"],
                        $"Missing {binding.HeaderName} for mirrored tool argument '{string.Join(".", binding.Path)}'");
                    return false;
                }

                bool encoded = headerValue.StartsWith(Base64HeaderPrefix, StringComparison.Ordinal)
                    && headerValue.EndsWith(Base64HeaderSuffix, StringComparison.Ordinal);
                if (!encoded && !IsSafePlainModernHeaderValue(headerValue))
                {
                    error = HeaderMismatch(rawMessage["id"],
                        $"{binding.HeaderName} contains a value that must use MCP Base64 header encoding");
                    return false;
                }

                string decodedValue;
                if (!TryDecodeModernHeaderValue(headerValue, out decodedValue))
                {
                    error = HeaderMismatch(rawMessage["id"],
                        $"{binding.HeaderName} contains invalid Base64 or UTF-8 encoding");
                    return false;
                }

                if (!ModernHeaderValueMatchesBody(binding.Type, decodedValue, bodyValue))
                {
                    error = HeaderMismatch(rawMessage["id"],
                        $"{binding.HeaderName} must match tool argument '{string.Join(".", binding.Path)}'");
                    return false;
                }
            }

            return true;
        }

        private static bool TryGetModernArgumentValue(JObject arguments, string[] path, out JToken value)
        {
            value = arguments;
            if (arguments == null)
                return false;

            JToken current = arguments;
            foreach (var segment in path)
            {
                var currentObject = current as JObject;
                if (currentObject == null || !currentObject.TryGetValue(segment, StringComparison.Ordinal, out current))
                {
                    value = null;
                    return false;
                }
            }

            value = current;
            return true;
        }

        private static bool ModernHeaderValueMatchesBody(string type, string headerValue, JToken bodyValue)
        {
            if (string.Equals(type, "string", StringComparison.Ordinal))
                return bodyValue.Type == JTokenType.String
                    && string.Equals(headerValue, (string)bodyValue, StringComparison.Ordinal);

            if (string.Equals(type, "boolean", StringComparison.Ordinal))
            {
                if (bodyValue.Type != JTokenType.Boolean)
                    return false;
                string expected = (bool)bodyValue ? "true" : "false";
                return string.Equals(headerValue, expected, StringComparison.Ordinal);
            }

            if (!string.Equals(type, "integer", StringComparison.Ordinal)
                || (bodyValue.Type != JTokenType.Integer && bodyValue.Type != JTokenType.Float))
            {
                return false;
            }

            decimal bodyNumber;
            decimal headerNumber;
            if (!decimal.TryParse(bodyValue.ToString(Formatting.None), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out bodyNumber)
                || !decimal.TryParse(headerValue, NumberStyles.Float, CultureInfo.InvariantCulture, out headerNumber))
            {
                return false;
            }

            if (decimal.Truncate(bodyNumber) != bodyNumber || decimal.Truncate(headerNumber) != headerNumber)
                return false;
            if (bodyNumber < -ModernHeaderMaxSafeInteger || bodyNumber > ModernHeaderMaxSafeInteger
                || headerNumber < -ModernHeaderMaxSafeInteger || headerNumber > ModernHeaderMaxSafeInteger)
            {
                return false;
            }

            return bodyNumber == headerNumber;
        }

        private static bool IsSafePlainModernHeaderValue(string value)
        {
            if (string.IsNullOrEmpty(value) || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]))
                return false;

            foreach (char ch in value)
            {
                if (ch < 0x20 || ch > 0x7e || ch == 0x7f)
                    return false;
            }
            return true;
        }

        private static string BuildModernCorsAllowedHeaders()
        {
            var headers = new List<string>
            {
                "Content-Type",
                "Mcp-Session-Id",
                "Mcp-Protocol-Version",
                "Mcp-Method",
                "Mcp-Name",
                "Accept",
                "Authorization",
                "X-Oni-Mcp-Token"
            };

            McpToolInfo toolInfo;
            List<ModernToolHeaderBinding> bindings;
            if (TryGetModernReadOnlyToolInfo(out toolInfo, out bindings))
            {
                headers.AddRange(bindings.Select(binding => binding.HeaderName));
            }

            return string.Join(", ", headers.Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }
}
