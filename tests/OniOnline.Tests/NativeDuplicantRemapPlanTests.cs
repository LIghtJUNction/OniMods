using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Multiplayer;

internal static class NativeDuplicantRemapPlanTests
{
    internal static NativeDupeRemapContext Context() => new NativeDupeRemapContext {
        GameAssemblyHash = NativeDuplicantRemapPlanner.GameHash, FrameworkAssemblyHash = NativeDuplicantRemapPlanner.FrameworkHash,
        SourceMinionId = 102, SourceProxyId = 108, DestinationMinionId = 301, DestinationProxyId = 302,
        SourceSeconds = 59.3831901550293f, SourceWholeCycle = 0, SourceFractionalCycles = 59.3831901550293f / 600f,
        DestinationSeconds = 6030, DestinationWholeCycle = 10, DestinationFractionalCycles = 10.05f,
        DestinationX = 100.5f, DestinationY = 100.01f, DestinationWorldId = 0, DestinationWorldWidth = 256, DestinationWorldHeight = 384 };

    internal static void Run(Action<bool, string> check)
    {
        Reject(check, () => NativeDuplicantRemapPlanner.Plan(Array.Empty<byte>(), Array.Empty<byte[]>(), null), "missing native contract rejected");
        var context = Context(); context.GameAssemblyHash = "changed";
        Reject(check, () => NativeDuplicantRemapPlanner.Plan(Array.Empty<byte>(), new[] { Array.Empty<byte>(), Array.Empty<byte>() }, context), "other native method fingerprint rejected");
        context = Context(); context.ExistingDestinationIds = new[] { 301 };
        Reject(check, () => NativeDuplicantRemapPlanner.Plan(Array.Empty<byte>(), new[] { Array.Empty<byte>(), Array.Empty<byte>() }, context), "destination collision rejected before parsing");
        context = Context(); context.DestinationProxyId = 301;
        Reject(check, () => NativeDuplicantRemapPlanner.Plan(Array.Empty<byte>(), new[] { Array.Empty<byte>(), Array.Empty<byte>() }, context), "distinct destination IDs required");
        context = Context(); context.SourceWholeCycle = 1; context.SourceSeconds = 660; context.SourceFractionalCycles = 1.1f;
        Reject(check, () => NativeDuplicantRemapPlanner.Plan(Array.Empty<byte>(), new[] { Array.Empty<byte>(), Array.Empty<byte>() }, context), "v1 nonzero-cycle fractional source clock not guessed");
        context = Context(); context.DestinationWorldId = 255;
        Reject(check, () => NativeDuplicantRemapPlanner.Plan(Array.Empty<byte>(), new[] { Array.Empty<byte>(), Array.Empty<byte>() }, context), "reserved destination world ID rejected");
        context = Context(); context.DestinationX = 256.5f;
        Reject(check, () => NativeDuplicantRemapPlanner.Plan(Array.Empty<byte>(), new[] { Array.Empty<byte>(), Array.Empty<byte>() }, context), "destination transform must fit exact world bounds");
        context = Context(); context.DestinationSeconds = float.PositiveInfinity;
        Reject(check, () => NativeDuplicantRemapPlanner.Plan(Array.Empty<byte>(), new[] { Array.Empty<byte>(), Array.Empty<byte>() }, context), "nonfinite clock rejected");
        check(Context().ArchiveVersion == 1, "legacy callers keep v1 clock contract by default");
        var schema = SyntheticSchema();
        foreach (int version in new[] { 0, 3, int.MaxValue })
        {
            context = Context(); context.ArchiveVersion = version;
            RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "unsupported_archive_version");
        }
        context = Context(); context.SourceSeconds = 600;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");
        context = Context(); context.ArchiveVersion = 2; context.SourceWholeCycle = 1; context.SourceSeconds = 599; context.SourceFractionalCycles = 1.1f;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");
        context.SourceSeconds = 1200;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");
        context.SourceSeconds = 660; context.SourceFractionalCycles = 2;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");
        context.SourceFractionalCycles = float.NaN;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");
        context.SourceFractionalCycles = 1.1f; context.SourceSeconds = float.PositiveInfinity;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");
        context = Context(); context.DestinationFractionalCycles = float.NegativeInfinity;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");
        context = Context(); context.SourceFractionalCycles += .00001f;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "source_fractional_clock_not_proven_by_export_v1");
        // The synthetic partial root deliberately fails later, proving that v2 context
        // validation accepted the native sample without replacing it by seconds / 600.
        context = Context(); context.ArchiveVersion = 2; context.SourceWholeCycle = 1; context.SourceSeconds = 660; context.SourceFractionalCycles = 1.1000001f;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "component_or_manager_not_supported:UnreviewedManager");
        context = Context(); context.ArchiveVersion = 2; context.SourceWholeCycle = 131071; context.SourceSeconds = 78642632; context.SourceFractionalCycles = 131071.125f;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "component_or_manager_not_supported:UnreviewedManager");
        context.SourceWholeCycle = 131072; context.SourceSeconds = 78643232; context.SourceFractionalCycles = 131072.125f;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");
        context = Context(); context.DestinationWholeCycle = 131072; context.DestinationSeconds = 78643232; context.DestinationFractionalCycles = 131072.125f;
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), context), "exact_source_and_destination_clocks_required");

        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(), Context()), "component_or_manager_not_supported:UnreviewedManager");
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, SyntheticPartialRoot(999), Context()), "source_root_definition_mismatch");
        var changed = (byte[])schema.Clone();
        int field = 4 + 4 + Encoding.UTF8.GetByteCount("KPrefabID") + 8 + 4;
        changed[field] = (byte)'X';
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(changed, SyntheticPartialRoot(), Context()), "unreviewed_schema_template:KPrefabID");
        var truncated = SyntheticPartialRoot(); truncated[0] = truncated[0].Take(63).ToArray();
        RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(schema, truncated, Context()), "truncated_payload");

        // Native duplicants without BalloonArtist have thirty shared machines. Both
        // reviewed counts must proceed to type checks, while unknown types stay refused.
        var machineSchema = SyntheticSchema(true);
        foreach (int count in new[] { 30, 31 })
            RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(machineSchema, SyntheticMachineRoot(count), Context()),
                "state_machine_state_not_supported:UnreviewedSM+Instance");
        foreach (int count in new[] { 0, 29, 32, 256 })
            RejectAt(check, () => NativeDuplicantRemapPlanner.Plan(machineSchema, SyntheticMachineRoot(count), Context()),
                "state_machine_set_not_supported");
    }

    private static void Reject(Action<bool, string> check, Action action, string reason)
    { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } check(rejected, reason); }

    private static byte[] SyntheticSchema(bool includeMachines = false)
    {
        return Bytes(w => {
            w.Write(includeMachines ? 2 : 1); Text(w, "KPrefabID"); w.Write(2); w.Write(0);
            Text(w, "InstanceID"); w.Write((byte)6);
            Text(w, "serializedTags"); w.Write((byte)149); w.Write((byte)1); w.Write((byte)64); Text(w, "Tag");
            if (includeMachines) { Text(w, "StateMachineController"); w.Write(0); w.Write(0); }
        });
    }
    private static byte[][] SyntheticMachineRoot(int machineCount)
    {
        var entries = Bytes(w => {
            w.Write(machineCount); w.Write(0); Text(w, "UnreviewedSM+Instance");
            w.Write(-1); Text(w, "root"); w.Write(0);
        });
        var body = Bytes(w => { w.Write(20); w.Write(entries.Length); w.Write(entries); });
        var root = Bytes(w => {
            foreach (float value in new[] { 80.5f, 120.01f, -25.5f, 0, 0, 0, 1, 1, 1, 1 }) w.Write(value);
            w.Write((byte)0); w.Write(50); Text(w, "KPrefabID"); w.Write(12); w.Write(102); w.Write(0); w.Write(0);
            Text(w, "StateMachineController"); w.Write(body.Length); w.Write(body);
        });
        return new[] { root, Array.Empty<byte>() };
    }
    private static byte[][] SyntheticPartialRoot(int id = 102, string following = "UnreviewedManager")
    {
        var root = Bytes(w => {
            foreach (float v in new[] { 80.5f, 120.01f, -25.5f, 0, 0, 0, 1, 1, 1, 1 }) w.Write(v);
            w.Write((byte)0); w.Write(50); Text(w, "KPrefabID"); w.Write(12); w.Write(id); w.Write(0); w.Write(0);
            Text(w, following); w.Write(0);
        });
        return new[] { root, Array.Empty<byte>() };
    }
    private static void RejectAt(Action<bool, string> check, Action action, string error)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException e) { rejected = e.Message.Contains(error); }
        check(rejected, "synthetic structural parser boundary: " + error);
    }
    private static byte[] Bytes(Action<BinaryWriter> write)
    { using (var s = new MemoryStream()) using (var w = new BinaryWriter(s)) { write(w); w.Flush(); return s.ToArray(); } }
    private static void Text(BinaryWriter w, string text) { byte[] bytes = Encoding.UTF8.GetBytes(text); w.Write(bytes.Length); w.Write(bytes); }
}
