using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OniOnline.Multiplayer
{
    internal sealed class NativeDupeAuditSlice
    {
        internal string Type = "";
        internal int Offset = 0;
        internal int Length = 0;
    }

    internal sealed class NativeDupeAuditParameter
    {
        internal string Machine;
        internal string State;
        internal string Context;
        internal string Name;
        internal int Offset;
        internal int Length;
        internal int? TargetInstanceId;
    }

    internal sealed class NativeDupeIdRemapSlot
    {
        internal int Offset;
        internal int OldId;
        internal int NewId;
    }

    internal sealed class NativeDupeOpaqueAudit
    {
        internal int SourceMinionId;
        internal int SourceProxyId;
        internal int StateMachines;
        internal int StorageItems;
        internal int Sicknesses;
        internal byte NavType;
        internal readonly Dictionary<string, float> Amounts = new Dictionary<string, float>(StringComparer.Ordinal);
        internal readonly List<NativeDupeAuditParameter> Parameters = new List<NativeDupeAuditParameter>();
        internal readonly List<string> RemainingContracts = new List<string>();

        // Structural decoding is not a reference-closure proof, a load permit or an importer.
        internal bool ImportReady => false;

        internal NativeDupeIdRemapSlot[] PlanStateMachineTargetRemap(IReadOnlyDictionary<int, int> ids)
        {
            if (ids == null || ids.Count != 2 || !ids.TryGetValue(SourceMinionId, out int minion) ||
                !ids.TryGetValue(SourceProxyId, out int proxy) || minion <= 0 || proxy <= 0 || minion == proxy)
                throw new InvalidDataException("archive_exact_two_root_id_map_required");
            var result = new List<NativeDupeIdRemapSlot>();
            foreach (var parameter in Parameters)
            {
                if (!parameter.TargetInstanceId.HasValue || parameter.TargetInstanceId.Value == 0) continue;
                int oldId = parameter.TargetInstanceId.Value;
                if (!ids.TryGetValue(oldId, out int newId)) throw new InvalidDataException("archive_external_sm_target");
                result.Add(new NativeDupeIdRemapSlot { Offset = parameter.Offset, OldId = oldId, NewId = newId });
            }
            // A plan only: never replace arbitrary matching integers in native bytes.
            return result.ToArray();
        }
    }

    // Reads pinned ONI 744825 bytes without loading game types, resolving Ref.Get, invoking callbacks,
    // instantiating objects or touching the native serializer's global template directory.
    internal static class NativeDuplicantImportAudit
    {
        private const int MaxRootBytes = 2 * 1024 * 1024;
        private const int MaxSchemaBytes = 4 * 1024 * 1024;
        private sealed class FieldType { internal byte Code; }
        private sealed class Template { internal readonly List<FieldType> Fields = new List<FieldType>(); }

        internal static NativeDupeOpaqueAudit Read(int build, byte[] schema, byte[] root,
            NativeDupeAuditSlice[] slices, int sourceMinionId, int sourceProxyId)
        {
            if (build != 744825 || sourceMinionId <= 0 || sourceProxyId <= 0 || sourceMinionId == sourceProxyId)
                throw new InvalidDataException("archive_pinned_build_and_two_roots_required");
            if (root == null || root.Length > MaxRootBytes || slices == null || slices.Length != 4)
                throw new InvalidDataException("archive_audit_bounds");
            var templates = ReadSchema(schema);
            var result = new NativeDupeOpaqueAudit { SourceMinionId = sourceMinionId, SourceProxyId = sourceProxyId };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ranges = new List<System.Tuple<int, int>>();
            foreach (var slice in slices)
            {
                if (slice == null || slice.Type == null || !seen.Add(slice.Type)) throw new InvalidDataException("archive_duplicate_detail_block");
                var reader = new Reader(root, slice.Offset, slice.Length);
                if (ranges.Any(r => slice.Offset < r.Item2 && reader.End > r.Item1)) throw new InvalidDataException("archive_overlapping_detail_block");
                ranges.Add(System.Tuple.Create(slice.Offset, reader.End));
                SkipFields(reader, templates, slice.Type);
                switch (slice.Type)
                {
                    case "StateMachineController": ReadStateMachines(reader, templates, result); break;
                    case "Navigator": ReadNavigator(reader, result); break;
                    case "Storage":
                        result.StorageItems = reader.Count(256);
                        if (result.StorageItems != 0) throw new InvalidDataException("archive_nonempty_storage_requires_nested_root_audit");
                        break;
                    case "MinionModifiers": ReadModifiers(reader, templates, result); break;
                    default: throw new InvalidDataException("archive_unknown_detail_block");
                }
                reader.Finished();
            }
            if (seen.Count != 4) throw new InvalidDataException("archive_four_detail_blocks_required");
            result.RemainingContracts.Add("all_other_root_components_and_external_assignment_owners");
            result.RemainingContracts.Add("plain_integer_cell_world_and_absolute_time_semantics");
            result.RemainingContracts.Add("native_inactive_construction_before_awake_and_prefab_callbacks");
            result.RemainingContracts.Add("two_phase_source_removal_destination_commit_and_reload_deduplication");
            return result;
        }

        private static Dictionary<string, Template> ReadSchema(byte[] bytes)
        {
            if (bytes == null || bytes.Length > MaxSchemaBytes) throw new InvalidDataException("archive_schema_bounds");
            var reader = new Reader(bytes, 0, bytes.Length);
            var result = new Dictionary<string, Template>(StringComparer.Ordinal);
            int count = reader.Count(4096);
            for (int i = 0; i < count; i++)
            {
                string name = reader.RequiredString();
                if (result.ContainsKey(name)) throw new InvalidDataException("archive_duplicate_schema_type");
                int fields = reader.Count(512), properties = reader.Count(512);
                var template = new Template();
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < fields + properties; j++)
                {
                    if (!names.Add(reader.RequiredString())) throw new InvalidDataException("archive_duplicate_schema_member");
                    template.Fields.Add(ReadType(reader, 0));
                }
                result.Add(name, template);
            }
            reader.Finished();
            return result;
        }

        private static FieldType ReadType(Reader reader, int depth)
        {
            if (depth > 16) throw new InvalidDataException("archive_schema_type_depth");
            byte code = reader.Byte();
            int kind = code & 63;
            if (kind > 23) throw new InvalidDataException("archive_unknown_serialization_type");
            if ((code & 128) != 0)
            {
                if (kind == 0) reader.RequiredString();
                int count = reader.Byte();
                if (count == 0 || count > 16) throw new InvalidDataException("archive_generic_arity");
                for (int i = 0; i < count; i++) ReadType(reader, depth + 1);
            }
            else if (kind == 17) ReadType(reader, depth + 1);
            else if (kind == 0 || kind == 13) reader.RequiredString();
            return new FieldType { Code = code };
        }

        private static void SkipFields(Reader reader, Dictionary<string, Template> templates, string type)
        {
            if (!templates.TryGetValue(type, out var template)) throw new InvalidDataException("archive_missing_schema_template:" + type);
            foreach (var field in template.Fields)
            {
                int kind = field.Code & 63;
                switch (kind)
                {
                    case 1: case 2: case 3: reader.Skip(1); break;
                    case 4: case 5: reader.Skip(2); break;
                    case 6: case 7: case 10: case 13: case 23: reader.Skip(4); break;
                    case 8: case 9: case 11: case 14: case 15: reader.Skip(8); break;
                    case 16: reader.Skip(12); break;
                    case 0: case 12: case 18:
                    {
                        int length = reader.Int();
                        if (length < -1) throw new InvalidDataException("archive_invalid_nullable_length");
                        if (length >= 0) reader.Skip(length);
                        break;
                    }
                    case 17: case 19: case 20: case 21: case 22:
                    {
                        int length = reader.Int();
                        int count = reader.Int();
                        if (length < 0 || count < -1 || count > 65536 || (count <= 0 && length != 0))
                            throw new InvalidDataException("archive_invalid_collection_envelope");
                        reader.Skip(length);
                        break;
                    }
                    default: throw new InvalidDataException("archive_unknown_field_width");
                }
            }
        }

        private static void ReadStateMachines(Reader reader, Dictionary<string, Template> templates, NativeDupeOpaqueAudit result)
        {
            if (reader.Int() != 20) throw new InvalidDataException("archive_sm_version");
            var body = reader.Subreader(reader.Int());
            result.StateMachines = body.Count(256);
            for (int i = 0; i < result.StateMachines; i++)
            {
                if (body.Int() != 0) throw new InvalidDataException("archive_sm_entry_version");
                string type = body.RequiredString();
                body.String(); // Native serializationSuffix can be null.
                string state = body.RequiredString();
                var entry = body.Subreader(body.Int());
                int prefix = entry.Position;
                SkipFields(entry, templates, type);
                if (entry.Position != prefix) result.RemainingContracts.Add("sm_serialized_fields:" + type);
                if (entry.Position < entry.End)
                {
                    int parameters = entry.Count(256);
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    for (int j = 0; j < parameters; j++)
                    {
                        var parameter = entry.Subreader(entry.Int());
                        string context = parameter.RequiredString(), name = parameter.RequiredString();
                        if (!names.Add(name)) throw new InvalidDataException("archive_duplicate_sm_parameter");
                        var item = new NativeDupeAuditParameter { Machine = type, State = state, Context = context,
                            Name = name, Offset = parameter.Position, Length = parameter.End - parameter.Position };
                        if (context.StartsWith("StateMachine`4+TargetParameter+Context[[", StringComparison.Ordinal))
                        {
                            if (item.Length != 4) throw new InvalidDataException("archive_sm_target_width");
                            item.TargetInstanceId = parameter.Int();
                            if (item.TargetInstanceId != 0 && item.TargetInstanceId != result.SourceMinionId && item.TargetInstanceId != result.SourceProxyId)
                                result.RemainingContracts.Add("external_sm_target:" + item.TargetInstanceId.Value);
                        }
                        else parameter.Skip(item.Length);
                        result.Parameters.Add(item);
                        parameter.Finished();
                    }
                }
                entry.Finished();
            }
            body.Finished();
        }

        private static void ReadNavigator(Reader reader, NativeDupeOpaqueAudit result)
        {
            result.NavType = reader.Byte();
            if (result.NavType > 10) throw new InvalidDataException("archive_nav_type");
            int count = reader.Count(11);
            var seen = new HashSet<byte>();
            for (int i = 0; i < count; i++)
            {
                byte type = reader.Byte(); int distance = reader.Int();
                if (type > 10 || !seen.Add(type) || distance < 0) throw new InvalidDataException("archive_nav_distance");
            }
        }

        private static void ReadModifiers(Reader reader, Dictionary<string, Template> templates, NativeDupeOpaqueAudit result)
        {
            // Current AmountInstance has exactly one serialized Single. Schema drift must fail closed.
            if (!templates.TryGetValue("Klei.AI.AmountInstance", out var amount) || amount.Fields.Count != 1 || amount.Fields[0].Code != 10)
                throw new InvalidDataException("archive_amount_schema_changed");
            int count = reader.Count(128);
            for (int i = 0; i < count; i++)
            {
                string name = reader.RequiredString();
                var value = reader.Subreader(reader.Int());
                float number = value.Single(); value.Finished();
                if (float.IsNaN(number) || float.IsInfinity(number) || result.Amounts.ContainsKey(name))
                    throw new InvalidDataException("archive_invalid_amount");
                result.Amounts.Add(name, number);
            }
            result.Sicknesses = reader.Count(128);
            if (result.Sicknesses != 0) throw new InvalidDataException("archive_sickness_requires_deeper_audit");
        }

        private sealed class Reader
        {
            private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
            private readonly byte[] bytes;
            internal int Position { get; private set; }
            internal int End { get; }
            internal Reader(byte[] bytes, int offset, int length)
            {
                if (bytes == null || offset < 0 || length < 0 || offset > bytes.Length - length)
                    throw new InvalidDataException("archive_slice_bounds");
                this.bytes = bytes; Position = offset; End = offset + length;
            }
            internal void Skip(int length)
            {
                if (length < 0 || length > End - Position) throw new InvalidDataException("archive_truncated_payload");
                Position += length;
            }
            internal byte Byte() { if (Position == End) throw new InvalidDataException("archive_truncated_byte"); return bytes[Position++]; }
            internal int Int()
            {
                int start = Position; Skip(4);
                return bytes[start] | bytes[start + 1] << 8 | bytes[start + 2] << 16 | bytes[start + 3] << 24;
            }
            internal float Single()
            {
                int start = Position; Skip(4);
                if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(bytes, start);
                return BitConverter.ToSingle(new[] { bytes[start + 3], bytes[start + 2], bytes[start + 1], bytes[start] }, 0);
            }
            internal int Count(int max) { int n = Int(); if (n < 0 || n > max) throw new InvalidDataException("archive_count_bound"); return n; }
            internal string String()
            {
                int length = Int(); if (length == -1) return null;
                if (length < 0 || length > 16384) throw new InvalidDataException("archive_string_bound");
                int start = Position; Skip(length); return Utf8.GetString(bytes, start, length);
            }
            internal string RequiredString() { string s = String(); if (string.IsNullOrEmpty(s)) throw new InvalidDataException("archive_required_string"); return s; }
            internal Reader Subreader(int length) { int start = Position; Skip(length); return new Reader(bytes, start, length); }
            internal void Finished() { if (Position != End) throw new InvalidDataException("archive_unconsumed_payload"); }
        }
    }
}
