namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
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
