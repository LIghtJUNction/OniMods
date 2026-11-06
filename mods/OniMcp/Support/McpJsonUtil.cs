using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OniMcp.Support
{
    public static class McpJsonUtil
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        // Tool composition keeps structured child results as values rather than
        // repeatedly escaping a JSON document inside another JSON string.
        public static JToken ParseResultText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new JValue(text ?? string.Empty);
            int index = 0;
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
            if (index == text.Length || (text[index] != '{' && text[index] != '['))
                return new JValue(text);
            try
            {
                using (var input = new StringReader(text))
                using (var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None })
                {
                    var value = JToken.ReadFrom(reader);
                    return reader.Read() ? new JValue(text) : value;
                }
            }
            catch (JsonException)
            {
                return new JValue(text);
            }
        }
    }
}
