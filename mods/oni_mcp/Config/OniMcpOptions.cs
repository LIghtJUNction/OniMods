using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        private const int CurrentSecurityMigrationVersion = 1;

        public int SecurityMigrationVersion { get; set; } = CurrentSecurityMigrationVersion;

        [Option("Host", "HTTP listen host. Use localhost for local clients, or 0.0.0.0 to listen on all interfaces.", "Server")]
        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 8788;

        [Option("Port", "HTTP port for the MCP endpoint.", "Server")]
        [JsonIgnore]
        public string PortInput
        {
            get => Port.ToString(CultureInfo.InvariantCulture);
            set => Port = ParseCompactInt(value, Port, 1024, 65535);
        }

        [Option("Require token", "Disabled by default. Enable manually to require the configured bearer token for every MCP request.", "Security")]
        public bool AuthEnabled { get; set; } = false;

        [Option("Token", "Used only when Require token is enabled. A token is generated safely if enabled while empty.", "Security")]
        public string AuthToken { get; set; } = CreateAuthToken();

        [Option("Disable auto disinfect globally", "Keep ONI's global auto disinfect setting disabled when the mod applies this policy.", "Gameplay")]
        public bool GlobalAutoDisinfectDisabled { get; set; } = false;

        [Option("Clean up screenshots", "Automatically remove old temporary screenshots created by MCP tools.", "Screenshots")]
        public bool ScreenshotCleanupEnabled { get; set; } = true;

        public int ScreenshotRetentionMinutes { get; set; } = 120;

        [Option("Screenshot retention minutes", "How long temporary screenshots are retained before cleanup.", "Screenshots")]
        [JsonIgnore]
        public string ScreenshotRetentionMinutesInput
        {
            get => ScreenshotRetentionMinutes.ToString(CultureInfo.InvariantCulture);
            set => ScreenshotRetentionMinutes = ParseCompactInt(value, ScreenshotRetentionMinutes, 1, 10080);
        }

        public int ScreenshotMaxFiles { get; set; } = 40;

        [Option("Screenshot max files", "Maximum number of temporary screenshots to keep.", "Screenshots")]
        [JsonIgnore]
        public string ScreenshotMaxFilesInput
        {
            get => ScreenshotMaxFiles.ToString(CultureInfo.InvariantCulture);
            set => ScreenshotMaxFiles = ParseCompactInt(value, ScreenshotMaxFiles, 1, 1000);
        }

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
                    yield return $"http://localhost:{Port}/";
                    yield return $"http://127.0.0.1:{Port}/";
                    yield return $"http://localhost:{Port}/mcp/";
                    yield return $"http://127.0.0.1:{Port}/mcp/";
                    yield return $"http://localhost:{Port}/screenshots/";
                    yield return $"http://127.0.0.1:{Port}/screenshots/";
                }
                else
                {
                    yield return $"http://{ListenHost}:{Port}/";
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
                    "Endpoint: " + EndpointUrl + "\nConfig: " + ConfigPath + "\nAuthentication: " + (AuthEnabled ? "enabled" : "disabled by default"),
                    "Current endpoint and config path. Expand Status, Server, Security, and Screenshots; PLib scrolls the dialog when needed.",
                    "Status"));

            var browseButton = new ButtonOptionsEntry(
                "OpenBrowser",
                new OptionAttribute(
                    "Open File Browser",
                    "Open the virtual file browser in your web browser.",
                    "Status"));
            browseButton.Value = (Action<object>)(_ => UnityEngine.Application.OpenURL("http://localhost:" + Current.Port + "/"));
            yield return browseButton;

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
            {
                var created = Sanitize(new OniMcpOptions());
                TrySave(created);
                return created;
            }

            try
            {
                string json = File.ReadAllText(path);
                var raw = JObject.Parse(json);
                var loaded = raw.ToObject<OniMcpOptions>();
                ApplySecurityMigration(loaded, raw);
                var options = Sanitize(loaded ?? new OniMcpOptions());
                TrySave(options);
                return options;
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to read config " + path + ": " + ex.Message);
                var fallback = Sanitize(new OniMcpOptions());
                TrySave(fallback);
                return fallback;
            }
        }

        private static void TrySave(OniMcpOptions options)
        {
            try
            {
                Save(options);
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to write config " + ConfigPath + ": " + ex.Message);
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
            if (options.SecurityMigrationVersion < CurrentSecurityMigrationVersion)
                options.SecurityMigrationVersion = CurrentSecurityMigrationVersion;

            options.ScreenshotRetentionMinutes = Clamp(options.ScreenshotRetentionMinutes, 1, 10080);
            options.ScreenshotMaxFiles = Clamp(options.ScreenshotMaxFiles, 1, 1000);
            return options;
        }

        private static void ApplySecurityMigration(OniMcpOptions options, JObject raw)
        {
            if (options == null || raw == null)
                return;
            int version = raw["SecurityMigrationVersion"]?.Value<int>() ?? 0;
            if (version >= CurrentSecurityMigrationVersion)
                return;
            options.SecurityMigrationVersion = CurrentSecurityMigrationVersion;
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

        private static int ParseCompactInt(string text, int current, int min, int max)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                || parsed < min || parsed > max)
                return current;
            return parsed;
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
