using System.IO;
using System.Reflection;
using UnityEngine;

namespace OniMcp.Support
{
    public static class OniMcpPaths
    {
        private const string ConfigFileName = "OniMcpConfig.json";
        private static string _modPath;

        public static void Initialize(string modPath, Assembly assembly)
        {
            if (!string.IsNullOrEmpty(modPath))
            {
                _modPath = modPath;
                return;
            }

            string assemblyLocation = assembly?.Location;
            if (!string.IsNullOrEmpty(assemblyLocation))
                _modPath = Path.GetDirectoryName(assemblyLocation);
        }

        public static string ModPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_modPath))
                    return _modPath;

                string assemblyLocation = typeof(OniMcpPaths).Assembly.Location;
                return string.IsNullOrEmpty(assemblyLocation) ? null : Path.GetDirectoryName(assemblyLocation);
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
