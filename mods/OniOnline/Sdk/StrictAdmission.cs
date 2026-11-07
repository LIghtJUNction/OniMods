using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Network;

namespace OniOnline.Sdk
{
    public sealed class StrictMod
    {
        public string Id { get; private set; }
        public string Version { get; private set; }
        public string Title { get; private set; }

        public StrictMod(string id, string version, string title)
        {
            if (!StrictEndpoint.ValidToken(id) || version == null || version.Length > 64 || title == null || title.Length > 128)
                throw new ArgumentException("invalid_strict_mod");
            Id = id;
            Version = version;
            Title = title;
        }

        internal static bool ValidToken(string value)
        { return value != null && value.Length > 0 && value.Length <= 128 && value.IndexOfAny(new[] { '\r', '\n' }) < 0; }
    }

    public sealed class StrictEndpoint
    {
        public string GameplayVersion { get; private set; }
        public string GameBuild { get; private set; }
        public string[] ActiveDlcIds { get; private set; }
        public StrictMod[] Mods { get; private set; }

        public StrictEndpoint(string gameplayVersion, string gameBuild, IEnumerable<string> activeDlcIds, IEnumerable<StrictMod> mods)
        {
            if (!Valid(gameplayVersion) || !Valid(gameBuild)) throw new ArgumentException("invalid_strict_endpoint");
            GameplayVersion = gameplayVersion;
            GameBuild = gameBuild;
            ActiveDlcIds = NormalizeDlc(activeDlcIds);
            Mods = NormalizeMods(mods);
        }

        private static bool Valid(string value)
        { return value != null && value.Length > 0 && value.Length <= 64; }

        private static string[] NormalizeDlc(IEnumerable<string> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                if (!Valid(value)) throw new ArgumentException("invalid_dlc_id");
                if (!unique.Add(value)) throw new ArgumentException("duplicate_dlc_id");
            }
            return unique.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static StrictMod[] NormalizeMods(IEnumerable<StrictMod> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var unique = new Dictionary<string, StrictMod>(StringComparer.Ordinal);
            foreach (StrictMod mod in values)
            {
                if (mod == null) throw new ArgumentException("invalid_strict_mod");
                if (unique.ContainsKey(mod.Id)) throw new ArgumentException("duplicate_mod_id");
                unique.Add(mod.Id, mod);
            }
            return unique.Values.OrderBy(mod => mod.Id, StringComparer.Ordinal).ToArray();
        }

        internal static bool ValidToken(string value)
        { return value != null && value.Length > 0 && value.Length <= 128 && value.IndexOfAny(new[] { '\r', '\n' }) < 0; }
    }

    public sealed class StrictAdmissionReport
    {
        public bool Accepted { get; private set; }
        public string[] Problems { get; private set; }
        public int Omitted { get; private set; }
        public string Summary { get; private set; }

        internal StrictAdmissionReport(bool accepted, string[] problems, int omitted, string summary)
        {
            Accepted = accepted;
            Problems = problems;
            Omitted = omitted;
            Summary = summary;
        }
    }

    // Opt-in comparison for a gameplay that shares one simulation. Chat never calls this.
    public static class StrictAdmission
    {
        public const int MaximumListedProblems = 10;

        public static string Gate(IEnumerable<string> negotiated, StrictEndpoint local, StrictEndpoint remote, string remoteName)
        {
            if (!SessionCapabilities.UsesStrictAdmission(negotiated)) return null;
            if (local == null || remote == null) return "strict_admission_missing";
            StrictAdmissionReport report = Evaluate(local, remote, remoteName ?? "remote");
            return report.Accepted ? null : report.Summary;
        }

        public static StrictAdmissionReport Evaluate(StrictEndpoint local, StrictEndpoint remote, string remoteName)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            if (remote == null) throw new ArgumentNullException(nameof(remote));
            if (string.IsNullOrEmpty(remoteName)) throw new ArgumentException("invalid_remote_name");
            var problems = new List<string>();
            if (!string.Equals(local.GameplayVersion, remote.GameplayVersion, StringComparison.Ordinal))
                problems.Add("gameplay version differs: this game " + local.GameplayVersion + ", " + remoteName + " " + remote.GameplayVersion);
            if (!string.Equals(local.GameBuild, remote.GameBuild, StringComparison.Ordinal))
                problems.Add("game build differs: this game " + local.GameBuild + ", " + remoteName + " " + remote.GameBuild);
            var localDlc = new HashSet<string>(local.ActiveDlcIds, StringComparer.Ordinal);
            var remoteDlc = new HashSet<string>(remote.ActiveDlcIds, StringComparer.Ordinal);
            foreach (string id in local.ActiveDlcIds)
                if (!remoteDlc.Contains(id)) problems.Add("DLC " + id + " is active only in this game");
            foreach (string id in remote.ActiveDlcIds)
                if (!localDlc.Contains(id)) problems.Add("DLC " + id + " is active only for the " + remoteName);
            var remoteMods = remote.Mods.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
            var localMods = local.Mods.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
            foreach (StrictMod mod in local.Mods)
            {
                StrictMod other;
                if (!remoteMods.TryGetValue(mod.Id, out other)) problems.Add("mod " + Label(mod) + " is active only in this game");
                else if (!string.Equals(mod.Version, other.Version, StringComparison.Ordinal))
                    problems.Add("mod " + Label(mod) + " version differs: this game " + Version(mod) + ", " + remoteName + " " + Version(other));
            }
            foreach (StrictMod mod in remote.Mods)
                if (!localMods.ContainsKey(mod.Id)) problems.Add("mod " + Label(mod) + " is active only for the " + remoteName);
            if (problems.Count == 0)
                return new StrictAdmissionReport(true, new string[0], 0, "The games match.");
            int shown = Math.Min(MaximumListedProblems, problems.Count);
            int omitted = problems.Count - shown;
            string summary = "Cannot play together: " + string.Join("; ", problems.Take(shown));
            if (omitted > 0) summary += "; and " + omitted + " more";
            return new StrictAdmissionReport(false, problems.Take(shown).ToArray(), omitted, summary + ".");
        }

        private static string Label(StrictMod mod)
        { return mod.Title.Length == 0 ? mod.Id : "'" + mod.Title + "' (" + mod.Id + ")"; }

        private static string Version(StrictMod mod)
        { return mod.Version.Length == 0 ? "(no version)" : mod.Version; }
    }
}
