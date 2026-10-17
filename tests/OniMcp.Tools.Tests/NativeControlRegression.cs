using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;
using UnityEngine;

internal static class NativeControlRegression
{
    private static int checks;

    internal static void Run()
    {
        checks = 0;
        var failures = new List<string>();
        RunCase("#266 worker cancellation", CheckWorkerCancellation, failures);
        RunCase("#313 generic assignment", CheckGenericAssignment, failures);
        RunCase("#313 medical assignment", CheckMedicalAssignment, failures);
        RunCase("#308 native bionic slots", CheckBionicSlots, failures);
        RunCase("#321 tree filters", CheckTreeFilters, failures);
        RunCase("#321 flat filters", CheckFlatFilters, failures);
        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("Native control production-handler checks passed: " + checks);
    }

    private static void RunCase(string name, Action action, List<string> failures)
    {
        Reset();
        try { action(); }
        catch (Exception error) { failures.Add(name + ": " + error.Message); }
        finally { Reset(); }
    }

    private static void CheckWorkerCancellation()
    {
        var dupe = AddDupe(1);
        var other = AddDupe(2);
        var driver = dupe.GetComponent<ChoreDriver>();
        var job = new Chore();
        driver.Current = job;
        JObject result = Success(Command(1, "cancel_all"));
        Check(driver.StopCalls == 1 && driver.Current == null && job.FailCalls == 1,
            "cancel_all releases the selected worker through its driver");
        Check(job.InProvider && job.CancelCalls == 0 && other.GetComponent<ChoreDriver>().TryTake(job),
            "a persistent colony job remains available for another worker");
        Check((bool?)result["cancelled"]?["stoppedDriver"] == true
            && (bool?)result["cancelled"]?["cancelledCurrentChore"] == false,
            "response reports driver release without claiming provider cancellation");

        var moveJob = new Chore { RunUntilComplete = false };
        var monitor = dupe.GetSMI<MoveToLocationMonitor.Instance>();
        monitor.Active = true;
        moveJob.OnFail = () => monitor.Active = false;
        driver.Current = moveJob;
        Success(Command(1, "cancel_all"));
        Check(!monitor.Active && !moveJob.InProvider && moveJob.FailCalls == 1 && moveJob.CancelCalls == 1,
            "a current direct move ends through native transient chore failure");

        var anotherJob = new Chore();
        driver.Current = anotherJob;
        Success(Command(1, "cancel_all_and_move"));
        Check(anotherJob.InProvider && anotherJob.CancelCalls == 0 && driver.Current == null,
            "cancel_all_and_move preserves the old colony job");
        Check(monitor.MoveCalls == 1 && monitor.TargetCell == Grid.XYToCell(8, 9),
            "cancel_all_and_move still issues the native move command");

        int stops = driver.StopCalls;
        var noConfirm = CommandArgs(1, "cancel_all_and_move");
        noConfirm["confirm"] = false;
        Check(DuplicantTools.ControlDupes().Handler(noConfirm).IsError
            && driver.StopCalls == stops && monitor.MoveCalls == 1,
            "unconfirmed commands do not mutate jobs or movement");

        var noDriver = new MinionIdentity();
        AddObject(3, "NoDriver").Add(noDriver);
        Components.LiveMinionIdentities.Items.Add(noDriver);
        result = Success(Command(3, "cancel_all"));
        Check((bool?)result["cancelled"]?["stoppedDriver"] == false,
            "missing driver is reported without speculative cancel calls");
    }

    private static void CheckGenericAssignment()
    {
        var dupe = AddDupe(1);
        var target = AddAssignable(10, Db.Get().AssignableSlots.MedicalBed);
        target.AssignmentPrecondition = proxy => false;
        Check(Assign(10, 1).IsError && target.AssignCalls == 0 && target.assignee == null,
            "rejected native preconditions cause no Assign call");
        Check(target.LastCheckedIdentity == dupe.assignableProxy.Get() && target.PreconditionCalls > 0,
            "preconditions receive the minion proxy identity");

        target.AssignmentPrecondition = proxy => proxy.GetTargetGameObject().WorldId == target.gameObject.WorldId;
        AddDupe(2, 1);
        Check(Assign(10, 2).IsError && target.AssignCalls == 0, "unrelated-world owner is rejected");
        target.CanBeAssigned = false;
        Check(Assign(10, 1).IsError && target.AssignCalls == 0, "disabled native target is rejected");
        target.CanBeAssigned = true;
        var proxyIdentity = dupe.assignableProxy.Get();
        dupe.assignableProxy.Value = null;
        Check(Assign(10, 1).IsError && target.AssignCalls == 0, "missing proxy cannot bypass predicates");
        dupe.assignableProxy.Value = proxyIdentity;
        Success(Assign(10, 1));
        Check(target.AssignCalls == 1 && ReferenceEquals(target.assignee, proxyIdentity),
            "eligible native target receives the proxy assignment");
        target.CanBeAssigned = false;
        var unassign = AssignmentArgs(10, 1);
        unassign["assignmentAction"] = "unassign";
        Success(DuplicantTools.ControlDupes().Handler(unassign));
        Check(target.assignee == null && target.UnassignCalls == 1,
            "unassign remains available to clean up an invalid old assignment");
    }

    private static void CheckMedicalAssignment()
    {
        var dupe = AddDupe(1);
        var target = AddAssignable(20, Db.Get().AssignableSlots.MedicalBed);
        var clinic = new Clinic { Threshold = 50 };
        target.gameObject.Add(clinic);
        Components.Clinics.Items.Add(clinic);
        // Pinned Clinic.CanManuallyAssignTo checks health against the current
        // slider; Assignable.OnSpawn supplies the world/parent-world predicate.
        // The unrelated worlds in this fixture have no parent relationship.
        target.AssignmentPrecondition = proxy => proxy.Target.GetMyWorldId() == target.GetMyWorldId()
            && proxy.Target.GetComponent<Health>().hitPoints / proxy.Target.GetComponent<Health>().maxHitPoints < clinic.MedicalAttentionMinimum;

        var args = new JObject { ["id"] = 20, ["dupeId"] = 1, ["confirm"] = true, ["thresholdPercent"] = 100 };
        Check(MedicalTools.AssignMedicalBed().Handler(args).IsError,
            "healthy patient cannot bypass native clinic preconditions");
        Check(target.AssignCalls == 0 && target.assignee == null && clinic.SliderWrites == 0 && clinic.Threshold == 50,
            "rejected patient leaves both assignment and requested threshold untouched");
        dupe.GetComponent<Health>().hitPoints = 25;
        target.CanBeAssigned = false;
        Check(MedicalTools.AssignMedicalBed().Handler(args).IsError && clinic.SliderWrites == 0,
            "medical entry also respects CanBeAssigned before slider writes");
        target.CanBeAssigned = true;
        args["dupeId"] = 404;
        Check(MedicalTools.AssignMedicalBed().Handler(args).IsError && clinic.SliderWrites == 0,
            "unknown patient leaves threshold untouched");
        args["dupeId"] = 1;
        args["action"] = "invalid";
        Check(MedicalTools.AssignMedicalBed().Handler(args).IsError && clinic.SliderWrites == 0,
            "invalid medical action leaves threshold untouched");
        args["action"] = "assign";
        JObject result = Success(MedicalTools.AssignMedicalBed().Handler(args));
        Check(target.AssignCalls == 1 && target.assignee == dupe.assignableProxy.Get(),
            "injured eligible patient is assigned using native proxy identity");
        Check(clinic.SliderWrites == 1 && clinic.Threshold == 100 && (float?)result["thresholdPercent"] == 100,
            "successful assignment applies and reports the optional slider value");
        args["action"] = "unassign";
        args.Remove("thresholdPercent");
        Success(MedicalTools.AssignMedicalBed().Handler(args));
        Check(target.assignee == null && clinic.SliderWrites == 1, "medical unassign remains available");
    }

    private static void CheckBionicSlots()
    {
        var dupe = AddDupe(1);
        var ownables = dupe.assignableProxy.Get().GetComponent<Ownables>();
        var slotType = Db.Get().AssignableSlots.BionicUpgrade;
        var slot = new AssignableSlotInstance { ID = "custom-upgrade-slot", slot = slotType, assignables = ownables };
        ownables.Slots.Add(slot);
        var nativeSlot = new BionicUpgradesMonitor.UpgradeComponentSlot { Slot = slot, IsLocked = true };
        var monitor = new BionicUpgradesMonitor.Instance { upgradeComponentSlots = new[] { nativeSlot } };
        dupe.gameObject.Add(monitor);
        var booster = AddAssignable(30, slotType);
        var args = new JObject
        {
            ["domain"] = "assignable", ["action"] = "set_slot", ["dupeId"] = 1,
            ["slotInstanceId"] = slot.ID, ["assignableId"] = 30, ["confirm"] = true
        };

        Check(DuplicantTools.ControlDupes().Handler(args).IsError && booster.AssignCalls == 0
            && booster.UnassignCalls == 0 && slot.UnassignCalls == 0 && slot.assignable == null,
            "locked native slot rejects a valid booster before any assignment mutation");
        nativeSlot.IsLocked = false;
        Success(DuplicantTools.ControlDupes().Handler(args));
        Check(slot.assignable == booster && booster.assignee == dupe.assignableProxy.Get(),
            "unlocked mapped native slot accepts a valid booster");

        nativeSlot.IsLocked = true;
        Check(DuplicantTools.ControlDupes().Handler(args).IsError && booster.UnassignCalls == 0,
            "locked replacement preserves the existing assignment");
        foreach (string action in new[] { "unassign", "none" })
        {
            slot.assignable = booster;
            booster.assignee = dupe.assignableProxy.Get();
            args["slotAction"] = action;
            Success(DuplicantTools.ControlDupes().Handler(args));
            Check(slot.assignable == null && booster.assignee == null,
                "locked slot still permits cleanup action " + action);
        }
        args["slotAction"] = "assign";
        int assignments = booster.AssignCalls;
        nativeSlot.Slot = new AssignableSlotInstance { ID = slot.ID, slot = slotType, assignables = ownables };
        nativeSlot.IsLocked = false;
        Check(DuplicantTools.ControlDupes().Handler(args).IsError && booster.AssignCalls == assignments,
            "matching ID cannot substitute for exact native slot instance mapping");
        monitor.upgradeComponentSlots = null;
        Check(DuplicantTools.ControlDupes().Handler(args).IsError && booster.AssignCalls == assignments,
            "unavailable native slot state rejects assignment");

        var normalType = new AssignableSlot { Id = "Hat", Name = "Hat" };
        var equipment = dupe.assignableProxy.Get().GetComponent<Equipment>();
        var normalSlot = new EquipmentSlotInstance { ID = "Hat", slot = normalType, assignables = equipment };
        equipment.Slots.Add(normalSlot);
        var hat = AddAssignable(31, normalType);
        args["slotInstanceId"] = "Hat";
        args["assignableId"] = 31;
        Success(DuplicantTools.ControlDupes().Handler(args));
        Check(normalSlot.assignable == hat, "ordinary equipment does not require a bionic monitor slot");
    }

    private static void CheckTreeFilters()
    {
        var tree = AddTree(40);
        foreach (string domain in new[] { "storage", "filter" })
        {
            foreach (string mode in new[] { "replace", "add" })
            {
                foreach (string invalid in new[] { "TypoTag", "UndiscoveredOre", "Water", "ForbiddenOre", "Solid" })
                {
                    var before = Snapshot(tree);
                    var request = FilterArgs(40, domain, mode, "Dirt", invalid);
                    Check(StorageTools.ControlStorageSystem().Handler(request).IsError,
                        domain + " " + mode + " rejects non-selectable tag " + invalid);
                    CheckUnchanged(tree, before, domain + " invalid tag " + invalid);
                }
            }
            var badModeBefore = Snapshot(tree);
            Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, domain, "typo", "Dirt")).IsError,
                domain + " rejects unknown mode");
            CheckUnchanged(tree, badModeBefore, domain + " unknown mode");
            JObject response = Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, domain, "replace", "Dirt", "SandStone")));
            Check(tree.GetTags().SetEquals(new[] { new Tag("Dirt"), new Tag("SandStone") }),
                domain + " accepts discovered leaves in the target storage category");
            Check(tree.GetFilterStorage().items.Count == 1, domain + " accepted existing content stays stored");
            if (domain == "storage")
                Check(((JArray)response["acceptedTags"]).Values<string>().Contains("SandStone"),
                    "storage response reflects the successfully updated filter");
            tree.Selected.Add(new Tag("OldInvalidSavedTag"));
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, domain, "remove", "OldInvalidSavedTag")));
            Check(!tree.Selected.Contains(new Tag("OldInvalidSavedTag")) && tree.Selected.Contains(new Tag("Dirt")),
                domain + " can remove an old invalid selection without losing valid selections");
        }

        JObject options = Success(FilterTools.ControlFilter().Handler(new JObject
        {
            ["action"] = "list", ["includeOptions"] = true, ["kind"] = "tree"
        }));
        var offered = options["filters"][0]["tree"]["options"].Select(tag => (string)tag["tag"]).ToList();
        Check(new HashSet<string>(offered).SetEquals(new[] { "Dirt", "SandStone" }),
            "read-side options use the same discovered/category/forbidden eligibility as writes");

        var firstStorage = tree.GetComponent<Storage>();
        var alternate = new Storage();
        alternate.storageFilters.Add(new Tag("Liquid"));
        tree.FilterStorage = alternate;
        Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, "storage", "replace", "Dirt")).IsError,
            "validation uses native GetFilterStorage rather than first component");
        Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, "filter", "replace", "Water")));
        Check(tree.Selected.SetEquals(new[] { new Tag("Water") }) && firstStorage.items.Count == 1,
            "alternate native filter storage supplies its own selectable category");
        DiscoveredResources.Instance = null;
        var beforeMissing = Snapshot(tree);
        Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, "filter", "add", "Water")).IsError,
            "unavailable discovery state cannot authorize adding a tag");
        CheckUnchanged(tree, beforeMissing, "missing discovery state");
        Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(40, "filter", "clear")));
        Check(tree.Selected.Count == 0, "clear remains available with unavailable discovery state");
    }

    private static void CheckFlatFilters()
    {
        var tree = AddTree(50);
        var flat = new FlatTagFilterable();
        flat.tagOptions.AddRange(new[] { new Tag("Dirt"), new Tag("Algae") });
        flat.selectedTags.Add(new Tag("Dirt"));
        tree.gameObject.Add(flat);
        DiscoveredResources.Instance = null;
        foreach (string domain in new[] { "storage", "filter" })
        {
            var before = Snapshot(tree);
            int selectCalls = flat.SelectCalls;
            Check(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, domain, "replace", "NoOption")).IsError,
                domain + " flat rejects tags outside tagOptions");
            CheckUnchanged(tree, before, domain + " invalid flat option");
            Check(flat.SelectCalls == selectCalls && flat.selectedTags.SequenceEqual(new[] { new Tag("Dirt") }),
                domain + " invalid flat option causes no SelectTag calls");
        }
        foreach (string domain in new[] { "storage", "filter" })
        {
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, domain, "add", "Algae")));
            Check(tree.GetFilterStorage().MassStored() == 42 && tree.GetFilterStorage().items.Count == 1,
                domain + " flat add never temporarily removes accepted Dirt or ejects its contents");
            Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, domain, "replace", "Dirt", "Algae")));
            Check(tree.GetFilterStorage().MassStored() == 42 && tree.GetFilterStorage().items.Count == 1,
                domain + " identical flat replace preserves existing contents");
        }
        // A replacement must add the new selection before removing the old one.
        // This also preserves a newly accepted item already present in storage.
        flat.selectedTags.Remove(new Tag("Algae"));
        tree.Selected.Remove(new Tag("Algae"));
        tree.GetFilterStorage().items.Add(AddObject(151, "Algae").Add(new PrimaryElement { Mass = 43 }));
        Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, "filter", "replace", "Algae")));
        Check(flat.selectedTags.SequenceEqual(new[] { new Tag("Algae") })
            && tree.Selected.SetEquals(flat.selectedTags), "valid flat options still use native SelectTag and synchronize tree selection");
        Check(tree.GetFilterStorage().MassStored() == 43
            && tree.GetFilterStorage().items.Single().GetComponent<KPrefabID>().PrefabTag == new Tag("Algae"),
            "flat replace preserves accepted Algae while dropping only deselected Dirt");
        Success(StorageTools.ControlStorageSystem().Handler(FilterArgs(50, "storage", "add", "Dirt")));
        Check(tree.Selected.SetEquals(new[] { new Tag("Dirt"), new Tag("Algae") })
            && new HashSet<Tag>(flat.selectedTags).SetEquals(tree.Selected), "storage route also keeps flat and tree selections synchronized");
        Check(tree.GetFilterStorage().MassStored() == 43, "flat add preserves existing accepted Algae");
    }

    private static JObject CommandArgs(int id, string command) => new JObject
    {
        ["domain"] = "command", ["action"] = "force_action", ["commandAction"] = command,
        ["id"] = id, ["x"] = 8, ["y"] = 9, ["confirm"] = true
    };
    private static CallToolResult Command(int id, string command) => DuplicantTools.ControlDupes().Handler(CommandArgs(id, command));
    private static JObject AssignmentArgs(int id, int dupeId) => new JObject
    {
        ["domain"] = "assignable", ["action"] = "set", ["targetId"] = id, ["dupeId"] = dupeId, ["confirm"] = true
    };
    private static CallToolResult Assign(int id, int dupeId) => DuplicantTools.ControlDupes().Handler(AssignmentArgs(id, dupeId));
    private static JObject FilterArgs(int id, string domain, string mode, params string[] tags) => new JObject
    {
        ["domain"] = domain, ["action"] = domain == "storage" ? "set_filter" : "set",
        ["id"] = id, ["kind"] = "tree", ["mode"] = mode, ["tags"] = new JArray(tags)
    };

    private static GameObject AddObject(int id, string name, int world = 0)
    {
        var go = new GameObject { name = name, WorldId = world, Cell = id + 1000 };
        go.Add(new KPrefabID { InstanceID = id, PrefabTag = new Tag(name) });
        Grid.WorldIdx[go.Cell] = world;
        return go;
    }

    private static MinionIdentity AddDupe(int id, int world = 0)
    {
        var dupe = new MinionIdentity();
        AddObject(id, "Dupe" + id, world).Add(dupe).Add(new ChoreDriver())
            .Add(new Navigator()).Add(new MoveToLocationMonitor.Instance()).Add(new Health { hitPoints = 100 });
        var proxy = new MinionAssignablesProxy { Target = dupe };
        new GameObject { name = "Dupe" + id, WorldId = world }.Add(proxy).Add(new Ownables()).Add(new Equipment());
        dupe.assignableProxy.Value = proxy;
        Components.LiveMinionIdentities.Items.Add(dupe);
        return dupe;
    }

    private static Assignable AddAssignable(int id, AssignableSlot slot, int world = 0)
    {
        var assignable = new Assignable { slot = slot, slotID = slot.Id };
        AddObject(id, "Item" + id, world).Add(assignable);
        assignable.AssignmentPrecondition = proxy => proxy.GetTargetGameObject().WorldId == world;
        Components.AssignableItems.Items.Add(assignable);
        return assignable;
    }

    private static TreeFilterable AddTree(int id)
    {
        var building = new BuildingComplete { Def = new BuildingDef { PrefabID = "StorageLocker", Name = "Storage" } };
        var storage = new Storage();
        var tree = new TreeFilterable();
        AddObject(id, "Storage" + id).Add(building).Add(storage).Add(tree);
        Components.BuildingCompletes.Items.Add(building);
        storage.storageFilters.Add(new Tag("Solid"));
        tree.Selected.Add(new Tag("Dirt"));
        tree.ForbiddenTags.Add(new Tag("ForbiddenOre"));
        storage.items.Add(AddObject(id + 100, "Dirt").Add(new PrimaryElement { Mass = 42 }));
        DiscoveredResources.Instance.Categories[new Tag("Solid")] = new HashSet<Tag>
        {
            new Tag("Dirt"), new Tag("SandStone"), new Tag("ForbiddenOre")
        };
        DiscoveredResources.Instance.Categories[new Tag("Liquid")] = new HashSet<Tag> { new Tag("Water") };
        return tree;
    }

    private sealed class TreeSnapshot
    {
        internal HashSet<Tag> Selected { get; set; }
        internal List<GameObject> Contents { get; set; }
        internal float Mass { get; set; }
        internal int Writes { get; set; }
    }
    private static TreeSnapshot Snapshot(TreeFilterable tree) => new TreeSnapshot
    {
        Selected = new HashSet<Tag>(tree.Selected), Contents = tree.GetFilterStorage().items.ToList(),
        Mass = tree.GetFilterStorage().MassStored(), Writes = tree.UpdateCalls
    };
    private static void CheckUnchanged(TreeFilterable tree, TreeSnapshot before, string description)
    {
        Check(tree.UpdateCalls == before.Writes && tree.Selected.SetEquals(before.Selected)
            && tree.GetFilterStorage().items.SequenceEqual(before.Contents) && tree.GetFilterStorage().MassStored() == before.Mass,
            description + " leaves filter, stored objects, and mass unchanged");
    }

    private static JObject Success(CallToolResult result)
    {
        Check(!result.IsError, "expected successful handler response: " + result.Content[0].Text);
        return JObject.Parse(result.Content[0].Text);
    }
    private static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(description);
    }
    private static void Reset()
    {
        Components.LiveMinionIdentities.Items.Clear();
        Components.AssignableItems.Items.Clear();
        Components.Clinics.Items.Clear();
        Components.BuildingCompletes.Items.Clear();
        Grid.WorldIdx.Clear();
        DiscoveredResources.Instance = new DiscoveredResources();
        Game.Instance = new Game();
    }
}
