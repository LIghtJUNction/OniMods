using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Multiplayer;

internal static class NativeDuplicantImportAuditTests
{
    private sealed class Fixture
    {
        internal byte[] Root;
        internal byte[] Schema;
        internal NativeDupeAuditSlice[] Slices;
    }

    internal static void Run(Action<bool, string> check)
    {
        var fixture = Make();
        byte[] original = (byte[])fixture.Root.Clone();
        var audit = Read(fixture);
        check(audit.StateMachines == 1 && audit.StorageItems == 0 && audit.Sicknesses == 0, "four native detail contracts decoded");
        check(audit.Amounts["HitPoints"] == 100 && audit.Amounts["Breath"] == 80 && audit.NavType == 0, "current needs retained as values");
        check(audit.Parameters.Count == 2 && audit.Parameters[0].TargetInstanceId == 102 && !audit.Parameters[1].TargetInstanceId.HasValue,
            "same integer in cell parameter is not an object ref");
        var plan = audit.PlanStateMachineTargetRemap(new Dictionary<int, int> { [102] = 301, [108] = 302 });
        check(plan.Length == 1 && plan[0].OldId == 102 && plan[0].NewId == 301 && BitConverter.ToInt32(fixture.Root, plan[0].Offset) == 102,
            "plan selects exact oldID byte location without patching");
        check(original.SequenceEqual(fixture.Root) && !audit.ImportReady && audit.RemainingContracts.Count > 0, "audit is read only and cannot authorize import");
        Reject(check, () => audit.PlanStateMachineTargetRemap(new Dictionary<int, int> { [102] = 301 }), "incomplete root remap rejected");
        Reject(check, () => audit.PlanStateMachineTargetRemap(new Dictionary<int, int> { [102] = 301, [108] = 301 }), "destination root collision rejected");
        var external = Read(Make(target: 999));
        check(external.RemainingContracts.Contains("external_sm_target:999"), "hidden external SM target disclosed");
        Reject(check, () => external.PlanStateMachineTargetRemap(new Dictionary<int, int> { [102] = 301, [108] = 302 }), "external SM target blocks plan");
        var zero = Read(Make(target: 0));
        check(zero.PlanStateMachineTargetRemap(new Dictionary<int, int> { [102] = 301, [108] = 302 }).Length == 0, "null target remains sentinel");
        Reject(check, () => NativeDuplicantImportAudit.Read(744824, fixture.Schema, fixture.Root, fixture.Slices, 102, 108), "unknown native build rejected");
        Reject(check, () => Read(Make(storageItems: 1)), "stored object cannot silently escape closure");
        Reject(check, () => Read(Make(sicknesses: 1)), "infected export requires deeper disease contract");
        Reject(check, () => Read(Make(smVersion: 11)), "legacy SM layout rejected");
        Reject(check, () => Read(Make(duplicateParameter: true)), "duplicate SM parameter rejected");
        Reject(check, () => Read(Make(amountValue: float.NaN)), "invalid amount rejected");
        Reject(check, () => Read(Make(amountSchema: 6)), "amount schema drift rejected");
        var duplicate = Make(); duplicate.Slices[3].Type = duplicate.Slices[0].Type;
        Reject(check, () => Read(duplicate), "duplicate block rejected");
        var overlapping = Make(); overlapping.Slices[1].Offset = overlapping.Slices[0].Offset;
        Reject(check, () => Read(overlapping), "overlapping block rejected");
        var truncated = Make(); truncated.Slices[0].Length--;
        Reject(check, () => Read(truncated), "SM entry cannot read into adjacent component");
        for (int i = 0; i < fixture.Schema.Length; i++)
        {
            byte[] schema = fixture.Schema.Take(i).ToArray();
            Reject(check, () => NativeDuplicantImportAudit.Read(744825, schema, fixture.Root, fixture.Slices, 102, 108), "truncated schema rejected at " + i);
        }
    }

    private static NativeDupeOpaqueAudit Read(Fixture f) => NativeDuplicantImportAudit.Read(744825, f.Schema, f.Root, f.Slices, 102, 108);
    private static void Reject(Action<bool, string> check, Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        check(rejected, name);
    }

    private static Fixture Make(int target = 102, int storageItems = 0, int sicknesses = 0, int smVersion = 20,
        bool duplicateParameter = false, float amountValue = 100, byte amountSchema = 10)
    {
        byte[] schema = Bytes(w =>
        {
            w.Write(6);
            Template(w, "StateMachineController"); Template(w, "Navigator"); Template(w, "MinionModifiers"); Template(w, "AuditSM+Instance");
            Template(w, "Klei.AI.AmountInstance", new[] { Tuple.Create("value", amountSchema) });
            Template(w, "Storage", new[] { Tuple.Create("onlyFetchMarkedItems", (byte)3), Tuple.Create("shouldSaveItems", (byte)3),
                Tuple.Create("workTimeRemaining", (byte)10), Tuple.Create("numberOfUses", (byte)6) });
        });
        byte[] stateMachines = Bytes(w =>
        {
            w.Write(smVersion);
            Sized(w, b =>
            {
                b.Write(1); b.Write(0); String(b, "AuditSM+Instance"); String(b, null); String(b, "root.healthy");
                Sized(b, e =>
                {
                    e.Write(2);
                    Sized(e, p => { String(p, "StateMachine`4+TargetParameter+Context[[Audit]]"); String(p, "masterTarget"); p.Write(target); });
                    Sized(e, p => { String(p, "StateMachine`4+IntParameter+Context[[Audit]]"); String(p, duplicateParameter ? "masterTarget" : "targetCell"); p.Write(102); });
                });
            });
        });
        byte[] modifiers = Bytes(w =>
        {
            w.Write(2); String(w, "HitPoints"); Sized(w, b => b.Write(amountValue));
            String(w, "Breath"); Sized(w, b => b.Write(80f)); w.Write(sicknesses);
        });
        byte[] storage = Bytes(w => { w.Write(false); w.Write(true); w.Write(1.5f); w.Write(0); w.Write(storageItems); });
        byte[] navigator = Bytes(w => { w.Write((byte)0); w.Write(2); w.Write((byte)0); w.Write(4); w.Write((byte)1); w.Write(0); });
        var data = new[] { stateMachines, modifiers, storage, navigator };
        var names = new[] { "StateMachineController", "MinionModifiers", "Storage", "Navigator" };
        var slices = new NativeDupeAuditSlice[4];
        int offset = 7; // Deliberately not aligned, as real SaveLoadRoot strings create arbitrary offsets.
        for (int i = 0; i < data.Length; i++)
        {
            slices[i] = new NativeDupeAuditSlice { Type = names[i], Offset = offset, Length = data[i].Length }; offset += data[i].Length;
        }
        byte[] root = new byte[offset];
        for (int i = 0; i < data.Length; i++) Buffer.BlockCopy(data[i], 0, root, slices[i].Offset, data[i].Length);
        return new Fixture { Root = root, Schema = schema, Slices = slices };
    }

    private static void Template(BinaryWriter w, string type, Tuple<string, byte>[] fields = null)
    {
        fields = fields ?? Array.Empty<Tuple<string, byte>>(); String(w, type); w.Write(fields.Length); w.Write(0);
        foreach (var field in fields) { String(w, field.Item1); w.Write(field.Item2); }
    }
    private static byte[] Bytes(Action<BinaryWriter> action)
    {
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream)) { action(writer); writer.Flush(); return stream.ToArray(); }
    }
    private static void Sized(BinaryWriter writer, Action<BinaryWriter> body) { byte[] bytes = Bytes(body); writer.Write(bytes.Length); writer.Write(bytes); }
    private static void String(BinaryWriter writer, string value)
    {
        if (value == null) { writer.Write(-1); return; }
        byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
    }
}
