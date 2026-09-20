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

            return true;
        }
    }
}
