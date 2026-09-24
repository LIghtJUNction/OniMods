using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool ValidateModernResourceReadUri(JObject parameters, out string errorMessage)
        {
            errorMessage = null;
            JToken uriToken = parameters?["uri"];
            string uri = uriToken?.Type == JTokenType.String ? (string)uriToken : null;
            Uri parsedUri;
            if (!string.IsNullOrEmpty(uri) && Uri.TryCreate(uri, UriKind.Absolute, out parsedUri))
                return true;

            errorMessage = "params.uri must be an absolute URI string for resources/read";
            return false;
        }
    }
}
