using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Server;
using OniMcp.Support;
using PeterHan.PLib;
using PeterHan.PLib.Options;
using UnityEngine;

namespace OniMcp.Config
{
    /// <summary>Persistent server and security settings exposed through PLib.</summary>
    [ConfigFile("OniMcpConfig.json", true)]
    [ModInfo("https://steamcommunity.com/sharedfiles/filedetails/?id=3731864673", "preview.png")]
    public class OniMcpOptions : IOptions
    {
        private static readonly object SyncRoot = new object();
        private static volatile OniMcpOptions _current;
        private const int CurrentSecurityMigrationVersion = 1;
        private const int MaxDisplayedEndpointLength = 48;
        private const string ProjectSupportUrl = "https://api.lmm.best";

        public int SecurityMigrationVersion { get; set; } = CurrentSecurityMigrationVersion;

        [Option("Host", "HTTP listen host. Localhost is the safe default; non-loopback hosts require authentication and expose plaintext HTTP unless protected by a trusted tunnel or TLS reverse proxy.", "Server")]
        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 8788;

        [Option("Port", "HTTP port for the MCP endpoint.", "Server")]
        [JsonIgnore]
        public string PortInput
        {
            get => Port.ToString(CultureInfo.InvariantCulture);
            set => Port = ParseCompactInt(value, Port, 1024, 65535);
        }

        [Option("Require token", "Disabled by default for loopback-only access. Non-loopback hosts require authentication.", "Security")]
        public bool AuthEnabled { get; set; } = false;

        [DynamicOption(typeof(MaskedTokenOptionsEntry))]
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
                var current = _current;
                if (current != null)
                    return current;
                lock (SyncRoot)
                {
                    if (_current == null)
                        _current = Load();
                    return _current;
                }
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
        internal bool IsLoopbackHost => IsLoopback(Host);

        [JsonIgnore]
        internal string PlaintextRemoteWarning => IsLoopbackHost
            ? null
            : "Remote OniMcp HTTP is plaintext. Bearer tokens are not encrypted; prefer a trusted VPN/tunnel or a TLS-terminating reverse proxy with a loopback upstream.";

        [JsonIgnore]
        public IEnumerable<string> ListenPrefixes
        {
            get
            {
                ValidateListenSecurity();
                string host = NormalizeHost(Host);
                if (!IsLoopbackHost)
                    OniMcpLog.Warning("[OniMcp] " + PlaintextRemoteWarning);

                if (host == "localhost")
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
            lock (SyncRoot)
                _current = Load();
        }

        public static void Save(OniMcpOptions options)
        {
            lock (SyncRoot)
            {
                options = Sanitize(options);
                options.ValidateListenSecurity();
                string path = ConfigPath;
                if (string.IsNullOrEmpty(path))
                    throw new InvalidOperationException("The config path is not available.");
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(options, Formatting.Indented);
                // Keep the original file intact until its replacement has been fully written.
                string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporaryPath, json);
                    if (File.Exists(path))
                        File.Replace(temporaryPath, path, null);
                    else
                        File.Move(temporaryPath, path);
                    _current = options;
                }
                finally
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception ex)
                    {
                        OniMcpLog.Warning("[OniMcp] Failed to remove temporary config " + temporaryPath + ": " + ex.Message);
                    }
                }
            }
        }

        public IEnumerable<IOptionsEntry> CreateOptions()
        {
            string endpoint = EndpointUrl;
            string displayedEndpoint = endpoint.Length > MaxDisplayedEndpointLength
                ? endpoint.Substring(0, MaxDisplayedEndpointLength - 3) + "..."
                : endpoint;
            yield return new TextBlockOptionsEntry(
                "OniMcpStatus",
                new OptionAttribute(
                    "Endpoint: " + displayedEndpoint + "\nConfig: OniMcpConfig.json\nAuthentication: " + (AuthEnabled ? "enabled" : "disabled by default"),
                    "Endpoint: " + endpoint + "\nConfig: " + ConfigPath + "\nUse Open config folder to locate the file.",
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

            var supportButton = new ButtonOptionsEntry(
                "OpenProjectSupport",
                new OptionAttribute(
                    "购买 AI Token / 支持项目",
                    ProjectSupportUrl + "\n可购买 AI Token（完全可选），与模组访问令牌无关。"
                        + "\nBuy AI tokens there (optional); OniMcp requires no purchase.",
                    "Support"));
            supportButton.Value = (Action<object>)(_ => Application.OpenURL(ProjectSupportUrl));
            yield return supportButton;
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
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                var created = Sanitize(new OniMcpOptions());
                TrySave(created);
                return created;
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to read config " + path + ": " + ex.Message);
                // A transient read failure must not overwrite the user's settings or token.
                if (_current != null)
                    return _current;
                throw new InvalidOperationException("Cannot load OniMcp config " + path + ". Fix the file before starting the server.", ex);
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
            // Preserve the user's opt-in authentication choice during migration.
            options.SecurityMigrationVersion = CurrentSecurityMigrationVersion;
        }

        internal void ValidateListenSecurity()
        {
            if (IsLoopbackHost || AuthEnabled)
                return;

            throw new InvalidOperationException(
                "Refusing to expose OniMcp on non-loopback host '" + NormalizeHost(Host)
                + "' without authentication. Enable Require token or use localhost/127.0.0.1/::1.");
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
            if (host.Length > 2 && host[0] == '[' && host[host.Length - 1] == ']')
                host = host.Substring(1, host.Length - 2);
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return "localhost";
            if (host == "*" || host == "+")
                return "0.0.0.0";

            return host;
        }

        private static bool IsLoopback(string host)
        {
            host = NormalizeHost(host);
            if (host == "localhost")
                return true;

            return IPAddress.TryParse(host, out IPAddress address) && IPAddress.IsLoopback(address);
        }

        private static string FormatHostForUrl(string host)
        {
            if (string.IsNullOrEmpty(host) || host == "+")
                return host;
            if (host.IndexOf(':') >= 0 && !(host[0] == '[' && host[host.Length - 1] == ']'))
                return "[" + host + "]";
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
                string host = NormalizeHost(Host);
                if (host == "0.0.0.0")
                    return "+";
                return FormatHostForUrl(host);
            }
        }

        [JsonIgnore]
        private string DisplayHost => FormatHostForUrl(NormalizeHost(Host));
    }
}
