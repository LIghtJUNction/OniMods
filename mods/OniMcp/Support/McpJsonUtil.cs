using Newtonsoft.Json;

namespace OniMcp.Support
{
    public static class McpJsonUtil
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };
    }
}
