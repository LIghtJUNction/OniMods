using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Game boundaries only. The command, assignment, slot resolution, storage lookup,
// and filter handlers are linked verbatim from production. Native contracts here
// follow Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636;
// these host fixtures cannot substitute for an ONI save/runtime acceptance run.
namespace UnityEngine
{
    internal sealed partial class GameObject
    {
        internal string name { get; set; } = "Fixture";
        internal int WorldId { get; set; }
        internal int Cell { get; set; }
        internal TestTransform transform => new TestTransform(Cell);
        internal int GetInstanceID() => GetComponent<KPrefabID>()?.InstanceID ?? 0;
        internal string GetProperName() => name;
        internal int GetMyWorldId() => WorldId;
        internal T GetSMI<T>() where T : class => GetComponent<T>();
        internal Klei.AI.Sicknesses GetSicknesses() => GetComponent<Klei.AI.Sicknesses>();
    }

    internal sealed class TestTransform
    {
        private readonly int cell;
        internal TestTransform(int cell) { this.cell = cell; }
        internal TestPosition GetPosition() => new TestPosition(cell % 1000, cell / 1000);
    }

    internal struct TestPosition
    {
        internal TestPosition(float x, float y) { this.x = x; this.y = y; }
        internal float x { get; }
        internal float y { get; }
    }

    internal static class Mathf
    {
        internal static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(value, max));
        internal static int RoundToInt(float value) => (int)Math.Round(value);
    }
}

internal class KMonoBehaviour
{
    internal GameObject gameObject { get; set; }
    internal T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    internal T GetSMI<T>() where T : class => gameObject.GetSMI<T>();
    public string GetProperName() => gameObject.GetProperName();
    internal int GetMyWorldId() => gameObject.GetMyWorldId();
}

internal sealed partial class KPrefabID
{
    internal int InstanceID { get; set; }
    internal Tag PrefabTag { get; set; }
}

internal partial struct Tag : IEquatable<Tag>
{
    internal string ProperName() => Name;
    public bool Equals(Tag other) => string.Equals(Name, other.Name, StringComparison.Ordinal);
    public override bool Equals(object other) => other is Tag && Equals((Tag)other);
    public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);
    public static bool operator ==(Tag left, Tag right) => left.Equals(right);
    public static bool operator !=(Tag left, Tag right) => !left.Equals(right);
}

internal static partial class GameTags
{
    internal static Tag Void => new Tag("Void");
}

internal static partial class Grid
{
    internal sealed class TestWorldIndex
    {
        private readonly Dictionary<int, int> worlds = new Dictionary<int, int>();
        internal int this[int cell]
        {
            get { int world; return worlds.TryGetValue(cell, out world) ? world : 0; }
            set { worlds[cell] = value; }
        }
        internal void Clear() => worlds.Clear();
    }

    internal static readonly TestWorldIndex WorldIdx = new TestWorldIndex();
    internal static int XYToCell(int x, int y) => x < 0 || x >= 1000 || y < 0 ? -1 : x + y * 1000;
    internal static int PosToCell(GameObject go) => go.Cell;
    internal static int PosToCell(TestPosition pos) => XYToCell((int)pos.x, (int)pos.y);
    internal static int CellColumn(int cell) => cell % 1000;
    internal static int CellRow(int cell) => cell / 1000;
    internal static bool IsVisible(int cell) => IsValidCell(cell);
    internal static bool IsWorldValidCell(int cell) => IsValidCell(cell);
}

internal sealed class TestRef<T> where T : class
{
    internal T Value { get; set; }
    internal T Get() => Value;
}

internal interface IAssignableIdentity
{
    Ownables GetSoleOwner();
    IEnumerable<Ownables> GetOwners();
    string GetProperName();
    bool IsNull();
    int NumOwners();
}

internal sealed class MinionIdentity : KMonoBehaviour, IAssignableIdentity
{
    internal TestRef<MinionAssignablesProxy> assignableProxy { get; } = new TestRef<MinionAssignablesProxy>();
    public Ownables GetSoleOwner() => assignableProxy.Get()?.GetSoleOwner();
    public IEnumerable<Ownables> GetOwners() => assignableProxy.Get()?.GetOwners();
    public bool IsNull() => false;
    public int NumOwners() => 1;
}

internal sealed class MinionAssignablesProxy : KMonoBehaviour, IAssignableIdentity
{
    internal MinionIdentity Target { get; set; }
    internal GameObject GetTargetGameObject() => Target?.gameObject;
    public Ownables GetSoleOwner() => GetComponent<Ownables>();
    public IEnumerable<Ownables> GetOwners() => new[] { GetSoleOwner() };
    public bool IsNull() => Target == null;
    public int NumOwners() => Target == null ? 0 : 1;
}

internal sealed class Ownables : KMonoBehaviour
{
    internal List<AssignableSlotInstance> Slots { get; } = new List<AssignableSlotInstance>();
}

internal sealed class Equipment : KMonoBehaviour
{
    internal List<AssignableSlotInstance> Slots { get; } = new List<AssignableSlotInstance>();
}

internal sealed class AssignableSlot
{
    internal string Id { get; set; }
    internal string Name { get; set; }
}

internal class AssignableSlotInstance
{
    internal string ID { get; set; }
    internal AssignableSlot slot { get; set; }
    internal KMonoBehaviour assignables { get; set; }
    internal Assignable assignable { get; set; }
    internal int UnassignCalls { get; private set; }
    internal bool IsAssigned() => assignable != null;
    internal bool IsUnassigning() => false;
    internal void Unassign()
    {
        UnassignCalls++;
        assignable?.Unassign();
        assignable = null;
    }
}

internal sealed class EquipmentSlotInstance : AssignableSlotInstance { }

internal class Assignable : KMonoBehaviour
{
    internal string slotID { get; set; }
    internal AssignableSlot slot { get; set; }
    internal bool CanBeAssigned { get; set; } = true;
    internal bool canBePublic { get; set; }
    internal IAssignableIdentity assignee { get; set; }
    internal Func<MinionAssignablesProxy, bool> AssignmentPrecondition { get; set; } = proxy => true;
    internal int AssignCalls { get; private set; }
    internal int UnassignCalls { get; private set; }
    internal int PreconditionCalls { get; private set; }
    internal IAssignableIdentity LastCheckedIdentity { get; private set; }
    private AssignableSlotInstance assignedSlot;

    // Native Assignable.CanAssignTo short-circuits raw MinionIdentity. Only the
    // proxy reaches registered native/mod preconditions. CanBeAssigned is the
    // side-screen gate, not an implicit gate inside Assign.
    internal bool CanAssignTo(IAssignableIdentity identity)
    {
        LastCheckedIdentity = identity;
        var proxy = identity as MinionAssignablesProxy;
        if (proxy == null) return true;
        PreconditionCalls++;
        return AssignmentPrecondition(proxy);
    }

    internal bool IsAssigned() => assignee != null && !assignee.IsNull();
    internal void Assign(IAssignableIdentity identity, AssignableSlotInstance specificSlot = null)
    {
        AssignCalls++;
        if (!CanAssignTo(identity)) return;
        assignee = identity;
        assignedSlot = specificSlot;
        if (specificSlot != null) specificSlot.assignable = this;
    }

    internal void Unassign()
    {
        UnassignCalls++;
        assignee = null;
        if (assignedSlot != null) assignedSlot.assignable = null;
        assignedSlot = null;
    }
}

internal sealed class Equippable : Assignable
{
    internal bool isEquipped { get; set; }
}

internal static class BionicUpgradesMonitor
{
    internal sealed class Instance
    {
        internal UpgradeComponentSlot[] upgradeComponentSlots { get; set; }
    }

    internal sealed class UpgradeComponentSlot
    {
        internal AssignableSlotInstance Slot { get; set; }
        internal bool IsLocked { get; set; }
        internal AssignableSlotInstance GetAssignableSlotInstance() => Slot;
    }
}

internal sealed class Db
{
    private static readonly Db instance = new Db();
    internal static Db Get() => instance;
    internal TestAssignableSlots AssignableSlots { get; } = new TestAssignableSlots();
    internal TestAmounts Amounts { get; } = new TestAmounts();
    internal sealed class TestAssignableSlots
    {
        internal AssignableSlot BionicUpgrade { get; } = new AssignableSlot { Id = "BionicUpgrade", Name = "Booster" };
        internal AssignableSlot MedicalBed { get; } = new AssignableSlot { Id = "MedicalBed", Name = "Medical bed" };
    }
    internal sealed class TestAmounts
    {
        internal object Stress { get; } = new object();
        internal object RadiationBalance { get; } = new object();
    }
}

internal sealed partial class Game
{
    internal TestAssignmentManager assignmentManager { get; } = new TestAssignmentManager();
    internal static bool IsDlcActiveForCurrentSave(string id) => false;
}

internal sealed class TestAssignmentManager
{
    internal Dictionary<string, IAssignableIdentity> assignment_groups { get; } = new Dictionary<string, IAssignableIdentity>();
}

internal sealed class Chore
{
    internal bool RunUntilComplete { get; set; } = true;
    internal bool InProvider { get; private set; } = true;
    internal int CancelCalls { get; private set; }
    internal int FailCalls { get; private set; }
    internal Action OnFail { get; set; }
    public void Cancel(string reason) { CancelCalls++; InProvider = false; }
    public void Cancel() => Cancel("cancel");

    // StandardChoreBase.Cancel removes the provider chore. Driver.StopChore
    // instead fails the worker's execution. A persistent WorkChore survives;
    // transient MoveChore (runUntilComplete=false) ends normally on failure.
    internal void Fail()
    {
        if (!InProvider) return;
        FailCalls++;
        if (!RunUntilComplete) Cancel("native transient chore failure");
        OnFail?.Invoke();
    }
}

internal sealed class ChoreDriver : KMonoBehaviour
{
    internal Chore Current { get; set; }
    internal int StopCalls { get; private set; }
    internal Chore GetCurrentChore() => Current;
    internal void StopChore()
    {
        StopCalls++;
        Current?.Fail();
        Current = null;
    }
    internal bool TryTake(Chore chore)
    {
        if (!chore.InProvider) return false;
        Current = chore;
        return true;
    }
}

// Retained to compile the previous reflection-based production version for
// known-red validation; neither has the speculative cancel APIs at the pin.
internal sealed class ChoreConsumer : KMonoBehaviour { }
internal sealed class MinionBrain : KMonoBehaviour { }

internal sealed class Navigator : KMonoBehaviour
{
    internal bool Reachable { get; set; } = true;
    internal bool CanReach(int cell) => Reachable;
}

internal static class MoveToLocationMonitor
{
    internal sealed class Instance
    {
        internal int MoveCalls { get; private set; }
        internal int TargetCell { get; private set; } = -1;
        internal bool Active { get; set; }
        internal void MoveToLocation(int cell) { MoveCalls++; TargetCell = cell; Active = true; }
    }
}

internal sealed class Health : KMonoBehaviour
{
    internal float hitPoints { get; set; }
    internal float maxHitPoints { get; set; } = 100;
}

internal interface ISliderControl
{
    float GetSliderMin(int index);
    float GetSliderMax(int index);
    float GetSliderValue(int index);
    string GetSliderTooltip(int index);
    void SetSliderValue(float value, int index);
}

internal sealed class Clinic : KMonoBehaviour, ISliderControl
{
    internal float Threshold { get; set; } = 50;
    internal int SliderWrites { get; private set; }
    internal float MedicalAttentionMinimum => Threshold / 100;
    public float GetSliderMin(int index) => 0;
    public float GetSliderMax(int index) => 100;
    public float GetSliderValue(int index) => Threshold;
    public string GetSliderTooltip(int index) => Threshold.ToString();
    public void SetSliderValue(float value, int index) { SliderWrites++; Threshold = value; }
}

internal sealed class Prioritizable : KMonoBehaviour
{
    internal PrioritySetting GetMasterPriority() => default(PrioritySetting);
}

internal struct PrioritySetting
{
    internal int priority_class { get; set; }
    internal int priority_value { get; set; }
}

namespace Klei.AI
{
    internal sealed class Sicknesses { internal List<SicknessInstance> ModifierList { get; } = new List<SicknessInstance>(); }
    internal sealed class SicknessInstance
    {
        internal TestSickness Sickness { get; set; }
        internal TestExposure ExposureInfo { get; set; }
    }
    internal sealed class TestSickness
    {
        internal string id { get; set; }
        internal string Name { get; set; }
        internal object DescriptiveSymptoms { get; set; }
    }
    internal sealed class TestExposure { internal string sourceInfo { get; set; } }
    internal sealed class Amounts { internal TestAmount Get(object amount) => throw new NotSupportedException(); }
    internal sealed class TestAmount { internal float value { get; set; } }
}

internal sealed class DoctorStation : KMonoBehaviour
{
    internal Storage storage { get; set; }
    internal bool IsTreatmentAvailable(GameObject dupe) => throw new NotSupportedException();
    internal bool IsDoctorAvailable(GameObject dupe) => throw new NotSupportedException();
}
internal sealed class MedicinalPill { internal TestPillInfo info { get; set; } }
internal sealed class TestPillInfo { internal List<string> curedSicknesses { get; set; } }
internal sealed class Operational { internal bool IsOperational { get; set; } }

internal static partial class Components
{
    internal static class AssignableItems { internal static readonly List<Assignable> Items = new List<Assignable>(); }
    internal static class LiveMinionIdentities { internal static readonly List<MinionIdentity> Items = new List<MinionIdentity>(); }
    internal static class Clinics { internal static readonly List<Clinic> Items = new List<Clinic>(); }
    internal static class BuildingCompletes { internal static readonly List<BuildingComplete> Items = new List<BuildingComplete>(); }
}

namespace OniMcp.Tools
{
    public static partial class ToolUtil
    {
        internal static float? GetFloat(JObject args, string key) => (float?)args[key];
        internal static int ClampLimit(JObject args, int fallback, int maximum) => Math.Max(1, Math.Min((int?)args["limit"] ?? fallback, maximum));
        internal static int ResolveWorldId(JObject args, int fallback) => (int?)args["worldId"] ?? fallback;
        internal static bool GameObjectMatchesWorld(GameObject go, int worldId) => worldId < 0 || go.WorldId == worldId;
        internal static bool CellMatchesWorld(int cell, int worldId) => worldId < 0 || Grid.WorldIdx[cell] == worldId;
        internal static MinionIdentity FindDupe(JObject args)
        {
            int? id = (int?)args["id"];
            string name = (string)args["name"];
            return Components.LiveMinionIdentities.Items.FirstOrDefault(dupe =>
                id.HasValue ? dupe.GetComponent<KPrefabID>()?.InstanceID == id.Value : dupe.GetProperName() == name);
        }
        internal static bool TryResolveSearchCell(JObject args, out int x, out int y, out string error)
        {
            throw new NotSupportedException();
        }
    }

    public static partial class DuplicantTools
    {
        private static McpTool ControlDupeInfo() => throw new NotSupportedException();
        private static McpTool ControlPersonalPriority() => throw new NotSupportedException();
        private static McpTool ControlHat() => throw new NotSupportedException();
        private static McpTool ControlDupeSideScreens() => throw new NotSupportedException();
        private static McpTool ControlSkill() => throw new NotSupportedException();
        private static McpTool RenameDupe() => throw new NotSupportedException();
        private static McpTool AutoRenameDupes() => throw new NotSupportedException();
        private static object DupeRef(MinionIdentity dupe) => new { id = dupe.GetComponent<KPrefabID>()?.InstanceID, name = dupe.GetProperName() };
        private static bool Contains(string value, string query) => value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static class ReceptacleTools
    {
        internal static McpTool ControlStorageTileSelection() => throw new NotSupportedException();
        internal static McpTool ControlReceptacle() => throw new NotSupportedException();
    }
}
