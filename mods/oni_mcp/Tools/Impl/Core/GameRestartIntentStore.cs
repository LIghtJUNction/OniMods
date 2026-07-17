using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using OniMcp.Support;

namespace OniMcp.Tools
{
    internal sealed class GameRestartIntent
    {
        public int SchemaVersion { get; set; } = 1;
        public string JobId { get; set; }
        public string ExactSavePath { get; set; }
        public bool Resume { get; set; }
        public string Stage { get; set; }
        public string CreatedUtc { get; set; }
        public string UpdatedUtc { get; set; }
        public string SavedUtc { get; set; }
        public string RelayStartedUtc { get; set; }
        public string LoadingUtc { get; set; }
        public string LoadedUtc { get; set; }
        public string FailedUtc { get; set; }
        public string Error { get; set; }
        public int OriginProcessId { get; set; }
        public int ConsumerProcessId { get; set; }
    }

    public static partial class GameLaunchTools
    {
        private static readonly TimeSpan RestartIntentMaxAge = TimeSpan.FromMinutes(15);

        internal static GameRestartIntent ReadRestartIntent(out string error)
        {
            error = null;
            string path = OniMcpPaths.RestartIntentPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<GameRestartIntent>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                error = "restart intent read failed: " + ex.Message;
                return null;
            }
        }

        internal static void WriteRestartIntent(GameRestartIntent intent)
        {
            if (intent == null)
                throw new ArgumentNullException(nameof(intent));
            string path = OniMcpPaths.RestartIntentPath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            intent.UpdatedUtc = UtcNowText();
            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(intent, Formatting.Indented));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        internal static bool IsRestartIntentStale(GameRestartIntent intent)
        {
            if (intent == null || !System.DateTime.TryParse(intent.CreatedUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out System.DateTime created))
                return true;
            return System.DateTime.UtcNow - created > RestartIntentMaxAge;
        }

        internal static void FailRestartIntent(GameRestartIntent intent, string error)
        {
            if (intent == null)
                return;
            intent.Stage = "failed";
            intent.Error = error;
            intent.FailedUtc = UtcNowText();
            WriteRestartIntent(intent);
        }

        private static Dictionary<string, object> RestartIntentStatus(GameRestartIntent intent, string readError = null)
        {
            if (intent == null)
            {
                return new Dictionary<string, object>
                {
                    ["found"] = false,
                    ["stage"] = readError == null ? "none" : "failed",
                    ["error"] = readError
                };
            }
            return new Dictionary<string, object>
            {
                ["found"] = true,
                ["jobId"] = intent.JobId,
                ["exactSavePath"] = intent.ExactSavePath,
                ["resume"] = intent.Resume,
                ["stage"] = intent.Stage,
                ["createdUtc"] = intent.CreatedUtc,
                ["updatedUtc"] = intent.UpdatedUtc,
                ["savedUtc"] = intent.SavedUtc,
                ["relayStartedUtc"] = intent.RelayStartedUtc,
                ["loadingUtc"] = intent.LoadingUtc,
                ["loadedUtc"] = intent.LoadedUtc,
                ["failedUtc"] = intent.FailedUtc,
                ["error"] = intent.Error,
                ["originProcessId"] = intent.OriginProcessId,
                ["consumerProcessId"] = intent.ConsumerProcessId,
                ["stale"] = IsRestartIntentStale(intent)
            };
        }

        internal static bool ExactPathEquals(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;
            string a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(a, b, comparison);
        }

        internal static string UtcNowText() => System.DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
    }
}
