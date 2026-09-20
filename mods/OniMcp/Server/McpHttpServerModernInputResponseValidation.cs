using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool ValidateModernInputResponseRequestParams(string method, JObject parameters,
            out string errorMessage)
        {
            errorMessage = null;
            if (!string.Equals(method, "tools/call", StringComparison.Ordinal)
                && !string.Equals(method, "resources/read", StringComparison.Ordinal))
            {
                return true;
            }

            var requestState = parameters?.Property("requestState");
            if (requestState != null && requestState.Value.Type != JTokenType.String)
            {
                errorMessage = "params.requestState must be a string when provided";
                return false;
            }

            var inputResponses = parameters?.Property("inputResponses");
            if (inputResponses != null && inputResponses.Value.Type != JTokenType.Object)
            {
                errorMessage = "params.inputResponses must be an object when provided";
                return false;
            }

            var inputResponsesObject = inputResponses?.Value as JObject;
            if (inputResponsesObject != null)
            {
                foreach (var response in inputResponsesObject.Properties())
                {
                    var responseObject = response.Value as JObject;
                    if (responseObject == null)
                    {
                        errorMessage = "params.inputResponses values must be objects";
                        return false;
                    }

                    if (!IsModernInputResponseShape(responseObject))
                    {
                        errorMessage = "params.inputResponses values must match an MCP InputResponse shape";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsModernInputResponseShape(JObject response)
        {
            return IsModernElicitResponse(response)
                || IsModernListRootsResponse(response)
                || IsModernCreateMessageResponse(response);
        }

        private static bool IsModernElicitResponse(JObject response)
        {
            var action = response["action"];
            if (action?.Type != JTokenType.String)
                return false;

            string value = (string)action;
            return string.Equals(value, "accept", StringComparison.Ordinal)
                || string.Equals(value, "decline", StringComparison.Ordinal)
                || string.Equals(value, "cancel", StringComparison.Ordinal);
        }

        private static bool IsModernListRootsResponse(JObject response)
        {
            return response["roots"]?.Type == JTokenType.Array;
        }

        private static bool IsModernCreateMessageResponse(JObject response)
        {
            var role = response["role"];
            var content = response["content"];
            var model = response["model"];
            if (role?.Type != JTokenType.String || model?.Type != JTokenType.String || content == null)
                return false;

            string roleValue = (string)role;
            bool validRole = string.Equals(roleValue, "user", StringComparison.Ordinal)
                || string.Equals(roleValue, "assistant", StringComparison.Ordinal);
            bool validContentShape = content.Type == JTokenType.Object || content.Type == JTokenType.Array;
            return validRole && validContentShape;
        }
    }
}
