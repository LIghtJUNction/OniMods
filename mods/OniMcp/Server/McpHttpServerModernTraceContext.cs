using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool IsValidModernTraceparent(JToken traceparent)
        {
            if (traceparent?.Type != JTokenType.String)
                return false;

            string value = (string)traceparent;
            if (value.Length < 55
                || value[2] != '-'
                || value[35] != '-'
                || value[52] != '-')
            {
                return false;
            }

            if (!IsLowerHexRange(value, 0, 2, false)
                || (value[0] == 'f' && value[1] == 'f')
                || !IsLowerHexRange(value, 3, 32, true)
                || !IsLowerHexRange(value, 36, 16, true)
                || !IsLowerHexRange(value, 53, 2, false))
            {
                return false;
            }

            bool versionZero = value[0] == '0' && value[1] == '0';
            if (versionZero)
                return value.Length == 55;

            return value.Length == 55 || value[55] == '-';
        }

        private static bool IsLowerHexRange(string value, int start, int length, bool rejectAllZero)
        {
            bool hasNonZero = false;
            int end = start + length;
            for (int i = start; i < end; i++)
            {
                char current = value[i];
                bool isHex = (current >= '0' && current <= '9')
                    || (current >= 'a' && current <= 'f');
                if (!isHex)
                    return false;
                if (current != '0')
                    hasNonZero = true;
            }

            return !rejectAllZero || hasNonZero;
        }
    }
}
