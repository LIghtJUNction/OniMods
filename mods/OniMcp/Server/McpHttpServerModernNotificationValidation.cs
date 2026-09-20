using System;
using System.Net;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool ValidateModernNotification(HttpListenerRequest httpRequest, JObject rawMessage,
            string method, string protocolVersion, JObject meta, string metaVersion, out JsonRpcResponse error)
        {
            error = null;

            var paramsObject = rawMessage["params"] as JObject;
            var metaToken = paramsObject?["_meta"];
            if (metaToken != null && metaToken.Type != JTokenType.Object)
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidParams,
                    "Notification _meta must be an object when provided");
                return false;
            }

            string metaKeyError;
            if (!ValidateModernMetaKeys(meta, out metaKeyError))
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidParams, metaKeyError);
                return false;
            }

            var traceparentProperty = meta?.Property("traceparent");
            if (traceparentProperty != null && !IsValidModernTraceparent(traceparentProperty.Value))
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidParams,
                    "Notification traceparent must be a valid W3C Trace Context value when provided");
                return false;
            }

            var tracestateProperty = meta?.Property("tracestate");
            if (tracestateProperty != null && !IsValidModernTracestate(tracestateProperty.Value))
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidParams,
                    "Notification tracestate must be a valid W3C Trace Context value when provided");
                return false;
            }

            var baggageProperty = meta?.Property("baggage");
            if (baggageProperty != null && !IsValidModernBaggage(baggageProperty.Value))
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidParams,
                    "Notification baggage must be a valid W3C Baggage value when provided");
                return false;
            }

            var subscriptionIdProperty = meta?.Property("io.modelcontextprotocol/subscriptionId");
            if (subscriptionIdProperty != null && !IsValidModernSubscriptionId(subscriptionIdProperty.Value))
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidParams,
                    "Notification io.modelcontextprotocol/subscriptionId must be a string or integer when provided");
                return false;
            }

            var metaVersionToken = meta?["io.modelcontextprotocol/protocolVersion"];
            if (metaVersionToken != null && metaVersionToken.Type != JTokenType.String)
            {
                error = JsonRpcResponse.MakeError(null, McpErrorCode.InvalidParams,
                    "Notification protocol version claim must be a string when provided");
                return false;
            }

            if (!string.IsNullOrEmpty(protocolVersion)
                && !string.IsNullOrEmpty(metaVersion)
                && !string.Equals(protocolVersion, metaVersion, StringComparison.Ordinal))
            {
                error = HeaderMismatch(null,
                    $"Mcp-Protocol-Version '{protocolVersion}' must match notification protocol version claim '{metaVersion}'");
                return false;
            }

            string methodHeader = httpRequest.Headers["Mcp-Method"];
            if (!string.IsNullOrEmpty(methodHeader)
                && !string.Equals(methodHeader, method, StringComparison.Ordinal))
            {
                error = HeaderMismatch(null,
                    $"Mcp-Method header must match JSON-RPC notification method '{method}'");
                return false;
            }

            return true;
        }

        private static bool IsValidModernSubscriptionId(JToken subscriptionId)
        {
            return subscriptionId != null
                && (subscriptionId.Type == JTokenType.String
                    || subscriptionId.Type == JTokenType.Integer);
        }
    }
}
