using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool ValidateModernMetaKeys(JObject meta, out string errorMessage)
        {
            errorMessage = null;
            if (meta == null)
                return true;

            foreach (var property in meta.Properties())
            {
                if (IsValidModernMetaKey(property.Name))
                    continue;

                errorMessage = $"params._meta key '{property.Name}' must use valid MCP metadata key syntax";
                return false;
            }

            return true;
        }

        private static bool IsValidModernMetaKey(string key)
        {
            if (key == null)
                return false;

            int slash = key.IndexOf('/');
            if (slash < 0)
                return IsValidModernMetadataName(key);
            if (slash == 0 || slash != key.LastIndexOf('/'))
                return false;

            string prefix = key.Substring(0, slash);
            foreach (string label in prefix.Split('.'))
            {
                if (!IsValidModernMetadataPrefixLabel(label))
                    return false;
            }

            return IsValidModernMetadataName(key.Substring(slash + 1));
        }
    }
}
