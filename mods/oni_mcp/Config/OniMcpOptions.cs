using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using OniMcp.Server;
using OniMcp.Support;
using PeterHan.PLib.Options;
using UnityEngine;

namespace OniMcp.Config
{
    [ConfigFile("OniMcpConfig.json", true)]
    [ModInfo("https://steamcommunity.com/sharedfiles/filedetails/?id=3731864673", "preview.png")]
    public class OniMcpOptions : IOptions
    {
        private static OniMcpOptions _current;

        [Option("Host", "HTTP listen host. Use localhost for local clients, or 0.0.0.0 to listen on all interfaces.", "Server")]
        public string Host { get; set; } = "localhost";

        [Option("Port", "HTTP port for the MCP endpoint.", "Server")]
        [Limit(1024, 65535, 1)]
        public int Port { get; set; } = 8788;

        [Option("Require token", "Require clients to send the configured bearer token.", "Authentication")]
        public bool AuthEnabled { get; set; } = true;

        [Option("Token", "Bearer token required when token authentication is enabled.", "Authentication")]
        public string AuthToken { get; set; } = CreateAuthToken();

        [Option("Disable auto disinfect globally", "Keep ONI's global auto disinfect setting disabled when the mod applies this policy.", "Gameplay")]
        public bool GlobalAutoDisinfectDisabled { get; set; } = false;

        [Option("Clean up screenshots", "Automatically remove old temporary screenshots created by MCP tools.", "Screenshots")]
        public bool ScreenshotCleanupEnabled { get; set; } = true;

        [Option("Screenshot retention minutes", "How long temporary screenshots are retained before cleanup.", "Screenshots")]
        [Limit(1, 10080, 1)]
        public int ScreenshotRetentionMinutes { get; set; } = 120;

        [Option("Screenshot max files", "Maximum number of temporary screenshots to keep.", "Screenshots")]
        [Limit(1, 1000, 1)]
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

        [JsonIgnore]
        public string EndpointUrl => $"http://{DisplayHost}:{Port}/mcp/";

        [JsonIgnore]
        public string ScreenshotLatestUrl => $"http://{DisplayHost}:{Port}/screenshots/latest.png";

        [JsonIgnore]
        public string ScreenshotBaseUrl => $"http://{DisplayHost}:{Port}/screenshots/";

        [JsonIgnore]
        public IEnumerable<string> ListenPrefixes
        {
            get
            {
                if (Host == "localhost")
                {
                    yield return $"http://localhost:{Port}/mcp/";
                    yield return $"http://127.0.0.1:{Port}/mcp/";
                    yield return $"http://localhost:{Port}/screenshots/";
                    yield return $"http://127.0.0.1:{Port}/screenshots/";
                }
                else
                {
                    yield return $"http://{ListenHost}:{Port}/mcp/";
                    yield return $"http://{ListenHost}:{Port}/screenshots/";
                }
            }
        }

        public static void Reload()
        {
            _current = Load();
        }

        public static void Save(OniMcpOptions options)
        {
            options = Sanitize(options);
            string path = ConfigPath;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(options, Formatting.Indented);
            File.WriteAllText(path, json);
            _current = options;
        }

        public IEnumerable<IOptionsEntry> CreateOptions()
        {
            yield return new TextBlockOptionsEntry(
                "OniMcpStatus",
                new OptionAttribute(
                    "Endpoint: " + EndpointUrl + "\nConfig: " + ConfigPath,
                    "Current ONI MCP endpoint and configuration file path.",
                    "Status"));

            var restartButton = new ButtonOptionsEntry(
                "RestartMcpServer",
                new OptionAttribute(
                    "Restart MCP server",
                    "Restart the ONI MCP HTTP server without restarting the game.",
                    "Status"));
            restartButton.Value = (Action<object>)(_ => RestartServer());
            yield return restartButton;

            var configButton = new ButtonOptionsEntry(
                "OpenConfigFolder",
                new OptionAttribute(
                    "Open config folder",
                    "Open the folder containing OniMcpConfig.json.",
                    "Status"));
            configButton.Value = (Action<object>)(_ => OpenConfigFolder());
            yield return configButton;
        }

        public void OnOptionsChanged()
        {
            Save(this);
            if (McpHttpServer.Instance != null)
                McpHttpServer.Instance.RestartServer();
        }

        private static void RestartServer()
        {
            if (McpHttpServer.Instance != null)
            {
                McpHttpServer.Instance.RestartServer();
                OniMcpLog.Debug("[OniMcp] MCP server restarted from PLib options.");
            }
            else
            {
                OniMcpLog.Warning("[OniMcp] Cannot restart MCP server from options: server is not available.");
            }
        }

        private static void OpenConfigFolder()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (string.IsNullOrEmpty(dir))
                dir = OniMcpPaths.ModPath;

            if (string.IsNullOrEmpty(dir))
            {
                OniMcpLog.Warning("[OniMcp] Cannot open config folder: path is not available.");
                return;
            }

            Application.OpenURL(new Uri(dir + Path.DirectorySeparatorChar).AbsoluteUri);
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
                options.Port = 8788;
            options.AuthToken = (options.AuthToken ?? "").Trim();
            if (options.AuthEnabled && string.IsNullOrEmpty(options.AuthToken))
                options.AuthToken = CreateAuthToken();
            options.ScreenshotRetentionMinutes = Clamp(options.ScreenshotRetentionMinutes, 1, 10080);
            options.ScreenshotMaxFiles = Clamp(options.ScreenshotMaxFiles, 1, 1000);
            return options;
        }

        private static string CreateAuthToken()
        {
            return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
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

        [JsonIgnore]
        private string ListenHost
        {
            get
            {
                if (Host == "0.0.0.0")
                    return "+";
                return Host;
            }
        }

        [JsonIgnore]
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
