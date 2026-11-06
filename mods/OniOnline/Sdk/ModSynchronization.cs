using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OniOnline.Sdk
{
    public enum ModSyncPolicy : byte { Required = 1, ClientOnly = 2, Optional = 3 }
    public enum ModDistributionSource : byte { Local = 1, SteamWorkshop = 2 }
    public enum ModSyncChangeKind { Missing, Disabled, SourceMismatch, VersionMismatch, ContentMismatch, Unexpected }
    public enum ModSyncComparisonMode { Strict = 1, AuthorityRequirementsOnly = 2 }

    public sealed class ModManifestEntry
    {
        public string Id { get; }
        public string Version { get; }
        public ModSyncPolicy Policy { get; }
        public ModDistributionSource Source { get; }
        public ulong WorkshopId { get; }
        public string ContentSha256 { get; }
        public bool Enabled { get; }

        public ModManifestEntry(string id, string version, ModSyncPolicy policy, ModDistributionSource source,
            ulong workshopId, string contentSha256, bool enabled = true)
        {
            if (!ModSyncValidation.Text(id, 128) || !ModSyncValidation.Text(version, 64) ||
                policy < ModSyncPolicy.Required || policy > ModSyncPolicy.Optional ||
                source < ModDistributionSource.Local || source > ModDistributionSource.SteamWorkshop ||
                source == ModDistributionSource.Local && workshopId != 0 ||
                source == ModDistributionSource.SteamWorkshop && workshopId == 0)
                throw new ArgumentException("invalid_mod_manifest_entry");
            if (contentSha256 == null) contentSha256 = "";
            if (contentSha256.Length != 0 && !ModSyncValidation.Sha256(contentSha256) ||
                policy == ModSyncPolicy.Required && contentSha256.Length == 0)
                throw new ArgumentException("invalid_mod_content_digest");
            Id = id; Version = version; Policy = policy; Source = source; WorkshopId = workshopId;
            ContentSha256 = contentSha256.ToLowerInvariant(); Enabled = enabled;
        }
    }

    public sealed class ModManifest
    {
        public const int MaximumEntries = 256;
        private readonly ReadOnlyCollection<ModManifestEntry> entries;
        public IReadOnlyList<ModManifestEntry> Entries { get { return entries; } }
        public string Fingerprint { get; }
        public ModManifest(IEnumerable<ModManifestEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var copy = new List<ModManifestEntry>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || copy.Count >= MaximumEntries || !ids.Add(entry.Id))
                    throw new ArgumentException("invalid_mod_manifest_collection");
                copy.Add(entry);
            }
            copy.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            this.entries = copy.AsReadOnly();
            Fingerprint = ModSyncValidation.Digest(ModManifestCodec.Encode(this));
        }
    }

    public sealed class ModSyncChange
    {
        public string Id { get { return (Expected ?? Installed).Id; } }
        public ModManifestEntry Expected { get; }
        public ModManifestEntry Installed { get; }
        public ModSyncChangeKind Kind { get; }
        // Retrieval availability is not evidence that a native loader has installed or enabled it.
        public bool CanDownload { get { return Expected != null && Expected.Source == ModDistributionSource.SteamWorkshop; } }
        public bool RequiresRestart { get { return true; } }
        internal ModSyncChange(ModManifestEntry expected, ModManifestEntry installed, ModSyncChangeKind kind)
        { Expected = expected; Installed = installed; Kind = kind; }
    }

    public sealed class ModSyncPlan
    {
        private readonly ReadOnlyCollection<ModSyncChange> changes;
        private readonly ReadOnlyCollection<ulong> downloads;
        public ModManifest RequiredManifest { get; }
        public ModManifest InstalledManifest { get; }
        public string RequiredFingerprint { get { return RequiredManifest.Fingerprint; } }
        public string InstalledFingerprint { get { return InstalledManifest.Fingerprint; } }
        public string Fingerprint { get; }
        public ModSyncComparisonMode ComparisonMode { get; }
        public IReadOnlyList<ModSyncChange> Changes { get { return changes; } }
        public IReadOnlyList<ulong> WorkshopItemsToDownload { get { return downloads; } }
        public bool CanJoin { get { return changes.Count == 0; } }
        internal ModSyncPlan(ModManifest required, ModManifest installed, IEnumerable<ModSyncChange> changes, ModSyncComparisonMode mode)
        {
            RequiredManifest = required; InstalledManifest = installed; ComparisonMode = mode;
            this.changes = new List<ModSyncChange>(changes).AsReadOnly();
            downloads = this.changes.Where(c => c.CanDownload).Select(c => c.Expected.WorkshopId)
                .Distinct().OrderBy(id => id).ToList().AsReadOnly();
            Fingerprint = ModSyncValidation.Digest(Encoding.UTF8.GetBytes("mod-sync-plan-v1\n" + ((int)mode).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + RequiredFingerprint + "\n" + InstalledFingerprint));
        }
        public bool IsCurrent(ModManifest required, ModManifest installed)
        { return required != null && installed != null && RequiredFingerprint == required.Fingerprint && InstalledFingerprint == installed.Fingerprint; }
    }

    public static class ModSyncPlanner
    {
        public static ModSyncPlan Compare(ModManifest required, ModManifest installed, ModSyncComparisonMode mode = ModSyncComparisonMode.Strict)
        {
            if (required == null || installed == null) throw new ArgumentNullException(required == null ? nameof(required) : nameof(installed));
            if (mode != ModSyncComparisonMode.Strict && mode != ModSyncComparisonMode.AuthorityRequirementsOnly) throw new ArgumentOutOfRangeException(nameof(mode));
            var available = installed.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            var changes = new List<ModSyncChange>();
            foreach (var expected in required.Entries)
            {
                // The authority's policy wins. A peer's ClientOnly flag cannot hide a required mismatch.
                if (expected.Policy != ModSyncPolicy.Required || !expected.Enabled) continue;
                available.TryGetValue(expected.Id, out var actual);
                ModSyncChangeKind? kind = actual == null ? ModSyncChangeKind.Missing
                    : !actual.Enabled ? ModSyncChangeKind.Disabled
                    : actual.Source != expected.Source || actual.WorkshopId != expected.WorkshopId ? ModSyncChangeKind.SourceMismatch
                    : actual.Version != expected.Version ? ModSyncChangeKind.VersionMismatch
                    : actual.ContentSha256 != expected.ContentSha256 ? ModSyncChangeKind.ContentMismatch
                    : (ModSyncChangeKind?)null;
                if (kind.HasValue) changes.Add(new ModSyncChange(expected, actual, kind.Value));
            }
            if (mode == ModSyncComparisonMode.Strict)
            {
                var requiredIds = new HashSet<string>(required.Entries.Where(entry => entry.Enabled && entry.Policy == ModSyncPolicy.Required)
                    .Select(entry => entry.Id), StringComparer.Ordinal);
                foreach (var extra in installed.Entries)
                    if (extra.Enabled && extra.Policy == ModSyncPolicy.Required && !requiredIds.Contains(extra.Id))
                        changes.Add(new ModSyncChange(null, extra, ModSyncChangeKind.Unexpected));
            }
            changes.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return new ModSyncPlan(required, installed, changes, mode);
        }
    }

    public static class ModManifestCodec
    {
        public const int MaximumEncodedBytes = 256 * 1024;
        private const int Magic = 0x464d4f4f;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static byte[] Encode(ModManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write((ushort)manifest.Entries.Count);
                foreach (var entry in manifest.Entries)
                {
                    WriteText(writer, entry.Id); WriteText(writer, entry.Version);
                    writer.Write((byte)entry.Policy); writer.Write((byte)entry.Source); writer.Write(entry.WorkshopId);
                    WriteText(writer, entry.ContentSha256); writer.Write((byte)(entry.Enabled ? 1 : 0));
                }
                if (stream.Length > MaximumEncodedBytes) throw new InvalidDataException("mod_manifest_budget");
                return stream.ToArray();
            }
        }
        public static ModManifest Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes.Length > MaximumEncodedBytes) throw new InvalidDataException("mod_manifest_budget");
            try
            {
                using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadByte() != 1) throw new InvalidDataException("mod_manifest_version");
                    int count = reader.ReadUInt16();
                    if (count > ModManifest.MaximumEntries) throw new InvalidDataException("mod_manifest_count");
                    var entries = new List<ModManifestEntry>();
                    for (int i = 0; i < count; i++)
                    {
                        string id = ReadText(reader, 128), version = ReadText(reader, 64);
                        var policy = (ModSyncPolicy)reader.ReadByte(); var source = (ModDistributionSource)reader.ReadByte();
                        ulong workshop = reader.ReadUInt64(); string digest = ReadText(reader, 64); byte enabled = reader.ReadByte();
                        if (enabled > 1) throw new InvalidDataException("mod_manifest_enabled");
                        entries.Add(new ModManifestEntry(id, version, policy, source, workshop, digest, enabled == 1));
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("mod_manifest_trailing_data");
                    return new ModManifest(entries);
                }
            }
            catch (Exception error) when (error is ArgumentException || error is EndOfStreamException || error is FormatException)
            { throw new InvalidDataException("invalid_mod_manifest", error); }
        }
        private static void WriteText(BinaryWriter writer, string text)
        { byte[] bytes = Utf8.GetBytes(text); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        private static string ReadText(BinaryReader reader, int characters)
        {
            int length = reader.ReadUInt16();
            if (length > characters * 4) throw new InvalidDataException("mod_manifest_text_budget");
            byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            string text = Utf8.GetString(bytes); if (text.Length > characters) throw new InvalidDataException("mod_manifest_text_budget");
            return text;
        }
    }

    internal static class ModSyncValidation
    {
        internal static bool Text(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsControl(c)) return false;
                if (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[++i]))) return false;
            }
            return true;
        }
        internal static bool Sha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')) return false;
            return true;
        }
        internal static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
