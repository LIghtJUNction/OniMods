using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static JToken MapEditResultValue(CallToolResult result)
        {
            string text = result?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            try { return JToken.Parse(text); }
            catch (Newtonsoft.Json.JsonReaderException) { return new JValue(text); }
        }
    }
}
