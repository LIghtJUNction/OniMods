using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool IsValidModernProgressToken(JToken progressToken)
        {
            if (progressToken == null)
                return false;
            if (progressToken.Type == JTokenType.String || progressToken.Type == JTokenType.Integer)
                return true;
            if (progressToken.Type != JTokenType.Float)
                return false;

            double numericValue = progressToken.Value<double>();
            return !double.IsNaN(numericValue) && !double.IsInfinity(numericValue);
        }
    }
}
