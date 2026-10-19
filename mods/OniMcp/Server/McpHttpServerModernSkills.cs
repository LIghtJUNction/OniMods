using System;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static object ProcessModernSkillsRequest(JsonRpcRequest request)
        {
            if (!OniMcpOptions.Current.SkillsExtensionEnabled)
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.MethodNotFound,
                    "Skills extension is disabled");

            if (string.Equals(request.Method, "skills/list", StringComparison.Ordinal))
                return CompleteModernListResult(new JObject
                {
                    ["skills"] = new JArray(OniMcpSkillCatalog.GetSkill())
                });

            JToken uriToken = request.Params?["uri"];
            string uri = uriToken?.Type == JTokenType.String ? (string)uriToken : null;
            if (!string.Equals(uri, OniMcpSkillCatalog.SkillUri, StringComparison.Ordinal))
                return JsonRpcResponse.MakeError(request.Id, McpErrorCode.InvalidParams,
                    "Unknown skill URI");

            return CompleteModernListResult(new JObject
            {
                ["skill"] = OniMcpSkillCatalog.GetSkill()
            });
        }
    }
}
