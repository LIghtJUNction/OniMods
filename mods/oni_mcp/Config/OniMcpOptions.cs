using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using OniMcp.Support;

namespace OniMcp.Config
{
    public class OniMcpOptions
    {
        private static OniMcpOptions _current;

        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 8787;

        public bool ScreenshotCleanupEnabled { get; set; } = true;

        public int ScreenshotRetentionMinutes { get; set; } = 120;

        public int ScreenshotMaxFiles { get; set; } = 40;

        public static OniMcpOptions Current
        {
            get
            {
                if (_current == null)
                    _current = Load();
                return _current;
            }
        }

        public static string ConfigPath => OniMcpPaths.ConfigPath;

        public string EndpointUrl => $"http://{DisplayHost}:{Port}/mcp/";

        public IEnumerable<string> ListenPrefixes
        {
            get
            {
                if (Host == "localhost")
                {
                    yield return $"http://localhost:{Port}/mcp/";
                    yield return $"http://127.0.0.1:{Port}/mcp/";
                }
                else
                {
                    yield return $"http://{ListenHost}:{Port}/mcp/";
                }
            }
        }

        public static void Reload()
        {
            _current = Load();
        }

        private static OniMcpOptions Load()
        {
            string path = ConfigPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new OniMcpOptions();

            try
            {
                string json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<OniMcpOptions>(json);
                return Sanitize(loaded ?? new OniMcpOptions());
            }
            catch (System.Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to read config " + path + ": " + ex.Message);
                return new OniMcpOptions();
            }
        }

        private static OniMcpOptions Sanitize(OniMcpOptions options)
        {
            if (options == null)
                options = new OniMcpOptions();

            options.Host = NormalizeHost(options.Host);
            if (options.Port < 1024 || options.Port > 65535)
                options.Port = 8787;
            options.ScreenshotRetentionMinutes = Clamp(options.ScreenshotRetentionMinutes, 1, 10080);
            options.ScreenshotMaxFiles = Clamp(options.ScreenshotMaxFiles, 1, 1000);
            return options;
        }

        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return "localhost";

            host = host.Trim();
            if (host == "*" || host == "+")
                return "0.0.0.0";

            return host;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private string ListenHost
        {
            get
            {
                if (Host == "0.0.0.0")
                    return "+";
                return Host;
            }
        }

        private string DisplayHost
        {
            get
            {
                if (Host == "+")
                    return "0.0.0.0";
                return Host;
            }
        }
    }
}
