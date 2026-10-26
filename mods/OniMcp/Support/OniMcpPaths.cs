using System.IO;
using System.Reflection;
using PeterHan.PLib.Core;
using UnityEngine;

namespace OniMcp.Support
{
    public static class OniMcpPaths
    {
        private const string ConfigFileName = "OniMcpConfig.json";
        private const string RestartIntentFileName = "OniMcpRestartIntent.json";
        private static string _modPath;

        public static void Initialize(string modPath, Assembly assembly)
        {
            if (!string.IsNullOrEmpty(modPath))
            {
                _modPath = modPath;
                return;
            }

            if (assembly != null)
                _modPath = PUtil.GetModPath(assembly);
        }

        public static string ModPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_modPath))
                    return _modPath;

                return PUtil.GetModPath(typeof(OniMcpPaths).Assembly);
            }
        }

        public static string ConfigPath
        {
            get
            {
                foreach (string candidate in ConfigCandidates)
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                        return candidate;
                }

                string configDir = Application.persistentDataPath;
                if (string.IsNullOrEmpty(configDir))
                    configDir = ModPath;

                return string.IsNullOrEmpty(configDir) ? ConfigFileName : Path.Combine(configDir, ConfigFileName);
            }
        }

        public static string RestartIntentPath
        {
            get
            {
                string directory = Application.persistentDataPath;
                if (string.IsNullOrEmpty(directory))
                    directory = ModPath;
                return string.IsNullOrEmpty(directory)
                    ? RestartIntentFileName
                    : Path.Combine(directory, RestartIntentFileName);
            }
        }

        public static string RestartRelayPath => string.IsNullOrEmpty(ModPath)
            ? null
            : Path.Combine(ModPath, "assets", "restart_oni_steam_relay.sh");

        private static string[] ConfigCandidates
        {
            get
            {
                string persistentPath = Application.persistentDataPath;
                string modPath = ModPath;
                return new[]
                {
                    string.IsNullOrEmpty(persistentPath) ? null : Path.Combine(persistentPath, ConfigFileName),
                    string.IsNullOrEmpty(modPath) ? null : Path.Combine(modPath, ConfigFileName)
                };
            }
        }
    }
}
