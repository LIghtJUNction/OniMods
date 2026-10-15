using System;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        // Both transports serve unparameterized media types. Only one q weight
        // can qualify a matching range; other parameters do not match either response.
        private static bool TryParseAcceptParameters(string[] segments, out decimal quality)
        {
            quality = 1m;
            bool qualitySeen = false;
            for (int index = 1; index < segments.Length; index++)
            {
                string parameter = segments[index].Trim();
                int equals = parameter.IndexOf('=');
                if (equals <= 0
                    || !string.Equals(parameter.Substring(0, equals).Trim(), "q",
                        StringComparison.OrdinalIgnoreCase)
                    || qualitySeen
                    || !TryParseHttpQualityValue(parameter.Substring(equals + 1), out quality))
                {
                    return false;
                }
                qualitySeen = true;
            }

            return true;
        }

        private static bool TryParseHttpQualityValue(string rawValue, out decimal quality)
        {
            quality = 0m;
            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            string value = rawValue.Trim();
            if (value[0] != '0' && value[0] != '1')
                return false;

            if (value.Length == 1)
            {
                quality = value[0] == '1' ? 1m : 0m;
                return true;
            }

            if (value[1] != '.' || value.Length > 5)
                return false;

            decimal fraction = 0m;
            decimal place = 0.1m;
            for (int i = 2; i < value.Length; i++)
            {
                char digit = value[i];
                if (digit < '0' || digit > '9')
                    return false;
                if (value[0] == '1' && digit != '0')
                    return false;

                fraction += (digit - '0') * place;
                place /= 10m;
            }

            quality = value[0] == '1' ? 1m : fraction;
            return true;
        }
    }
}
