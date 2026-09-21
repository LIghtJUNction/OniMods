using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool HasValidModernSamplingMessageComposition(string role, JToken content)
        {
            if (!string.Equals(role, "user", StringComparison.Ordinal))
                return true;

            var blocks = content as JArray;
            if (blocks == null)
                return true;

            bool hasToolResult = false;
            bool hasOtherContent = false;
            foreach (var item in blocks)
            {
                var block = item as JObject;
                string type = (string)block?["type"];
                if (string.Equals(type, "tool_result", StringComparison.Ordinal))
                    hasToolResult = true;
                else
                    hasOtherContent = true;
            }

            return !hasToolResult || !hasOtherContent;
        }
    }
}
