using System;
using System.Globalization;

namespace OniOnline.Network
{
    public sealed class RoomAddress
    {
        public bool Steam;
        public ulong LobbyId;
        public string Host;
        public int Port;

        public static RoomAddress Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("invalid_address");
            value = value.Trim();
            if (value.StartsWith("oni://steam/", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("oni-steam://", StringComparison.OrdinalIgnoreCase))
            {
                ulong id;
                if (!ulong.TryParse(value.Substring(12), NumberStyles.None, CultureInfo.InvariantCulture, out id) || id == 0)
                    throw new ArgumentException("invalid_address");
                return new RoomAddress { Steam = true, LobbyId = id };
            }
            if (!value.StartsWith("oni://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("oni-lan://", StringComparison.OrdinalIgnoreCase)) value = "oni://" + value;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != "oni" && uri.Scheme != "oni-lan") ||
                string.IsNullOrEmpty(uri.Host) || uri.Port < 1 || uri.Port > 65535 ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) || (uri.AbsolutePath != "" && uri.AbsolutePath != "/"))
                throw new ArgumentException("invalid_address");
            return new RoomAddress { Host = uri.Host.Trim('[', ']'), Port = uri.Port };
        }
    }
}
