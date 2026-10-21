using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OniOnline.Multiplayer
{
    internal sealed class NativeDupeRemapContext
    {
        internal int ArchiveVersion = 1;
        internal int Build = 744825;
        internal string GameAssemblyHash = "";
        internal string FrameworkAssemblyHash = "";
        internal int SourceMinionId = -1;
        internal int SourceProxyId = -1;
        internal int DestinationMinionId = -1;
        internal int DestinationProxyId = -1;
        internal int[] ExistingDestinationIds = Array.Empty<int>();
        internal float SourceSeconds = 0;
        internal int SourceWholeCycle = 0;
        internal float SourceFractionalCycles = 0;
        internal float DestinationSeconds = 0;
        internal int DestinationWholeCycle = 0;
        internal float DestinationFractionalCycles = 0;
        internal float DestinationX = 0;
        internal float DestinationY = 0;
        internal int DestinationWorldId = -1;
        internal int DestinationWorldX = 0;
        internal int DestinationWorldY = 0;
        internal int DestinationWorldWidth = 0;
        internal int DestinationWorldHeight = 0;
    }

    internal sealed class NativeDupeByteEdit
    {
        internal int Root = -1;
        internal int Offset = -1;
        internal string Path = "";
        internal string Semantic = "";
        internal byte[] Expected = Array.Empty<byte>();
        internal byte[] Replacement = Array.Empty<byte>();
    }

    internal sealed class NativeDupeMappedField
    {
        internal int Root = -1;
        internal int Offset = -1;
        internal int Length = 0;
        internal string Path = "";
        internal string Semantic = "";
        internal string DisplayValue = "";
    }

    internal sealed class NativeDupeRemapPlan
    {
        internal string[] RootHashes = Array.Empty<string>();
        internal string[] RootPrefabNames = Array.Empty<string>();
        internal string SchemaHash = "";
        internal int[] RootBytesFullyConsumed = Array.Empty<int>();
        internal NativeDupeByteEdit[] Edits = Array.Empty<NativeDupeByteEdit>();
        internal NativeDupeMappedField[] Fields = Array.Empty<NativeDupeMappedField>();
        internal int Components = 0;
        internal int ObjectIdSlots = 0;
        internal int StateMachines = 0;
        internal string[] UsedTemplates = Array.Empty<string>();
        internal bool BinaryLayoutComplete = false;
        internal bool ImportReady => false;
    }

    // Plans byte edits only. No native types, global serializer directory, filesystem access,
    // deserialization callbacks, object creation, activation or deletion occur in this helper.
    internal static partial class NativeDuplicantRemapPlanner
    {
        internal const string GameHash = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        internal const string FrameworkHash = "117fcc6962eeed82b4929347b9a466fb41e807a01b5a9a7acc8bdc446da276c7";
        // Beyond this reviewed range native float seconds lose finer-than-eight-second
        // precision. Both adjacent 600-second boundaries stay exactly representable here.
        private const int MaxClockWholeCycle = 131071;
        private const string ProxyRef = "Ref`1[[MinionAssignablesProxy, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]]";

        internal static NativeDupeRemapPlan Plan(byte[] schema, byte[][] roots, NativeDupeRemapContext context)
        {
            ValidateContext(context);
            if (schema == null || schema.Length > 4 * 1024 * 1024 || roots == null || roots.Length != 2 ||
                roots.Any(r => r == null || r.Length > 2 * 1024 * 1024)) throw Invalid("bounded_two_roots_and_schema_required");
            var parser = new Parser(schema, roots, context);
            return parser.Read();
        }

        // Validate a separately captured destination directory without ever replacing native
        // KSerialization.Manager state with a remote client's full schema directory.
        internal static void RequireDestinationSchemaCompatibility(NativeDupeRemapPlan plan, byte[] destinationDirectory)
        {
            if (plan == null || !plan.BinaryLayoutComplete || plan.UsedTemplates == null || plan.UsedTemplates.Length == 0 ||
                plan.UsedTemplates.Length > 256 || destinationDirectory == null || destinationDirectory.Length > 4 * 1024 * 1024)
                throw Invalid("complete_plan_and_bounded_destination_directory_required");
            new Parser(destinationDirectory, Array.Empty<byte[]>(), null).CheckDestination(plan.UsedTemplates);
        }

        private static void ValidateContext(NativeDupeRemapContext c)
        {
            if (c == null || c.Build != 744825 || c.GameAssemblyHash != GameHash || c.FrameworkAssemblyHash != FrameworkHash)
                throw Invalid("pinned_native_fingerprints_required");
            if (c.SourceMinionId <= 0 || c.SourceProxyId <= 0 || c.SourceMinionId == c.SourceProxyId ||
                c.DestinationMinionId <= 0 || c.DestinationProxyId <= 0 || c.DestinationMinionId == c.DestinationProxyId)
                throw Invalid("exact_two_root_id_mapping_required");
            if (c.ExistingDestinationIds == null || c.ExistingDestinationIds.Length > 200000 ||
                c.ExistingDestinationIds.Contains(c.DestinationMinionId) || c.ExistingDestinationIds.Contains(c.DestinationProxyId))
                throw Invalid("destination_id_collision_or_unbounded_inventory");
            if (c.ArchiveVersion != 1 && c.ArchiveVersion != 2)
                throw Invalid("unsupported_archive_version");
            if (!ClockInDomain(c.SourceSeconds, c.SourceWholeCycle, c.SourceFractionalCycles) ||
                !ClockInDomain(c.DestinationSeconds, c.DestinationWholeCycle, c.DestinationFractionalCycles))
                throw Invalid("exact_source_and_destination_clocks_required");
            // V1 has no native fractional-cycle sample, so only its cycle-zero slice has a
            // proven conversion. V2 must supply the sampled GetTimeInCycles value: it is
            // range checked independently, never recomputed from global elapsed seconds.
            if (c.ArchiveVersion == 1 && (c.SourceWholeCycle != 0 || c.SourceFractionalCycles != c.SourceSeconds / 600f))
                throw Invalid("source_fractional_clock_not_proven_by_export_v1");
            if (!Finite(c.DestinationX) || !Finite(c.DestinationY) || c.DestinationWorldId < 0 || c.DestinationWorldId >= 255 ||
                c.DestinationWorldWidth <= 0 || c.DestinationWorldHeight <= 0 ||
                c.DestinationX < c.DestinationWorldX || c.DestinationX >= (long)c.DestinationWorldX + c.DestinationWorldWidth ||
                c.DestinationY < c.DestinationWorldY || c.DestinationY >= (long)c.DestinationWorldY + c.DestinationWorldHeight)
                throw Invalid("destination_transform_outside_world_bounds");
        }

        private static bool ClockInDomain(float seconds, int wholeCycle, float fractionalCycles)
        {
            if (!Finite(seconds) || !Finite(fractionalCycles) || wholeCycle < 0 || wholeCycle > MaxClockWholeCycle)
                return false;
            // Use double only for exact interval boundaries, not to invent a native clock
            // sample. Rounded seconds at an adjacent-cycle boundary are conservatively
            // rejected rather than silently moving them into the reported whole cycle.
            return seconds >= (double)wholeCycle * 600 && seconds < ((double)wholeCycle + 1) * 600 &&
                fractionalCycles >= wholeCycle && fractionalCycles < (double)wholeCycle + 1;
        }

        private sealed class TypeSpec
        {
            internal byte Code = 0;
            internal string Name = "";
            internal TypeSpec[] Arguments = Array.Empty<TypeSpec>();
        }
        private sealed class Member
        {
            internal string Name = "";
            internal TypeSpec Type = null;
        }
        private sealed class Template
        {
            internal string Hash = "";
            internal Member[] Members = Array.Empty<Member>();
        }
        private sealed class Parser
        {
            private readonly byte[] schema;
            private readonly byte[][] roots;
            private readonly NativeDupeRemapContext context;
            private readonly Dictionary<string, Template> templates = new Dictionary<string, Template>(StringComparer.Ordinal);
            private readonly HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<NativeDupeMappedField> fields = new List<NativeDupeMappedField>();
            private readonly List<NativeDupeByteEdit> edits = new List<NativeDupeByteEdit>();
            private readonly HashSet<string> machines = new HashSet<string>(StringComparer.Ordinal);
            private bool balloonArtistTrait;
            private int root;
            private int nodes;
            private int components;
            private int objectSlots;

            internal Parser(byte[] schema, byte[][] roots, NativeDupeRemapContext context)
            { this.schema = schema; this.roots = roots; this.context = context; ReadSchema(); }

            internal NativeDupeRemapPlan Read()
            {
                for (root = 0; root < 2; root++)
                {
                    var reader = new Reader(roots[root], 0, roots[root].Length);
                    ReadHeader(reader);
                    string[] expected = root == 0 ? MinionComponents : ProxyComponents;
                    if (reader.Int() != expected.Length) throw Invalid("component_or_manager_tail_count_not_supported");
                    foreach (string type in expected)
                    {
                        string actual = reader.RequiredString();
                        if (actual != type) throw Invalid("component_or_manager_not_supported:" + actual);
                        var body = reader.Sub(reader.Int());
                        Data(body, type, type, 0);
                        if (type == "StateMachineController") StateMachines(body);
                        else if (type == "MinionModifiers") Modifiers(body);
                        else if (type == "Navigator") Navigator(body);
                        else if (type == "Storage" && body.Int() != 0) throw Invalid("nonempty_native_storage_root_graph");
                        body.Done(); components++;
                    }
                    reader.Done();
                }
                // TRAITS.OnAddBalloonArtist installs this extra state machine only for
                // that trait. All shared machines and entity references remain mandatory.
                if (MachineStates.Keys.Any(type => type != "BalloonArtist+Instance" && !machines.Contains(type)) || objectSlots != 30)
                    throw Invalid("incomplete_tiny_slice_machine_or_reference_set");
                if (machines.Contains("BalloonArtist+Instance") != balloonArtistTrait)
                    throw Invalid("balloon_artist_trait_machine_mismatch");
                foreach (var edit in edits)
                    if (edits.Any(other => !ReferenceEquals(other, edit) && other.Root == edit.Root &&
                        edit.Offset < other.Offset + other.Expected.Length && other.Offset < edit.Offset + edit.Expected.Length))
                        throw Invalid("overlapping_remap_edit");
                return new NativeDupeRemapPlan { RootHashes = roots.Select(Hash).ToArray(), RootPrefabNames = new[] { "Minion", "MinionAssignablesProxy" }, SchemaHash = Hash(schema),
                    RootBytesFullyConsumed = roots.Select(r => r.Length).ToArray(), Edits = edits.ToArray(), Fields = fields.ToArray(),
                    Components = components, ObjectIdSlots = objectSlots, StateMachines = machines.Count,
                    UsedTemplates = used.OrderBy(t => t, StringComparer.Ordinal).ToArray(), BinaryLayoutComplete = true };
            }

            internal void CheckDestination(string[] required)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in required)
                    if (name == null || !names.Add(name) || !TemplateHashes.TryGetValue(name, out string approved) ||
                        !templates.TryGetValue(name, out var actual) || actual.Hash != approved)
                        throw Invalid("destination_used_template_not_compatible:" + name);
            }

            private void ReadSchema()
            {
                var reader = new Reader(schema, 0, schema.Length);
                int count = reader.Count(4096), totalMembers = 0;
                for (int i = 0; i < count; i++)
                {
                    string name = reader.RequiredString(); int start = reader.Position;
                    int fieldCount = reader.Count(512), propertyCount = reader.Count(512);
                    totalMembers += fieldCount + propertyCount; if (totalMembers > 50000) throw Invalid("schema_member_budget");
                    var members = new List<Member>(); var names = new HashSet<string>(StringComparer.Ordinal);
                    for (int j = 0; j < fieldCount + propertyCount; j++)
                    {
                        string member = reader.RequiredString();
                        if (!names.Add(member)) throw Invalid("duplicate_schema_member:" + name);
                        members.Add(new Member { Name = member, Type = Type(reader, 0) });
                    }
                    if (templates.ContainsKey(name)) throw Invalid("duplicate_schema_type:" + name);
                    templates.Add(name, new Template { Members = members.ToArray(), Hash = Hash(schema, start, reader.Position - start) });
                }
                reader.Done();
            }

            private static TypeSpec Type(Reader reader, int depth)
            {
                if (depth > 16) throw Invalid("schema_type_depth");
                reader.TypeNode();
                byte code = reader.Byte(); int kind = code & 63;
                if (kind > 23) throw Invalid("schema_kind_not_supported");
                var type = new TypeSpec { Code = code };
                if ((code & 128) != 0)
                {
                    if (kind == 0) type.Name = reader.RequiredString();
                    int count = reader.Byte(); if (count == 0 || count > 16) throw Invalid("schema_generic_arity");
                    type.Arguments = Enumerable.Range(0, count).Select(_ => Type(reader, depth + 1)).ToArray();
                }
                else if (kind == 17) type.Arguments = new[] { Type(reader, depth + 1) };
                else if (kind == 0 || kind == 13) type.Name = reader.RequiredString();
                return type;
            }

            private void Data(Reader reader, string type, string path, int depth)
            {
                if (++nodes > 50000 || depth > 32) throw Invalid("bounded_native_graph_required:" + path);
                if (!templates.TryGetValue(type, out var template) || !TemplateHashes.TryGetValue(type, out string approved) || template.Hash != approved)
                    throw Invalid("unreviewed_schema_template:" + type);
                used.Add(type);
                foreach (var member in template.Members) Value(reader, member.Type, path + "." + member.Name, type, member.Name, depth + 1, false);
            }

            private void Value(Reader reader, TypeSpec type, string path, string owner, string field, int depth, bool direct)
            {
                if (++nodes > 50000 || depth > 32) throw Invalid("bounded_native_graph_required:" + path);
                int kind = type.Code & 63, offset = reader.Position;
                if (kind == 0)
                {
                    if (direct) Data(reader, type.Name, path, depth + 1);
                    else
                    {
                        int length = reader.Int();
                        if (length == -1) { Record(offset, 4, path, "preserve-null-object", "null"); return; }
                        var body = reader.Sub(length); Data(body, type.Name, path, depth + 1); body.Done();
                    }
                    return;
                }
                if (kind == 17 || kind == 19 || kind == 20 || kind == 21 || kind == 22)
                {
                    int length = reader.Int(), count = reader.Int();
                    if (count < -1 || count > 4096 || (count <= 0 && length != 0)) throw Invalid("collection_bounds:" + path);
                    if (owner == "GermExposureMonitor+Instance" && count > 0) throw Invalid("nonempty_exposure_history_requires_cell_time_contract:" + field);
                    if (owner == "Klei.AI.Effects" && field == "saveLoadImmunities" && count > 0) throw Invalid("immunity_giver_contract_not_supported");
                    var body = reader.Sub(length);
                    if (kind == 19)
                    {
                        if (type.Arguments.Length != 2) throw Invalid("dictionary_arity");
                        foreach (int index in new[] { 1, 0 })
                            for (int i = 0; i < count; i++) Value(body, type.Arguments[index], path + "[" + i + "]." + (index == 1 ? "value" : "key"), owner, field, depth + 1, false);
                    }
                    else
                    {
                        if (type.Arguments.Length != 1) throw Invalid("collection_arity");
                        for (int i = 0; i < count; i++) Value(body, type.Arguments[0], path + "[" + i + "]", owner, field, depth + 1, (type.Arguments[0].Code & 64) != 0);
                    }
                    body.Done(); return;
                }
                if (kind == 12)
                {
                    string text = reader.String();
                    if (path == "MinionIdentity.model.name" && text != "Minion") throw Invalid("biological_model_required");
                    if (owner == "Klei.AI.Traits" && field == "TraitIds" && text == "BalloonArtist") balloonArtistTrait = true;
                    string textSemantic = owner == "ResourceGuid" || owner == "Klei.AI.Effects+SaveLoadEffect" && field == "id" ||
                        owner == "Klei.AI.AttributeLevels+LevelSaveLoad" && field == "attributeId" || owner == "Klei.AI.Traits" && field == "TraitIds"
                        ? "preserve-static-db-resource-reference" : "preserve-text-or-resource-key";
                    Record(offset, reader.Position - offset, path, textSemantic, text ?? "null"); return;
                }
                if (kind == 10)
                {
                    float value = reader.Single(); Scalar(reader, offset, value, path, owner, field); return;
                }
                int width;
                switch (kind)
                {
                    case 1: case 2: case 3: width = 1; break;
                    case 4: case 5: width = 2; break;
                    case 6: case 7: case 13: case 23: width = 4; break;
                    case 8: case 9: case 11: case 14: case 15: width = 8; break;
                    case 16: width = 12; break;
                    default: throw Invalid("unsupported_value_kind:" + path);
                }
                if (kind == 6)
                {
                    int value = reader.Int();
                    if (owner == "KPrefabID" && field == "InstanceID")
                    {
                        int expected = root == 0 ? context.SourceMinionId : context.SourceProxyId;
                        if (value != expected) throw Invalid("source_root_definition_mismatch:" + path);
                        Id(offset, value, path, "entity-definition"); return;
                    }
                    if ((owner == ProxyRef && field == "id") || (owner == "MinionAssignablesProxy" && field == "target_instance_id"))
                    {
                        int expected = owner == ProxyRef ? context.SourceProxyId : context.SourceMinionId;
                        if (value != expected) throw Invalid("source_cross_root_ref_mismatch:" + path);
                        Id(offset, value, path, "cross-root-entity-reference"); return;
                    }
                    if (path == "PrimaryElement.diseaseID.hash" && value != 0) throw Invalid("surface_disease_contract_not_supported");
                    if (path == "PrimaryElement.diseaseCount" && value != 0) throw Invalid("surface_disease_contract_not_supported");
                    if (path == "MinionIdentity.model.hash" && value != 2129234166) throw Invalid("biological_model_tag_hash_required");
                    Record(offset, width, path, "preserve-integer-enum-or-resource-hash", value.ToString(System.Globalization.CultureInfo.InvariantCulture)); return;
                }
                byte[] bytes = reader.Bytes(width);
                if (path == "PrimaryElement.ElementID" && (bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24) != 976099455) throw Invalid("native_creature_element_required");
                if (path == "Health.canBeIncapacitated" && bytes[0] != 1) throw Invalid("biological_health_contract_required");
                if (kind == 3 && bytes[0] > 1) throw Invalid("invalid_boolean:" + path);
                if (owner == "Clearable" && field == "isMarkedForClear" && bytes[0] != 0) throw Invalid("pending_clear_of_source_minion");
                Record(offset, width, path, "preserve-native-value", BitConverter.ToString(bytes));
            }

            private void Scalar(Reader reader, int offset, float value, string path, string owner, string field)
            {
                if (!Finite(value) && !(float.IsPositiveInfinity(value) && field == "workTimeRemaining")) throw Invalid("invalid_float:" + path);
                if (path == "PrimaryElement.Units" && value != 1f) throw Invalid("single_positive_native_minion_unit_required");
                if (path == "PrimaryElement._Temperature" && (value <= 0 || value > 10000)) throw Invalid("primary_element_temperature_would_be_sanitized");
                if (owner == "MinionIdentity" && field == "arrivalTime")
                {
                    if (value > context.SourceWholeCycle) throw Invalid("future_source_arrival");
                    FloatEdit(offset, value, value + (context.DestinationWholeCycle - context.SourceWholeCycle), path, "absolute-whole-cycle-origin"); return;
                }
                if (owner == "CoughMonitor+Instance" && field == "lastCoughTime")
                { FloatEdit(offset, value, value + (context.DestinationFractionalCycles - context.SourceFractionalCycles), path, "absolute-fractional-cycles"); return; }
                if (owner == "CoughMonitor+Instance" && field == "lastConsumeTime" && value > 0)
                {
                    float next = value + (context.DestinationFractionalCycles - context.SourceFractionalCycles);
                    if (next <= 0) throw Invalid("rebased_consumption_time_collides_with_null_sentinel");
                    FloatEdit(offset, value, next, path, "absolute-fractional-cycles-positive-sentinel"); return;
                }
                if (owner == "GasLiquidExposureMonitor+Instance" && field == "lastReactTime")
                { FloatEdit(offset, value, value + (context.DestinationSeconds - context.SourceSeconds), path, "absolute-game-seconds"); return; }
                if (owner == "RecreationTimeMonitor+Instance" && field == "moraleAddedTimes")
                { FloatEdit(offset, value, value + (context.DestinationSeconds - context.SourceSeconds), path, "absolute-game-seconds"); return; }
                if (owner == "JoyBehaviourMonitor+Instance" && field == "transitionTime" && value != 0) throw Invalid("nonzero_inactive_joy_deadline_not_supported");
                string semantic = field == "timeRemaining" || field == "workTimeRemaining" ? "preserve-relative-duration" :
                    owner == "CoughMonitor+Instance" && field == "lastConsumeTime" ? "preserve-unset-time-sentinel" :
                    owner == "JoyBehaviourMonitor+Instance" && field == "transitionTime" ? "preserve-inactive-deadline-sentinel" : "preserve-measurement-or-counter";
                Record(offset, 4, path, semantic, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            private void StateMachines(Reader reader)
            {
                if (root != 0 || reader.Int() != 20) throw Invalid("state_machine_serializer_version");
                var body = reader.Sub(reader.Int());
                int machineCount = body.Count(256);
                if (machineCount != MachineStates.Count && machineCount != MachineStates.Count - 1) throw Invalid("state_machine_set_not_supported");
                for (int i = 0; i < machineCount; i++)
                {
                    if (body.Int() != 0) throw Invalid("state_machine_entry_version");
                    string type = body.RequiredString(); string suffix = body.String(); string state = body.RequiredString();
                    if (suffix != null || !machines.Add(type) || !MachineStates.TryGetValue(type, out string approved) || state != approved)
                        throw Invalid("state_machine_state_not_supported:" + type + ":" + state);
                    var entry = body.Sub(body.Int()); Data(entry, type, "SM[" + type + "]", 0);
                    var parameters = new HashSet<string>(StringComparer.Ordinal);
                    if (entry.Position < entry.End)
                    {
                        int count = entry.Count(256);
                        for (int p = 0; p < count; p++)
                        {
                            var parameter = entry.Sub(entry.Int());
                            string contextType = parameter.RequiredString(), name = parameter.RequiredString();
                            string key = type + "|" + name;
                            if (!parameters.Add(key) || !ParameterContexts.TryGetValue(key, out string hash) || Hash(Encoding.UTF8.GetBytes(contextType)) != hash)
                                throw Invalid("state_machine_parameter_not_supported:" + key);
                            string path = "SM[" + type + "]." + name; int offset = parameter.Position;
                            string family = contextType.Substring(0, contextType.IndexOf("[[", StringComparison.Ordinal));
                            if (family == "StateMachine`4+TargetParameter+Context")
                            {
                                int target = parameter.Int();
                                if (name != "masterTarget" || target != context.SourceMinionId) throw Invalid("external_or_missing_state_machine_target:" + key);
                                Id(offset, target, path, "state-machine-master-entity-reference");
                            }
                            else if (family == "StateMachine`4+FloatParameter+Context")
                            {
                                float value = parameter.Single(); if (!Finite(value)) throw Invalid("invalid_parameter_float:" + key);
                                string semantic = name.StartsWith("timeUntil", StringComparison.Ordinal) || name.EndsWith("Remaining", StringComparison.Ordinal) || name.EndsWith("Countdown", StringComparison.Ordinal)
                                    ? "preserve-relative-countdown" : "preserve-parameter-measurement-or-counter";
                                Record(offset, 4, path, semantic, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                            }
                            else if (family == "StateMachine`4+BoolParameter+Context")
                            {
                                byte value = parameter.Byte(); if (value != 0) throw Invalid("active_illness_cough_or_inspiration_not_supported:" + key);
                                Record(offset, 1, path, "preserve-false-condition", "false");
                            }
                            else if (family == "StateMachine`4+IntParameter+Context")
                            {
                                int value = parameter.Int(); if (key != "RocketPassengerMonitor+Instance|targetCell" || value != -1) throw Invalid("external_or_active_native_cell:" + key);
                                Record(offset, 4, path, "preserve-invalid-cell-sentinel", "-1");
                            }
                            else if (family == "StateMachine`4+ResourceParameter`1+Context")
                            {
                                if (key != "DeathMonitor+Instance|death" || parameter.String() != "") throw Invalid("pending_death_resource_not_supported");
                                Record(offset, parameter.Position - offset, path, "preserve-no-death-resource", "empty");
                            }
                            else if (family != "StateMachine`4+Signal+Context") throw Invalid("parameter_family_not_supported:" + key);
                            parameter.Done();
                        }
                    }
                    if (ParameterContexts.Keys.Count(k => k.StartsWith(type + "|", StringComparison.Ordinal)) != parameters.Count)
                        throw Invalid("missing_state_machine_parameter:" + type);
                    entry.Done();
                }
                body.Done();
            }

            private void Modifiers(Reader reader)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                if (reader.Count(128) != AmountNames.Length) throw Invalid("amount_set_not_supported");
                for (int i = 0; i < AmountNames.Length; i++)
                {
                    string name = reader.RequiredString();
                    if (!names.Add(name) || !AmountNames.Contains(name)) throw Invalid("amount_not_supported:" + name);
                    var body = reader.Sub(reader.Int()); Data(body, "Klei.AI.AmountInstance", "Amount[" + name + "]", 0); body.Done();
                }
                if (reader.Int() != 0) throw Invalid("nonempty_sickness_graph_not_supported");
            }

            private void Navigator(Reader reader)
            {
                int offset = reader.Position; byte type = reader.Byte();
                if (type != 0) throw Invalid("floor_navigation_required");
                Record(offset, 1, "Navigator.CurrentNavType", "preserve-floor-nav-type", "0");
                if (reader.Count(11) != 11) throw Invalid("nav_distance_set_not_supported");
                var types = new HashSet<byte>();
                for (int i = 0; i < 11; i++)
                {
                    byte nav = reader.Byte(); int valueOffset = reader.Position, distance = reader.Int();
                    if (nav > 10 || !types.Add(nav) || distance < 0) throw Invalid("invalid_nav_distance");
                    Record(valueOffset, 4, "Navigator.distance[" + nav + "]", "preserve-distance-counter", distance.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            private void ReadHeader(Reader reader)
            {
                for (int i = 0; i < 10; i++)
                {
                    int offset = reader.Position; float value = reader.Single();
                    if (!Finite(value)) throw Invalid("nonfinite_root_transform");
                    if (i >= 3 && value != (i >= 6 ? 1f : 0f)) throw Invalid("nonstandard_root_rotation_or_scale");
                    if (root == 0 && i < 2) FloatEdit(offset, value, i == 0 ? context.DestinationX : context.DestinationY, "Header.position." + i, "destination-world-transform");
                    else Record(offset, 4, "Header." + i, "preserve-root-transform", value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
                if (reader.Byte() != 0) throw Invalid("root_transform_flag_not_supported");
            }

            private void Id(int offset, int id, string path, string semantic)
            {
                int target;
                if (id == context.SourceMinionId) target = context.DestinationMinionId;
                else if (id == context.SourceProxyId) target = context.DestinationProxyId;
                else throw Invalid("external_entity_reference:" + path);
                objectSlots++; Record(offset, 4, path, semantic, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Edit(offset, path, semantic, LittleEndian(BitConverter.GetBytes(target)));
            }
            private void FloatEdit(int offset, float oldValue, float next, string path, string semantic)
            {
                if (!Finite(next)) throw Invalid("rebased_value_overflow:" + path);
                Record(offset, 4, path, semantic, oldValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                Edit(offset, path, semantic, LittleEndian(BitConverter.GetBytes(next)));
            }
            private void Edit(int offset, string path, string semantic, byte[] next)
            {
                byte[] before = new byte[next.Length]; Buffer.BlockCopy(roots[root], offset, before, 0, before.Length);
                if (before.SequenceEqual(next)) return;
                edits.Add(new NativeDupeByteEdit { Root = root, Offset = offset, Path = path, Semantic = semantic, Expected = before, Replacement = next });
            }
            private void Record(int offset, int length, string path, string semantic, string display)
            { fields.Add(new NativeDupeMappedField { Root = root, Offset = offset, Length = length, Path = path, Semantic = semantic, DisplayValue = display }); }
        }

        private sealed class Reader
        {
            private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
            private readonly byte[] bytes;
            private int typeNodes;
            internal void TypeNode() { if (++typeNodes > 50000) throw Invalid("schema_type_budget"); }
            internal int Position { get; private set; }
            internal int End { get; }
            internal Reader(byte[] bytes, int offset, int length)
            {
                if (bytes == null || offset < 0 || length < 0 || offset > bytes.Length - length) throw Invalid("payload_bounds");
                this.bytes = bytes; Position = offset; End = offset + length;
            }
            private void Need(int length) { if (length < 0 || length > End - Position) throw Invalid("truncated_payload"); }
            internal byte Byte() { Need(1); return bytes[Position++]; }
            internal int Int() { Need(4); int p = Position; Position += 4; return bytes[p] | bytes[p + 1] << 8 | bytes[p + 2] << 16 | bytes[p + 3] << 24; }
            internal byte[] Bytes(int length) { Need(length); var value = new byte[length]; Buffer.BlockCopy(bytes, Position, value, 0, length); Position += length; return value; }
            internal float Single() { var value = Bytes(4); if (!BitConverter.IsLittleEndian) Array.Reverse(value); return BitConverter.ToSingle(value, 0); }
            internal int Count(int max) { int n = Int(); if (n < 0 || n > max) throw Invalid("count_bounds"); return n; }
            internal string String()
            {
                int n = Int(); if (n == -1) return null;
                if (n < 0 || n > 16384) throw Invalid("string_bounds");
                try { return Utf8.GetString(Bytes(n)); } catch (DecoderFallbackException) { throw Invalid("invalid_utf8"); }
            }
            internal string RequiredString() { string value = String(); if (string.IsNullOrEmpty(value)) throw Invalid("required_string"); return value; }
            internal Reader Sub(int length) { Need(length); var reader = new Reader(bytes, Position, length); Position += length; return reader; }
            internal void Done() { if (Position != End) throw Invalid("unparsed_payload_bytes"); }
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static byte[] LittleEndian(byte[] value) { if (!BitConverter.IsLittleEndian) Array.Reverse(value); return value; }
        private static string Hash(byte[] bytes) { return Hash(bytes, 0, bytes.Length); }
        private static string Hash(byte[] bytes, int offset, int length)
        { using (var hash = new SHA256Managed()) return BitConverter.ToString(hash.ComputeHash(bytes, offset, length)).Replace("-", "").ToLowerInvariant(); }
        private static InvalidDataException Invalid(string reason) { return new InvalidDataException("native_dupe_remap:" + reason); }
    }
}

namespace OniOnline.Multiplayer
{
    internal static partial class NativeDuplicantRemapPlanner
    {
        // Reviewed native 744825 API layouts; hashes cover raw field/property template definitions.
        // Unused directory types are not approved merely because they occur in the export schema.
        private static readonly Dictionary<string, string> TemplateHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Accessorizer", "1ba473b1911e14bd7b1eb73751476d4af72776863d29818df7b0db7d7096e63f" },
            { "AnimEventHandler", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "BalloonArtist+Instance", "95b91962d3483eb7d17ef72622a3c2b8e06a6ccf93d2db4d722eb5746297f951" },
            { "CalorieMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Chattable", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "ChoreConsumer", "95581c73e06b9168ae5b1feb74d943215719ef76de7774b350182cc308408515" },
            { "ChoreConsumer+PriorityInfo", "f1010ea61877ed968499b51a86dd4cbfabdd20a23865482e732ffa8c2eba79a3" },
            { "ChoreDriver", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Clearable", "3fd7b52d577143288423fd19244c9190e596254061ef3816498e7287fd065753" },
            { "ClothingWearer", "723bec9a58395c7eae68b7cfcc2f507a3f8b439c7cb9bb93aa11a13406ef04a2" },
            { "ClothingWearer+ClothingInfo", "8fee527976afb92a9e6624337ac7631f80afea1ba7fd6e26ec8fb4dfe03ba93c" },
            { "CodexEntryRedirector", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "ColdImmunityMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "ConsumableConsumer", "92fd46b1377bc85e683095f62c6e4f607e6c0d1506be57d6eca88bcdbd099811" },
            { "ConversationMonitor+Instance", "120560fef9b5885a78e391fe1b55733dead7d7becb3bb48d95c4b15785fb2d92" },
            { "CoughMonitor+Instance", "fa18721852dc5ee6a0c71d8f3bd90e96c243e32fa6913fc4bf4c96eaff1749cd" },
            { "CreatureSimTemperatureTransfer", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "DeathMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "DecorMonitor+Instance", "7ff73277262e2de94b695f0647bd1b5755e2b63d77da0ba67aa9897f9ca24816" },
            { "DoctorMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "EmoteMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "EntityLuminescence+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Equipment", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "ExternalTemperatureMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "FaceGraph", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Facing", "f028d0c89a2f24069dcafe0ac65bda8e706321bf33e77431b9aade234e611f32" },
            { "FactionAlignment", "514aa5b0cd381a5b61210de8dd433b772bc1e54361fc4a4564587613e321be2b" },
            { "GasLiquidExposureMonitor+Instance", "036541fd9f804659d29993a15995c8233c09dae2486ef186e71b49f7ffaf7157" },
            { "GermExposureMonitor+Instance", "d7900252cccc5959e19109e7a3711c5ab0728b0a2baa6ef560f005ba9c860f34" },
            { "GridVisibility", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "HashedString", "8506534ebd8412229961dcd91182fce95ee7d8d8d49d612681f1a9d442b53752" },
            { "Health", "d87f702898c3f27d9cdfa4220b45d7815685f7bbcecf6c625cee100ce2fd4453" },
            { "HeatImmunityMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "HygieneMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "IncapacitationMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "InfraredTemperatureAmount", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "InspirationEffectMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "JoyBehaviourMonitor+Instance", "91dc3f342f05bddead9b3d48a5db0c04f3c7e55c25e5025212d204b61449c11d" },
            { "KBatchedAnimHeatPostProcessingEffect", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "KBoxCollider2D", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "KPrefabID", "8d1e44bd1206d7ff1f2a1e955e70d7214ac61fd53724e6b1c6716da0b00d8d70" },
            { "Klei.AI.AmountInstance", "3c9f1557d98231cfb58a910b4ac419c558b6f712393c7384122a0cc21cf8c39c" },
            { "Klei.AI.AttributeConverters", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Klei.AI.AttributeLevels", "244e12e1701ed53dfb9d824ccd57b761073ba513679ee666766cb490a41c313c" },
            { "Klei.AI.AttributeLevels+LevelSaveLoad", "15150f212803e8fadd297299b3b59daf0659060bee5c1e9250e1d6a5f9d586de" },
            { "Klei.AI.Effects", "c8e801b6fa13178e263846e5d4979f3debdbe409ccb5941fe3ee9eabcf2f4273" },
            { "Klei.AI.Effects+SaveLoadEffect", "c95f29e7561117865db7b7214190ba51e30e8021665d601b2d2833ced60c396c" },
            { "Klei.AI.Traits", "b129f9fd8c7d1cc3a2a2233b7fbccd02e40d5d54ccb59899652fb100ae975130" },
            { "Light2D", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "MiningSounds", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "MinionAssignablesProxy", "0cabe0bb9993a0569eb1017bf1a19d407be5cd6d6b9aa01d4b6213390056ee9e" },
            { "MinionBrain", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "MinionIdentity", "ba74279787e3c31761e8425ff1674843703987050652c00cdd321a9e64f3cf40" },
            { "MinionModifiers", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "MinionResume", "b8ebc926400804047fce459b89745cd3c682bfe2fd2e7fb726f0362a550f76ea" },
            { "MoverLayerOccupier", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Navigator", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "NoiseListener", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Ownables", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "OxygenBreather", "4cc4134a63b2e5a3d80b09f20646ac7800000b3e509f5359b4f9cc1d46e4985f" },
            { "Pickupable", "6e65b624dc56f8e7ff531eee6d67c929dfba11f703308b0a4c9a466094e70c7e" },
            { "PressureMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "PrimaryElement", "38fd6453da6c061dbcbe602ef3e527ab88b7cf8453ca3bd67ed3593dd0364834" },
            { "Prioritizable", "44aa6475c5ee9ffdd27f094f548673d9cc7a844b89ce6aa138e6a9019823f849" },
            { "PrioritySetting", "53f2be78245b8c8a27eccebbc2d3a4c43f8b6c9655407a33b6cda94adcc008c3" },
            { "RadiationMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "RangedAttackable", "41e54067fb8dddb9e505dce4ff2a6accbccf12ca3621a640e3d0f2e47cd4cb35" },
            { "RationMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "RecreationTimeMonitor+Instance", "b26ea9552e1ce18500e3ac4219e3fa1124433f58cc620bb22cd48888bbd78c33" },
            { "RedAlertMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Ref`1[[MinionAssignablesProxy, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]]", "ee3b401cccdfc8dc22e8a74d1c5819f4be4e5f49a02eba2ce22ced13007c59fa" },
            { "ResourceGuid", "a6bcf56e35716adc884f35250155e0be38aa7e66a4d3638db3eb97d0050af01d" },
            { "ResourceRef`1[[Accessory, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]]", "48b3edfc17f23798f802d75b7253c2a7bbff22b90a9094810fa1182d9bd20760" },
            { "ResourceRef`1[[Database.ClothingItemResource, Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]]", "48b3edfc17f23798f802d75b7253c2a7bbff22b90a9094810fa1182d9bd20760" },
            { "RocketPassengerMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "SafeCellMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "ScaldingMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Schedulable", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Sensors", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "SicknessMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "SicknessTrigger", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "SlipperyMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "SnapOn", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "StaminaMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "StandardWorker", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "StateMachineController", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Storage", "ddb92221b8074b08b7507d01f79e609ef02a3054e0a98bf9f2485ef68b90b4ea" },
            { "StressMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "SuffocationMonitor+Instance", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "SuitEquipper", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "SymbolOverrideController", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Tag", "be35bc02b2aca68b63819b3b938da8a736bf4ad4fa722a7103a8747293e0fc83" },
            { "User", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "WarmBlooded", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "Weapon", "af5570f5a1810b7af78caf4bc70a660f0df51e42baf91d4de5b2328de0e83dfc" },
            { "WearableAccessorizer", "a7079dd8be578ea7ed463f3705a990f0d18aa291ed55f346b55fa82a35be6216" },
            { "WearableAccessorizer+Wearable", "434752b3f358a40ac606e7f88d1d3da06f692aa3abdd2114dbf43548c37befce" },
        };
        private static readonly Dictionary<string, string> MachineStates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "EntityLuminescence+Instance", "root" },
            { "BalloonArtist+Instance", "root.neutral" },
            { "JoyBehaviourMonitor+Instance", "root.neutral" },
            { "DeathMonitor+Instance", "root.alive" },
            { "RadiationMonitor+Instance", "root.active.idle" },
            { "StressMonitor+Instance", "root.satisfied" },
            { "EmoteMonitor+Instance", "root.satisfied" },
            { "DecorMonitor+Instance", "root" },
            { "IncapacitationMonitor+Instance", "root.healthy" },
            { "DoctorMonitor+Instance", "root" },
            { "SicknessMonitor+Instance", "root.healthy" },
            { "GermExposureMonitor+Instance", "root" },
            { "ExternalTemperatureMonitor+Instance", "root.alive.comfortable" },
            { "ScaldingMonitor+Instance", "root.idle" },
            { "ColdImmunityMonitor+Instance", "root.idle.feelingFine" },
            { "HeatImmunityMonitor+Instance", "root.idle.feelingFine" },
            { "RedAlertMonitor+Instance", "root.off" },
            { "SafeCellMonitor+Instance", "root.safe" },
            { "SuffocationMonitor+Instance", "root.satisfied.normal" },
            { "RocketPassengerMonitor+Instance", "root.satisfied" },
            { "ConversationMonitor+Instance", "root" },
            { "CoughMonitor+Instance", "root.idle" },
            { "GasLiquidExposureMonitor+Instance", "root.normal" },
            { "InspirationEffectMonitor+Instance", "root.idle" },
            { "SlipperyMonitor+Instance", "root.safe" },
            { "PressureMonitor+Instance", "root.safe" },
            { "RecreationTimeMonitor+Instance", "root.idle" },
            { "StaminaMonitor+Instance", "root.satisfied" },
            { "RationMonitor+Instance", "root.rationsavailable.edibleavailable.readytoeat" },
            { "CalorieMonitor+Instance", "root.satisfied" },
            { "HygieneMonitor+Instance", "root.needsshower" },
        };
        private static readonly Dictionary<string, string> ParameterContexts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "EntityLuminescence+Instance|masterTarget", "d06208943bf95b8a84bbcf072fe13053b20e6a34d2e059e0b5003204204430c6" },
            { "JoyBehaviourMonitor+Instance|exitEarly", "94e00f5191ccc9d2642d6732e2ac8ef0bebb0ef6f18809f86da6ac039f4da782" },
            { "JoyBehaviourMonitor+Instance|masterTarget", "08df7b2bf5db7d0bc180a08986f9964bf1238cb5eb196ac3b900cc7011721443" },
            { "DeathMonitor+Instance|death", "00f10bdc7b712af9d55641547effa0d4e5a0895b0fab2b7e5b47a8d60831e78a" },
            { "DeathMonitor+Instance|masterTarget", "91d5e925a78a44752b7576e317b16d7f31de5a9682a042e041d486d307f75edd" },
            { "RadiationMonitor+Instance|radiationExposure", "4422996facc0cba888aa7413dfdc7bae7f50de11213f840c7401e26d6a83d954" },
            { "RadiationMonitor+Instance|currentExposurePerCycle", "4422996facc0cba888aa7413dfdc7bae7f50de11213f840c7401e26d6a83d954" },
            { "RadiationMonitor+Instance|isSick", "b393e1c6949171b7bef95a6504ea320aa9a91d2f40cd173dc8b6c6289bf5b85d" },
            { "RadiationMonitor+Instance|timeUntilNextExposureReact", "4422996facc0cba888aa7413dfdc7bae7f50de11213f840c7401e26d6a83d954" },
            { "RadiationMonitor+Instance|timeUntilNextSickReact", "4422996facc0cba888aa7413dfdc7bae7f50de11213f840c7401e26d6a83d954" },
            { "RadiationMonitor+Instance|masterTarget", "ff62d8ccf311ad3f1e5f692e3fc24a3bf4be75af0523d2efec3d42a03cbee9c0" },
            { "StressMonitor+Instance|masterTarget", "e64981e24b582e9a496ed08a0003a68e1c2d66ec3f7e5a039343aff5ca4ab211" },
            { "EmoteMonitor+Instance|masterTarget", "e7035caa9601b33bd91d20b0ff2c047833e2f46e7bfd8d12757c38c114a14f83" },
            { "IncapacitationMonitor+Instance|bleedOutStamina", "8c0dc32f1d2002cc17d375dcf7b45c4af4ba9cb8113986687c14b745aee30b91" },
            { "IncapacitationMonitor+Instance|baseBleedOutSpeed", "8c0dc32f1d2002cc17d375dcf7b45c4af4ba9cb8113986687c14b745aee30b91" },
            { "IncapacitationMonitor+Instance|baseStaminaRecoverSpeed", "8c0dc32f1d2002cc17d375dcf7b45c4af4ba9cb8113986687c14b745aee30b91" },
            { "IncapacitationMonitor+Instance|maxBleedOutStamina", "8c0dc32f1d2002cc17d375dcf7b45c4af4ba9cb8113986687c14b745aee30b91" },
            { "IncapacitationMonitor+Instance|masterTarget", "ea24fceb3e9816a0f35461153cfeb42a0c9dc368ddb6dc3b023736c73fdebd60" },
            { "DoctorMonitor+Instance|masterTarget", "a85a4ee92bd7e83f7d966f2fa2ba12da55d068f8e7b15f5924567dda832c6c4b" },
            { "SicknessMonitor+Instance|masterTarget", "61fd136db165da65630c7b8ddcbd8c0dfd57afdf5c22a881cd368c2fdcc5ec6d" },
            { "ExternalTemperatureMonitor+Instance|_ColdResistanceDurationRemaining", "298be1c3d150667069c12f68ab1ce5cfa05e1bc49838342cc3800d2382c5e837" },
            { "ExternalTemperatureMonitor+Instance|masterTarget", "dcb1162a58f47ae49d688e868b828cf6296ce2b16e3b30dfe2537a7ad31ad607" },
            { "ScaldingMonitor+Instance|masterTarget", "58cb4a801821be1600cd9b778725b2ca16b17bc867e6f04d08d74ebd722b08db" },
            { "ColdImmunityMonitor+Instance|coldCountdown", "84ac4473560b15ae9b57323e0d8efe93aa9e81bb74c61273ad67dc8579c40fb0" },
            { "ColdImmunityMonitor+Instance|masterTarget", "ae30fc8a90c6a4e07149549c7817b744fcfa559bc9e9d5879e8b095663e00794" },
            { "HeatImmunityMonitor+Instance|heatCountdown", "2c086a984d16b0fa5b1c1421d0f0a8ab5d70eb4231a1b8ca07da130a044f2f20" },
            { "HeatImmunityMonitor+Instance|masterTarget", "a557be7556063b53e60a6a89014fa76d2590bcc5fb0df60a6c7575051f2a2cc4" },
            { "RedAlertMonitor+Instance|masterTarget", "a2f334caea2422f13f0fd388f08f608ee87a605a005e4383ab2a59c0e9e505b7" },
            { "SafeCellMonitor+Instance|masterTarget", "599e9220f5929069fca778e82118596bc40fd4f9d938c5d252c0c615be71fed1" },
            { "SuffocationMonitor+Instance|timeUntilDeath", "0bd2156acf8710f014c2523bcfdc472d2a1b3c414e72823e1900e38f46b20dd4" },
            { "SuffocationMonitor+Instance|masterTarget", "8944638ba555282689e8e4192d39b4f5e87cf146e1e75af760bf365d0708f42d" },
            { "RocketPassengerMonitor+Instance|targetCell", "65581252ecb9460eceba82e31de94daeecb01b6712dba64fe6a2d016ba1d4f80" },
            { "RocketPassengerMonitor+Instance|masterTarget", "aa1afc8228801d6bb41567fcebf034c71f3b5a4db96311f5e211822d1dbad588" },
            { "CoughMonitor+Instance|shouldCough", "f6b1016e638f0f6221f3afcaa6709f004cefa63234417aab50bf315c57935c9c" },
            { "CoughMonitor+Instance|masterTarget", "37a3c1e89a86f33d1bcc4e5c79e48e257e95acf2ea04adcc4cd29154d30ae78a" },
            { "InspirationEffectMonitor+Instance|shouldCatchyTune", "c4349cd690ff6933a7f341501b78dccb205b2f3a7c458b4bab0bfa2f27a47248" },
            { "InspirationEffectMonitor+Instance|inspirationTimeRemaining", "5f15f83bf8309b3d505d9aaa03dad3c2504325d48bc6c6c29d4300a3467aff0a" },
            { "InspirationEffectMonitor+Instance|masterTarget", "ed93bf322b38d132443dd7dbc05d23b1c5e0e30d019927571d144077f5492da0" },
            { "SlipperyMonitor+Instance|masterTarget", "401b2e7ac2bbd27682e59d7f1db1aa9b00f58582174502f279411dbb0e333055" },
            { "PressureMonitor+Instance|masterTarget", "f64d832ebd23714a65fddbd8f930e28bacf3a543067137db91714020497c26f6" },
            { "RecreationTimeMonitor+Instance|masterTarget", "7f49916bba51a3c0b81e48a82b6610b0b0bb2563185bad10bd1a60789ba4f8c9" },
            { "StaminaMonitor+Instance|masterTarget", "8b5871e2b747874bff72ef6cdc85421b90be46d657246d3c9773263521004e14" },
            { "RationMonitor+Instance|rationsAteToday", "b39d10a56029fb4d4322139761c56fdc48888eac23feda02bde8d0f286405884" },
            { "RationMonitor+Instance|masterTarget", "dfeaf6cb4843b074e0ea2fe7bf3b1d27e1e2cb00dc0b5988062cb5831a195231" },
            { "CalorieMonitor+Instance|masterTarget", "cd65a8e39f69f587a3631379a0cb0d7ad442c9f2b830159d14f321aca114371b" },
            { "HygieneMonitor+Instance|dirtiness", "58b24acb003e1b862d6a374067d556e074e20d0d9501f40552e63e98e01f5cba" },
            { "HygieneMonitor+Instance|masterTarget", "141b5057de8664a56ac0963d03d78c90faaa47114f276d8992c54d5739fc6a51" },
        };
        private static readonly string[] MinionComponents = new[] {
            "KPrefabID", "StateMachineController", "MinionModifiers", "Klei.AI.Traits", "Klei.AI.Effects", "Klei.AI.AttributeLevels", "Klei.AI.AttributeConverters", "MinionBrain", "StandardWorker", "ChoreConsumer", "Storage", "Health", "MinionIdentity", "OxygenBreather", "WarmBlooded", "GridVisibility", "MiningSounds", "MoverLayerOccupier", "Navigator", "KBoxCollider2D", "SnapOn", "PrimaryElement", "InfraredTemperatureAmount", "Sensors", "Chattable", "FaceGraph", "Accessorizer", "WearableAccessorizer", "Schedulable", "AnimEventHandler", "FactionAlignment", "Weapon", "RangedAttackable", "Pickupable", "CreatureSimTemperatureTransfer", "SicknessTrigger", "ClothingWearer", "SuitEquipper", "ConsumableConsumer", "NoiseListener", "MinionResume", "SymbolOverrideController", "CodexEntryRedirector", "Facing", "ChoreDriver", "User", "Light2D", "Clearable", "Prioritizable", "KBatchedAnimHeatPostProcessingEffect"
        };
        private static readonly string[] ProxyComponents = new[] {
            "KPrefabID", "Ownables", "Equipment", "MinionAssignablesProxy"
        };
        private static readonly string[] AmountNames = new[] { "HitPoints", "ImmuneLevel", "Breath", "Stress", "Toxicity", "Temperature", "Decor", "RadiationBalance", "Bladder", "Stamina", "Calories", "FoodPoisoning", "SlimeLung", "PollenGerms", "ZombieSpores", "RadiationSickness" };
    }
}
