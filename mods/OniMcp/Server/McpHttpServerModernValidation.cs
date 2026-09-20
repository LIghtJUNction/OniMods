using System;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool ValidateModernRequest(HttpListenerRequest httpRequest, JObject rawMessage, string method,
            string protocolVersion, JObject meta, string metaVersion, out JsonRpcResponse error)
        {
            error = null;
            if (rawMessage.Property("id") != null && !IsValidModernRequestId(rawMessage["id"]))
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest,
                    "Modern request id must be a string or integer");
                return false;
            }

            // Notifications use NotificationParams / NotificationMetaObject, not RequestParams.
            // The 2026 HTTP standard-header presence requirements are request-only; a notification
            // can be routed by either the protocol header or an explicit body protocol claim.
            if (rawMessage.Property("id") == null)
                return ValidateModernNotification(httpRequest, rawMessage, method, protocolVersion, meta, metaVersion,
                    out error);

            if (!string.Equals(protocolVersion, ModernProtocolVersion, StringComparison.Ordinal))
            {
                error = HeaderMismatch(rawMessage["id"],
                    $"Mcp-Protocol-Version must be {ModernProtocolVersion} for a modern request");
                return false;
            }
            if (string.IsNullOrEmpty(metaVersion))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "Modern requests require params._meta.io.modelcontextprotocol/protocolVersion");
                return false;
            }

            if (!string.Equals(metaVersion, protocolVersion, StringComparison.Ordinal))
            {
                error = HeaderMismatch(rawMessage["id"],
                    $"Mcp-Protocol-Version '{protocolVersion}' must match params._meta.io.modelcontextprotocol/protocolVersion '{metaVersion}'");
                return false;
            }

            string metaKeyError;
            if (!ValidateModernMetaKeys(meta, out metaKeyError))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams, metaKeyError);
                return false;
            }

            var clientCapabilities = meta?["io.modelcontextprotocol/clientCapabilities"] as JObject;
            if (clientCapabilities == null)
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "Modern requests require params._meta.io.modelcontextprotocol/clientCapabilities");
                return false;
            }

            string capabilityError;
            if (!ValidateModernClientCapabilities(clientCapabilities, out capabilityError))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams, capabilityError);
                return false;
            }

            var progressTokenProperty = meta.Property("progressToken");
            if (progressTokenProperty != null && !IsValidModernProgressToken(progressTokenProperty.Value))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "params._meta.progressToken must be a string or number when provided");
                return false;
            }

            var traceparentProperty = meta.Property("traceparent");
            if (traceparentProperty != null && !IsValidModernTraceparent(traceparentProperty.Value))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "params._meta.traceparent must be a valid W3C Trace Context value when provided");
                return false;
            }

            var tracestateProperty = meta.Property("tracestate");
            if (tracestateProperty != null && !IsValidModernTracestate(tracestateProperty.Value))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "params._meta.tracestate must be a valid W3C Trace Context value when provided");
                return false;
            }

            var baggageProperty = meta.Property("baggage");
            if (baggageProperty != null && !IsValidModernBaggage(baggageProperty.Value))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "params._meta.baggage must be a valid W3C Baggage value when provided");
                return false;
            }

            string clientInfoError;
            if (!ValidateModernClientInfo(meta["io.modelcontextprotocol/clientInfo"], out clientInfoError))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams, clientInfoError);
                return false;
            }

            var logLevel = meta["io.modelcontextprotocol/logLevel"];
            if (logLevel != null && !IsValidModernLogLevel(logLevel))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    "params._meta.io.modelcontextprotocol/logLevel must be a valid MCP logging level when provided");
                return false;
            }

            string inputResponseError;
            if (!ValidateModernInputResponseRequestParams(method, rawMessage["params"] as JObject,
                    out inputResponseError))
            {
                error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                    inputResponseError);
                return false;
            }

            string methodHeader = httpRequest.Headers["Mcp-Method"];
            if (string.IsNullOrEmpty(methodHeader) || !string.Equals(methodHeader, method, StringComparison.Ordinal))
            {
                error = HeaderMismatch(rawMessage["id"],
                    $"Mcp-Method header must match JSON-RPC method '{method}'");
                return false;
            }

            bool nameHeaderRequired = RequiresModernNameHeader(method);
            string expectedName = ModernPrincipalName(method, rawMessage["params"] as JObject);
            string nameHeader = httpRequest.Headers["Mcp-Name"];
            if (nameHeaderRequired && string.IsNullOrEmpty(nameHeader))
            {
                error = HeaderMismatch(rawMessage["id"], $"Mcp-Name header is required for method '{method}'");
                return false;
            }

            if (string.Equals(method, "resources/read", StringComparison.Ordinal))
            {
                JToken uriToken = (rawMessage["params"] as JObject)?["uri"];
                if (uriToken?.Type != JTokenType.String || string.IsNullOrEmpty((string)uriToken))
                {
                    error = JsonRpcResponse.MakeError(rawMessage["id"], McpErrorCode.InvalidParams,
                        "params.uri must be a non-empty string for resources/read");
                    return false;
                }
            }

            if (expectedName != null)
            {
                string decodedNameHeader;
                if (!TryDecodeModernNameHeaderValue(nameHeader, out decodedNameHeader))
                {
                    error = HeaderMismatch(rawMessage["id"],
                        "Mcp-Name must use safe plain ASCII or valid MCP Base64 UTF-8 encoding");
                    return false;
                }

                if (!string.Equals(decodedNameHeader, expectedName, StringComparison.Ordinal))
                {
                    error = HeaderMismatch(rawMessage["id"],
                        $"Mcp-Name header must match request principal '{expectedName}'");
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(nameHeader))
            {
                error = HeaderMismatch(rawMessage["id"], nameHeaderRequired
                    ? $"Mcp-Name header has no matching string request principal for method '{method}'"
                    : $"Mcp-Name is not valid for method '{method}'");
                return false;
            }

            if (string.Equals(method, "tools/call", StringComparison.Ordinal)
                && !ValidateModernToolParameterHeaders(httpRequest, rawMessage, out error))
            {
                return false;
            }

            return true;
        }

        private static bool IsValidModernProgressToken(JToken progressToken)
        {
            return progressToken != null
                && (progressToken.Type == JTokenType.String
                    || progressToken.Type == JTokenType.Integer
                    || progressToken.Type == JTokenType.Float);
        }

        private static bool IsValidModernLogLevel(JToken logLevel)
        {
            if (logLevel?.Type != JTokenType.String)
                return false;

            switch ((string)logLevel)
            {
                case "debug":
                case "info":
                case "notice":
                case "warning":
                case "error":
                case "critical":
                case "alert":
                case "emergency":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ValidateModernClientCapabilities(JObject capabilities, out string errorMessage)
        {
            errorMessage = null;

            if (!IsObjectCapabilityWhenPresent(capabilities, "roots", out errorMessage))
                return false;

            JObject sampling;
            if (!TryGetObjectCapability(capabilities, "sampling", out sampling, out errorMessage))
                return false;
            if (sampling != null
                && (!IsObjectCapabilityWhenPresent(sampling, "context", out errorMessage)
                    || !IsObjectCapabilityWhenPresent(sampling, "tools", out errorMessage)))
                return false;

            JObject elicitation;
            if (!TryGetObjectCapability(capabilities, "elicitation", out elicitation, out errorMessage))
                return false;
            if (elicitation != null
                && (!IsObjectCapabilityWhenPresent(elicitation, "form", out errorMessage)
                    || !IsObjectCapabilityWhenPresent(elicitation, "url", out errorMessage)))
                return false;

            if (!ValidateObjectMapCapability(capabilities, "experimental", out errorMessage)
                || !ValidateModernExtensionCapabilities(capabilities, out errorMessage))
                return false;

            return true;
        }

        private static bool IsObjectCapabilityWhenPresent(JObject parent, string name, out string errorMessage)
        {
            errorMessage = null;
            JToken token = parent?[name];
            if (token == null)
                return true;
            if (token.Type == JTokenType.Object)
                return true;

            errorMessage = $"params._meta.io.modelcontextprotocol/clientCapabilities.{name} must be an object when provided";
            return false;
        }

        private static bool TryGetObjectCapability(JObject capabilities, string name, out JObject value,
            out string errorMessage)
        {
            value = null;
            errorMessage = null;
            JToken token = capabilities?[name];
            if (token == null)
                return true;

            value = token as JObject;
            if (value != null)
                return true;

            errorMessage = $"params._meta.io.modelcontextprotocol/clientCapabilities.{name} must be an object when provided";
            return false;
        }

        private static bool ValidateObjectMapCapability(JObject capabilities, string name, out string errorMessage)
        {
            errorMessage = null;
            JObject map;
            if (!TryGetObjectCapability(capabilities, name, out map, out errorMessage))
                return false;
            if (map == null)
                return true;

            foreach (var property in map.Properties())
            {
                if (property.Value?.Type == JTokenType.Object)
                    continue;

                errorMessage = $"params._meta.io.modelcontextprotocol/clientCapabilities.{name}.{property.Name} must be an object";
                return false;
            }

            return true;
        }

        private static bool ValidateModernExtensionCapabilities(JObject capabilities, out string errorMessage)
        {
            errorMessage = null;
            JObject extensions;
            if (!TryGetObjectCapability(capabilities, "extensions", out extensions, out errorMessage))
                return false;
            if (extensions == null)
                return true;

            foreach (var property in extensions.Properties())
            {
                if (property.Value?.Type != JTokenType.Object)
                {
                    errorMessage =
                        $"params._meta.io.modelcontextprotocol/clientCapabilities.extensions.{property.Name} must be an object";
                    return false;
                }

                if (!IsValidModernExtensionIdentifier(property.Name))
                {
                    errorMessage =
                        $"params._meta.io.modelcontextprotocol/clientCapabilities.extensions key '{property.Name}' must use a valid prefixed MCP metadata identifier";
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidModernExtensionIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return false;

            int slash = identifier.IndexOf('/');
            if (slash <= 0 || slash != identifier.LastIndexOf('/'))
                return false;

            string prefix = identifier.Substring(0, slash);
            string name = identifier.Substring(slash + 1);
            string[] labels = prefix.Split('.');
            if (labels.Length == 0)
                return false;

            foreach (string label in labels)
            {
                if (!IsValidModernMetadataPrefixLabel(label))
                    return false;
            }

            return IsValidModernMetadataName(name);
        }

        private static bool IsValidModernMetadataPrefixLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || !IsAsciiLetter(label[0])
                || !IsAsciiAlphaNumeric(label[label.Length - 1]))
                return false;

            for (int i = 1; i < label.Length - 1; i++)
            {
                char value = label[i];
                if (!IsAsciiAlphaNumeric(value) && value != '-')
                    return false;
            }

            return true;
        }

        private static bool IsValidModernMetadataName(string name)
        {
            if (name.Length == 0)
                return true;
            if (!IsAsciiAlphaNumeric(name[0]) || !IsAsciiAlphaNumeric(name[name.Length - 1]))
                return false;

            for (int i = 1; i < name.Length - 1; i++)
            {
                char value = name[i];
                if (!IsAsciiAlphaNumeric(value) && value != '-' && value != '_' && value != '.')
                    return false;
            }

            return true;
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return IsAsciiLetter(value) || (value >= '0' && value <= '9');
        }

        private static bool IsValidModernRequestId(JToken requestId)
        {
            return requestId != null
                && (requestId.Type == JTokenType.String || requestId.Type == JTokenType.Integer);
        }

        private static bool RequiresModernNameHeader(string method)
        {
            return string.Equals(method, "tools/call", StringComparison.Ordinal)
                || string.Equals(method, "resources/read", StringComparison.Ordinal)
                || string.Equals(method, "prompts/get", StringComparison.Ordinal);
        }

        private static bool TryDecodeModernNameHeaderValue(string headerValue, out string decodedValue)
        {
            decodedValue = headerValue;
            if (string.IsNullOrEmpty(headerValue))
                return false;

            bool encoded = headerValue.StartsWith(Base64HeaderPrefix, StringComparison.Ordinal)
                && headerValue.EndsWith(Base64HeaderSuffix, StringComparison.Ordinal);
            if (!encoded && !IsSafePlainModernHeaderValue(headerValue))
                return false;

            return TryDecodeModernHeaderValue(headerValue, out decodedValue);
        }

        private static bool TryDecodeModernHeaderValue(string headerValue, out string decodedValue)
        {
            decodedValue = headerValue;
            if (string.IsNullOrEmpty(headerValue)
                || !headerValue.StartsWith(Base64HeaderPrefix, StringComparison.Ordinal)
                || !headerValue.EndsWith(Base64HeaderSuffix, StringComparison.Ordinal))
            {
                return true;
            }

            int payloadLength = headerValue.Length - Base64HeaderPrefix.Length - Base64HeaderSuffix.Length;
            if (payloadLength < 0)
                return false;

            string payload = headerValue.Substring(Base64HeaderPrefix.Length, payloadLength);
            try
            {
                decodedValue = StrictUtf8.GetString(Convert.FromBase64String(payload));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static JsonRpcResponse HeaderMismatch(object id, string message)
        {
            return JsonRpcResponse.MakeError(id, HeaderMismatchErrorCode, message);
        }

        private static JsonRpcResponse UnsupportedProtocolVersion(object id, string requestedVersion)
        {
            return JsonRpcResponse.MakeError(id, UnsupportedProtocolVersionErrorCode,
                $"Unsupported protocol version: {requestedVersion}", new JObject
                {
                    ["requested"] = requestedVersion,
                    ["supported"] = BuildSupportedProtocolVersions()
                });
        }

        private static JArray BuildSupportedProtocolVersions()
        {
            return new JArray(ModernProtocolVersion);
        }

        private static string ModernPrincipalName(string method, JObject @params)
        {
            if (@params == null)
                return null;
            switch (method)
            {
                case "resources/read":
                    return @params["uri"]?.Type == JTokenType.String ? (string)@params["uri"] : null;
                case "tools/call":
                case "prompts/get":
                    return @params["name"]?.Type == JTokenType.String ? (string)@params["name"] : null;
                default:
                    return null;
            }
        }
    }
}
