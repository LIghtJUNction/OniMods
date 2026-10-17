using System;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool IsModernBase64ImageDataUri(string uri)
        {
            const string prefix = "data:";
            int comma = uri.IndexOf(',');
            if (comma <= prefix.Length)
                return false;

            string metadata = uri.Substring(prefix.Length, comma - prefix.Length);
            string[] metadataParts = metadata.Split(';');
            if (metadataParts.Length < 2
                || !metadataParts[0].StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || metadataParts[0].Length == "image/".Length
                || !string.Equals(metadataParts[metadataParts.Length - 1], "base64",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                string payload = Uri.UnescapeDataString(uri.Substring(comma + 1));
                Convert.FromBase64String(payload);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
