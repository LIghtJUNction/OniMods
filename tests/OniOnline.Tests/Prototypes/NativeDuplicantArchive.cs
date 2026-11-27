using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using KSerialization;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    internal sealed class NativeDuplicantArchivePermit
    {
        internal string OwnedSaveDirectory = "";
        internal string ExpectedActiveSave = "";
        internal string ExpectedOwnerId = "";
        internal string ExpectedPlanetId = "";
        internal int ExpectedSelectedInstanceId = -1;
        internal int[] OriginalStarterIds = Array.Empty<int>();
        // Supplied only by the probe's existing disposable-colony guard, never by a UI text field.
        internal bool IsDisposableProbeColony = false;
    }

    internal sealed class NativeDuplicantArchiveResult
    {
        internal NativeDuplicantArchiveManifest Manifest;
        internal byte[][] NativeRoots;
        internal byte[] NativeSchemaDirectory;
    }
    [Serializable] internal sealed class NativeDuplicantArchiveManifest
    {
        public int Version = 2;
        public string Scope = "native-read-only-export; not an activated import or completed migration";
        public string NativeBuild;
        public string SourceOwnerId;
        public string SourcePlanetId;
        public string SourceDupeOriginId;
        public string DupeIdentityScheme = "source-owner-planet-native-instance-v1";
        // The legacy label above contains a native ID. Keep the immutable origin
        // separately so a future import cannot assign a visiting dupe to its host.
        public NativeArchiveDupeOrigin OriginalOrigin;
        public string SourceName;
        public string SourceActiveSave;
        public int SourceWorldId;
        public double SourceTime;
        public double SourceCycle;
        // Sample the native fractional clock directly. At later cycles, rounded
        // global seconds cannot reconstruct GetTimeInCycles() exactly.
        public double SourceTimeInCycles;
        public string SchemaSha256;
        public int SchemaBytes;
        public NativeArchiveRoot[] Roots;
        public NativeArchiveAssembly[] Assemblies;
        public NativeArchiveReference[] KnownReferences;
        public NativeArchiveReference[] KnownExternalReferences;
        public string[] BlockingReasons;
        public bool ReferenceInventoryComplete;
        public bool ImportReady;
        public bool PhysicalFingerprintUnchanged;
        public string BeforeFingerprint;
        public string AfterFingerprint;
    }
    [Serializable] internal sealed class NativeArchiveDupeOrigin
    {
        public string StableDupeId;
        public string OriginalOwnerId;
        public string OriginPlanetId;
        public string OriginHomeSystemId;
        public string Kind;
    }
    [Serializable] internal sealed class NativeArchiveRoot
    {
        public string Role;
        public int InstanceId;
        public string Prefab;
        public int NativeBytes;
        public string Sha256;
        public string[] SerializedComponents;
        public NativeArchiveBlock[] NativeBlocks;
        public string[] SaveCallbacks;
    }
    [Serializable] internal sealed class NativeArchiveBlock
    { public string Type; public int Offset; public int Length; public bool OpaqueManagerTail; }
    [Serializable] internal sealed class NativeArchiveReference
    { public string Path; public string Kind; public int TargetInstanceId; public bool WithinCapturedRoots; public bool Resolves; public string TargetPrefab; }
    [Serializable] internal sealed class NativeArchiveAssembly
    { public string Name; public string Version; public string Sha256; }

    // Exact native SaveLoadRoot bytes are exported. This deliberately has no Load/Spawn/Delete API.
    internal static class NativeDuplicantArchive
    {
        internal const int MaxRootBytes = 2 * 1024 * 1024;
        internal const int MaxSchemaBytes = 4 * 1024 * 1024;
        private static readonly Assembly NativeAssembly = typeof(MinionIdentity).Assembly;
        private static readonly Assembly FrameworkAssembly = typeof(KMonoBehaviour).Assembly;
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static NativeDuplicantArchiveResult CaptureSelected(NativeDuplicantArchivePermit permit)
        {
            MinionIdentity selected = ValidatePermit(permit);
            var identity = selected.GetComponent<KPrefabID>();
            // Read the stored Ref integer directly. Get()/GetId() may repair or clear missing refs.
            int proxyId = ReadRefId(selected.assignableProxy);
            var proxyIdentity = proxyId < 0 ? null : KPrefabIDTracker.Get().GetInstance(proxyId);
            var proxy = proxyIdentity == null ? null : proxyIdentity.GetComponent<MinionAssignablesProxy>();
            if (proxy == null || proxy.TargetInstanceID != identity.InstanceID)
                throw new InvalidOperationException("archive_proxy_not_exactly_bound");
            var roots = new[] { selected.GetComponent<SaveLoadRoot>(), proxy.GetComponent<SaveLoadRoot>() };
            if (roots.Any(r => r == null) || roots[0] == roots[1]) throw new InvalidOperationException("archive_two_native_roots_required");
            OniOnline.Core.DupeOriginRecord origin;
            var colony = OnlineColonyState.Current;
            if (colony == null || !colony.TryResolveDuplicantOrigin(identity.InstanceID, selected.GetMyWorldId(), out origin))
                throw new InvalidOperationException("archive_dupe_original_origin_unresolved");
            var components = roots.Select(r => r.GetComponents<KMonoBehaviour>().Where(Saved).ToArray()).ToArray();
            if (components.Any(c => c.Length > 128) || components.SelectMany(c => c).Any(c => !TrustedNativeType(c.GetType())))
                throw new InvalidOperationException("archive_unreviewed_mod_component_save_callback");

            var before = Fingerprint(selected, proxy, roots);
            var rootIds = new HashSet<int>(roots.Select(r => r.GetComponent<KPrefabID>().InstanceID));
            var blockers = new HashSet<string>(StringComparer.Ordinal) { "external_reference_closure_not_yet_import_validated", "plain_integer_id_cell_time_contracts_not_complete" };
            var references = new List<NativeArchiveReference>();
            var walker = new SerializedGraphInventory(rootIds, references, blockers);
            var native = new byte[2][];
            var rootInfo = new NativeArchiveRoot[2];
            for (int i = 0; i < roots.Length; i++)
            {
                foreach (var component in components[i]) walker.Visit(component, "root[" + i + "]." + component.GetType().FullName, 0, true);
                using (var stream = new BoundedStream(MaxRootBytes))
                using (var writer = new BinaryWriter(stream))
                {
                    roots[i].Save(writer); writer.Flush(); if (stream.LimitExceeded) throw new InvalidDataException("archive_byte_bound"); native[i] = stream.ToArray();
                }
                var savedComponents = components[i].Select(c => c.GetType().ToString()).ToArray();
                rootInfo[i] = new NativeArchiveRoot { Role = i == 0 ? "living-minion" : "assignables-proxy", InstanceId = roots[i].GetComponent<KPrefabID>().InstanceID,
                    Prefab = roots[i].GetComponent<KPrefabID>().GetSaveLoadTag().Name, NativeBytes = native[i].Length, Sha256 = Hash(native[i]), SerializedComponents = savedComponents,
                    NativeBlocks = DescribeBlocks(native[i], savedComponents, blockers), SaveCallbacks = components[i].SelectMany(Callbacks).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray() };
            }
            byte[] schema;
            using (var stream = new BoundedStream(MaxSchemaBytes))
            using (var writer = new BinaryWriter(stream))
            { KSerialization.Manager.SerializeDirectory(writer); writer.Flush(); if (stream.LimitExceeded) throw new InvalidDataException("archive_schema_byte_bound"); schema = stream.ToArray(); }
            var after = Fingerprint(selected, proxy, roots);
            var manifest = new NativeDuplicantArchiveManifest { NativeBuild = typeof(KleiVersion).GetField("ChangeList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue().ToString(),
                SourceOwnerId = permit.ExpectedOwnerId, SourcePlanetId = permit.ExpectedPlanetId,
                SourceDupeOriginId = "oni-dupe-origin:" + permit.ExpectedOwnerId + ":" + permit.ExpectedPlanetId + ":" + identity.InstanceID,
                OriginalOrigin = new NativeArchiveDupeOrigin { StableDupeId = origin.StableDupeId, OriginalOwnerId = origin.OriginalOwnerId,
                    OriginPlanetId = origin.OriginPlanetId, OriginHomeSystemId = origin.OriginHomeSystemId, Kind = origin.Kind.ToString() },
                SourceName = selected.GetProperName(), SourceActiveSave = Path.GetFullPath(SaveLoader.GetActiveSaveFilePath()), SourceWorldId = selected.GetMyWorldId(),
                SourceTime = GameClock.Instance.GetTime(), SourceCycle = GameClock.Instance.GetCycle(),
                SourceTimeInCycles = GameClock.Instance.GetTimeInCycles(), Roots = rootInfo, SchemaBytes = schema.Length, SchemaSha256 = Hash(schema),
                Assemblies = AssemblyFootprint(components.SelectMany(c => c).Select(c => c.GetType().Assembly)), KnownReferences = references.ToArray(),
                KnownExternalReferences = references.Where(r => !r.WithinCapturedRoots).ToArray(),
                BlockingReasons = blockers.OrderBy(s => s, StringComparer.Ordinal).ToArray(), ReferenceInventoryComplete = false, ImportReady = false,
                PhysicalFingerprintUnchanged = before == after, BeforeFingerprint = before, AfterFingerprint = after };
            if (!manifest.PhysicalFingerprintUnchanged) throw new InvalidOperationException("archive_native_save_changed_observed_source_state");
            ValidatePermit(permit);
            return new NativeDuplicantArchiveResult { Manifest = manifest, NativeRoots = native, NativeSchemaDirectory = schema };
        }

        private static MinionIdentity ValidatePermit(NativeDuplicantArchivePermit permit)
        {
            if (permit == null || !permit.IsDisposableProbeColony || Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() ||
                SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused || SelectTool.Instance == null || SelectTool.Instance.selected == null)
                throw new InvalidOperationException("archive_requires_paused_selected_probe_starter");
            if (permit.OriginalStarterIds == null || permit.OriginalStarterIds.Length != 3 || permit.OriginalStarterIds.Distinct().Count() != 3 ||
                !permit.OriginalStarterIds.Contains(permit.ExpectedSelectedInstanceId)) throw new InvalidOperationException("archive_starter_allowlist");
            string root = Path.GetFullPath(permit.OwnedSaveDirectory ?? "").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string save = Path.GetFullPath(permit.ExpectedActiveSave ?? "");
            if (!Path.IsPathRooted(permit.OwnedSaveDirectory) || !Path.IsPathRooted(permit.ExpectedActiveSave) || !save.StartsWith(root, StringComparison.Ordinal) ||
                root.IndexOf("OniOnlineHarness-", StringComparison.Ordinal) < 0 || !File.Exists(save) || Path.GetFullPath(SaveLoader.GetActiveSaveFilePath()) != save)
                throw new InvalidOperationException("archive_active_save_not_exact_owned_fixture");
            var manifest = OnlineColonyState.Current?.CapturePlanet();
            if (manifest == null || manifest.PlanetId != permit.ExpectedPlanetId || manifest.OwnerId != permit.ExpectedOwnerId)
                throw new InvalidOperationException("archive_persistent_owner_planet_mismatch");
            var minion = SelectTool.Instance.selected.GetComponent<MinionIdentity>();
            if (minion == null || !Components.LiveMinionIdentities.Items.Contains(minion) || minion.GetComponent<KPrefabID>().InstanceID != permit.ExpectedSelectedInstanceId ||
                minion.GetMyWorldId() != manifest.NativeWorldId) throw new InvalidOperationException("archive_not_exact_live_selected_starter");
            return minion;
        }

        private static bool Saved(KMonoBehaviour c)
        { return c != null && !c.GetType().IsDefined(typeof(SkipSaveFileSerialization), false); }
        private static bool TrustedNativeType(Type type)
        { return type.Assembly == NativeAssembly || type.Assembly == FrameworkAssembly; }
        private static int ReadRefId(object value)
        {
            if (value == null) return -1;
            return (int)value.GetType().GetField("id", Fields).GetValue(value);
        }
        private static IEnumerable<string> Callbacks(KMonoBehaviour component)
        {
            var template = KSerialization.Manager.GetSerializationTemplate(component.GetType());
            if (template.onSerializing != null) yield return template.onSerializing.DeclaringType.FullName + "." + template.onSerializing.Name;
            if (template.onSerialized != null) yield return template.onSerialized.DeclaringType.FullName + "." + template.onSerialized.Name;
            if (template.customSerialize != null) yield return template.customSerialize.DeclaringType.FullName + "." + template.customSerialize.Name;
            if (component is ISaveLoadableDetails) yield return component.GetType().FullName + ":ISaveLoadableDetails.Serialize";
        }
        private static NativeArchiveBlock[] DescribeBlocks(byte[] bytes, string[] nativeTypes, HashSet<string> blockers)
        {
            var reader = new FastReader(bytes);
            reader.ReadVector3(); reader.ReadQuaternion(); reader.ReadVector3(); reader.ReadByte();
            int count = reader.ReadInt32();
            if (count < 0 || count > 256) throw new InvalidDataException("archive_native_component_count");
            var blocks = new List<NativeArchiveBlock>();
            for (int i = 0; i < count; i++)
            {
                string type = reader.ReadKleiString();
                if (!nativeTypes.Contains(type))
                {
                    blockers.Add("opaque_component_manager_tail:" + type);
                    blocks.Add(new NativeArchiveBlock { Type = type, Offset = reader.Position, Length = bytes.Length - reader.Position, OpaqueManagerTail = true });
                    break;
                }
                int length = reader.ReadInt32();
                if (length < 0 || (long)reader.Position + length > bytes.Length) throw new InvalidDataException("archive_native_component_size");
                blocks.Add(new NativeArchiveBlock { Type = type, Offset = reader.Position, Length = length, OpaqueManagerTail = false }); reader.SkipBytes(length);
            }
            return blocks.ToArray();
        }
        private static string Fingerprint(MinionIdentity minion, MinionAssignablesProxy proxy, SaveLoadRoot[] roots)
        {
            var values = new List<string> { GameClock.Instance.GetTime().ToString("R", CultureInfo.InvariantCulture), minion.GetProperName(), minion.transform.position.ToString("R"),
                minion.GetMyWorldId().ToString(CultureInfo.InvariantCulture), proxy.TargetInstanceID.ToString(CultureInfo.InvariantCulture),
                string.Join(",", roots.Select(r => r.GetComponent<KPrefabID>().InstanceID)),
                string.Join(",", Components.LiveMinionIdentities.Items.Select(m => m.GetComponent<KPrefabID>().InstanceID).OrderBy(id => id)) };
            var amounts = minion.GetComponent<MinionModifiers>()?.amounts;
            if (amounts != null) values.AddRange(amounts.ModifierList.Where(a => a != null && a.amount != null).Select(a => a.amount.Id + "=" + a.value.ToString("R", CultureInfo.InvariantCulture)).OrderBy(s => s, StringComparer.Ordinal));
            return Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", values)));
        }
        private static NativeArchiveAssembly[] AssemblyFootprint(IEnumerable<Assembly> components)
        {
            var assemblies = components.Concat(new[] { typeof(ModInfo).Assembly }).Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic &&
                !string.IsNullOrEmpty(a.Location) && a.Location.Replace('\\', '/').IndexOf("/mods/", StringComparison.OrdinalIgnoreCase) >= 0)).Distinct().ToArray();
            if (assemblies.Length > 64) throw new InvalidOperationException("archive_assembly_footprint_bound");
            return assemblies.OrderBy(a => a.GetName().Name, StringComparer.Ordinal).Select(a => new NativeArchiveAssembly { Name = a.GetName().Name,
                Version = a.GetName().Version.ToString(), Sha256 = HashFile(a.Location) }).ToArray();
        }
        private static string HashFile(string file) { using (var sha = SHA256.Create()) using (var input = File.OpenRead(file)) return Hex(sha.ComputeHash(input)); }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        private sealed class BoundedStream : MemoryStream
        {
            private readonly int max;
            internal bool LimitExceeded { get; private set; }
            internal BoundedStream(int max) : base(32768) { this.max = max; }
            public override void Write(byte[] buffer, int offset, int count) { if (count < 0 || Position > max - (long)count) { LimitExceeded = true; throw new InvalidDataException("archive_byte_bound"); } base.Write(buffer, offset, count); }
            public override void WriteByte(byte value) { if (Position >= max) { LimitExceeded = true; throw new InvalidDataException("archive_byte_bound"); } base.WriteByte(value); }
            public override void SetLength(long value) { if (value < 0 || value > max) { LimitExceeded = true; throw new InvalidDataException("archive_byte_bound"); } base.SetLength(value); }
        }

        private sealed class SerializedGraphInventory
        {
            private readonly HashSet<int> roots;
            private readonly List<NativeArchiveReference> references;
            private readonly HashSet<string> blockers;
            private readonly HashSet<object> visited = new HashSet<object>(new IdentityComparer());
            private int nodes;
            internal SerializedGraphInventory(HashSet<int> roots, List<NativeArchiveReference> references, HashSet<string> blockers)
            { this.roots = roots; this.references = references; this.blockers = blockers; }
            internal void Visit(object value, string path, int depth, bool nativeRoot = false)
            {
                if (value == null) return;
                if (++nodes > 12000 || depth > 24) { blockers.Add("serialized_graph_traversal_bound"); return; }
                Type type = value.GetType();
                if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)) return;
                if (!type.IsValueType && !visited.Add(value)) return;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Ref<>)) { Reference(path, "Ref<" + type.GetGenericArguments()[0].Name + ">", ReadRefId(value)); return; }
                if (!nativeRoot && value is UnityEngine.Object) { blockers.Add("direct_unity_object:" + path); return; }
                if (value is IDictionary)
                {
                    if (type.Assembly != typeof(Dictionary<,>).Assembly) { blockers.Add("unreviewed_dictionary:" + path); return; }
                    int index = 0; foreach (DictionaryEntry entry in (IDictionary)value) { Visit(entry.Key, path + ".key[" + index + "]", depth + 1); Visit(entry.Value, path + ".value[" + index++ + "]", depth + 1); if (index > 512) { blockers.Add("collection_bound:" + path); break; } } return;
                }
                if (value is IList)
                {
                    if (!type.IsArray && type.Assembly != typeof(List<>).Assembly) { blockers.Add("unreviewed_list:" + path); return; }
                    IList list = (IList)value; for (int i = 0; i < Math.Min(list.Count, 512); i++) Visit(list[i], path + "[" + i + "]", depth + 1);
                    if (list.Count > 512) blockers.Add("collection_bound:" + path); return;
                }
                if (!TrustedNativeType(type)) { blockers.Add("opaque_serialized_type:" + type.FullName); return; }
                if (value is ISaveLoadableDetails) blockers.Add("opaque_save_details:" + type.FullName);
                var template = KSerialization.Manager.GetSerializationTemplate(type);
                foreach (var field in template.serializableFields)
                {
                    object child = field.field.GetValue(value);
                    string name = path + "." + field.field.Name;
                    if (type == typeof(MinionAssignablesProxy) && field.field.Name == "target_instance_id") Reference(name, "plain_target_instance_id", (int)child);
                    else Visit(child, name, depth + 1);
                }
                foreach (var property in template.serializableProperties)
                {
                    FieldInfo backing = property.property.DeclaringType.GetField("<" + property.property.Name + ">k__BackingField", Fields);
                    if (backing == null) blockers.Add("uninvoked_serialized_property:" + type.FullName + "." + property.property.Name);
                    else Visit(backing.GetValue(value), path + "." + property.property.Name, depth + 1);
                }
            }
            private void Reference(string path, string kind, int id)
            {
                if (id < 0) return;
                if (references.Count >= 512) { blockers.Add("reference_inventory_bound"); return; }
                var target = KPrefabIDTracker.Get().GetInstance(id);
                references.Add(new NativeArchiveReference { Path = path, Kind = kind, TargetInstanceId = id, WithinCapturedRoots = roots.Contains(id),
                    Resolves = target != null, TargetPrefab = target == null ? "" : target.GetSaveLoadTag().Name });
            }
            private sealed class IdentityComparer : IEqualityComparer<object>
            { public new bool Equals(object a, object b) { return ReferenceEquals(a, b); } public int GetHashCode(object obj) { return RuntimeHelpers.GetHashCode(obj); } }
        }
    }
}
