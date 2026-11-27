using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class PrintingPodClaimHandlerRegression
{
    private static int assertions;

    internal static void Run()
    {
        var telepad = Reset();
        var stale = CarePackage("stale");
        CarePackageContainer.containers.Add(stale);
        var result = FacilitySideScreenTools.TestClaim(Confirmed(), telepad);
        Check(result.IsError && NoWrites(telepad),
            "empty screen options must reject stale registry rewards before any write");

        ImmigrantScreen.instance = null;
        result = FacilitySideScreenTools.TestClaim(Confirmed(), telepad);
        Check(result.IsError && NoWrites(telepad),
            "missing screen must reject stale rewards before any write");
        ImmigrantScreen.instance = new ImmigrantScreen { Choices = null };
        result = FacilitySideScreenTools.TestClaim(Confirmed(), telepad);
        Check(result.IsError && NoWrites(telepad),
            "missing native screen option list must reject the claim before any write");

        var current = CarePackage("current");
        var screen = new ImmigrantScreen
        {
            Choices = new List<ITelepadDeliverableContainer> { current }
        };
        ImmigrantScreen.instance = screen;
        Check(FacilitySideScreenTools.TestRewards().SequenceEqual(new[] { current.Info }),
            "only the current native screen choices may be listed; static registry entries are not claim authority");
        result = FacilitySideScreenTools.TestClaim(new JObject { ["dryRun"] = true }, telepad);
        Check(!result.IsError && NoWrites(telepad) && screen.HideAttempts == 0 && screen.Choices.Count == 1,
            "dry run must preserve delivery, immigration, screen, and container state");
        result = FacilitySideScreenTools.TestClaim(new JObject(), telepad);
        Check(result.IsError && NoWrites(telepad) && screen.HideAttempts == 0,
            "unconfirmed claim must not close the screen or deliver a reward");

        // The production priority plan reads the pod position after reward
        // enumeration but before cleanup preflight. Invalidate that selected
        // choice at the game boundary so this cannot take the empty-list exit.
        telepad.BeforeNextPositionRead = () => screen.Choices.Remove(current);
        result = FacilitySideScreenTools.TestClaim(Confirmed(), telepad);
        Check(result.IsError && result.Content[0].Text.Contains("reward UI state is unavailable")
            && screen.Choices.Count == 0 && NoWrites(telepad) && screen.HideAttempts == 0,
            "lost selected reward must fail cleanup preflight before closing, delivery, immigration end, or destruction");
        screen.Choices.Add(current);

        screen.ThrowOnHide = true;
        result = FacilitySideScreenTools.TestClaim(Confirmed(), telepad);
        Check(result.IsError && NoWrites(telepad) && screen.Choices.Count == 1 && screen.HideAttempts == 1,
            "screen hide failure must preserve the available choices and reject before delivery or destruction");

        telepad = Reset();
        current = CarePackage("current");
        var dupe = new PrintingPodTestDupeContainer();
        screen = ImmigrantScreen.instance;
        screen.Choices.Add(current);
        screen.Choices.Add(dupe);
        CarePackageContainer.containers.Add(current);
        result = FacilitySideScreenTools.TestClaim(Confirmed(), telepad);
        Check(!result.IsError && telepad.Deliveries.Count == 1 && ReferenceEquals(telepad.Deliveries[0], current.Info)
            && Immigration.Instance.EndCalls == 1 && screen.HideAttempts == 1,
            "successful claim must deliver the current reward and end immigration exactly once");
        Check(screen.Choices.Count == 0 && UnityEngine.Object.Destroyed.SequenceEqual(new[]
            { current.GetGameObject(), dupe.GetGameObject() }),
            "successful claim must synchronously invalidate and destroy all current options in order");
        Check(CarePackageContainer.containers.Count == 1,
            "the stale static registry fixture must remain populated to exercise replay safety");
        result = FacilitySideScreenTools.TestClaim(Confirmed(), telepad);
        Check(result.IsError && telepad.Deliveries.Count == 1 && Immigration.Instance.EndCalls == 1
            && UnityEngine.Object.Destroyed.Count == 2,
            "immediate repeat must not deliver, end immigration, or destroy options a second time");
        Immigration.Instance.ImmigrantsAvailable = true;
        result = FacilitySideScreenTools.TestClaim(Confirmed(), telepad);
        Check(result.IsError && telepad.Deliveries.Count == 1 && Immigration.Instance.EndCalls == 1
            && UnityEngine.Object.Destroyed.Count == 2 && screen.HideAttempts == 1,
            "a later available cycle must not revive old static options before new screen choices exist");
        CheckDuplicantOffers();
        Reset();
        ImmigrantScreen.instance = null;
        Immigration.Instance = null;
        Console.WriteLine("Printing-pod production claim regression checks passed: " + assertions);
    }

    private static void CheckDuplicantOffers()
    {
        var pod = Reset();
        var screen = ImmigrantScreen.instance;
        screen.Telepad = pod;
        var otherPod = new Telepad();
        var first = Candidate("Ada");
        var second = Candidate("Ada");
        var package = CarePackage("Dirt");
        first.Stats.personality = new TestPrintingResource("Nisbet");
        first.Stats.Traits.Add(new TestPrintingResource("StrongArm"));
        first.Stats.StartingLevels["Athletics"] = 4;
        first.Stats.skillAptitudes[new TestPrintingResource("Research")] = 1f;
        first.Stats.stressTrait = new TestPrintingResource("BingeEater");
        first.Stats.joyTrait = new TestPrintingResource("StickerBomber");
        screen.Choices.Add(first);
        screen.Choices.Add(package);
        screen.Choices.Add(second);
        var ids = CandidateIds(pod);
        Check(ids.Count == 2 && ids.All(id => !string.IsNullOrWhiteSpace(id))
            && ids.Distinct(StringComparer.Ordinal).Count() == 2
            && CandidateIds(pod).SequenceEqual(ids),
            "two native candidates with the same name must have distinct, stable IDs");

        var details = (JObject)((JArray)JObject.Parse(
            FacilitySideScreenTools.TestImmigrants(pod).Content[0].Text)["candidates"])[0];
        Check((string)details["name"] == "Ada" && (string)details["personalityId"] == "Nisbet"
            && details["traits"].Values<string>().SequenceEqual(new[] { "StrongArm" })
            && (int)details["startingLevels"]["Athletics"] == 4
            && (float)details["aptitudes"]["Research"] == 1f
            && (string)details["stressTrait"] == "BingeEater"
            && (string)details["joyTrait"] == "StickerBomber",
            "list should expose the live starting stats, traits, interests and reactions");
        Check(FacilitySideScreenTools.TestRewards().SequenceEqual(new[] { package.Info }),
            "only the care package may appear in the legacy reward list");

        var selected = ids[1];
        screen.AddDeliverable(first.Stats);
        var selectionChanges = screen.SelectionChanges;
        var preview = DuplicantArgs(selected);
        preview["dryRun"] = true;
        var result = FacilitySideScreenTools.TestDuplicantClaim(preview, pod);
        Check(!result.IsError && screen.IsSelected(first.Stats)
            && screen.SelectionChanges == selectionChanges && screen.ProceedCalls == 0
            && NoWrites(pod) && screen.HideAttempts == 0 && screen.Choices.Count == 3,
            "preview, even with confirmation, must preserve the native selection and the offer");

        var noConfirm = new JObject { ["candidateId"] = selected };
        var byIndex = new JObject { ["rewardIndex"] = 1, ["confirm"] = true };
        var byName = new JObject { ["name"] = "Ada", ["confirm"] = true };
        var nonString = new JObject { ["candidateId"] = 1, ["confirm"] = true };
        var textConfirm = new JObject { ["candidateId"] = selected, ["confirm"] = "true" };
        var badDryRun = DuplicantArgs(selected);
        badDryRun["dryRun"] = "not a boolean";
        foreach (var invalid in new[] { noConfirm, byIndex, byName, nonString, textConfirm, badDryRun })
        {
            result = FacilitySideScreenTools.TestDuplicantClaim(invalid, pod);
            Check(result.IsError && NoWrites(pod) && screen.ProceedCalls == 0
                && screen.SelectionChanges == selectionChanges && screen.IsSelected(first.Stats),
                "invalid or unconfirmed duplicant request must preserve the native selection");
        }

        var wrongList = FacilitySideScreenTools.TestImmigrants(otherPod);
        Check(wrongList.IsError || !JObject.Parse(wrongList.Content[0].Text)["candidates"].Any(),
            "another telepad must not be able to list this offer's candidates");
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(selected), otherPod);
        Check(result.IsError && NoWrites(pod) && NoWrites(otherPod) && screen.ProceedCalls == 0
            && screen.SelectionChanges == selectionChanges && screen.IsSelected(first.Stats),
            "a valid ID must not authorize a different telepad");

        screen.Telepad = otherPod;
        var transferred = CandidateIds(otherPod);
        Check(!transferred.Intersect(ids).Any(), "rebinding the same materialized choices must issue pod-specific IDs");
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(selected), otherPod);
        Check(result.IsError && screen.ProceedCalls == 0, "the new owner cannot redeem the previous pod's ID");
        screen.Telepad = pod;
        selected = CandidateIds(pod)[1];

        var originalStats = second.Stats;
        second.Stats = Candidate("Ada").Stats;
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(selected), pod);
        Check(result.IsError && NoWrites(pod) && screen.ProceedCalls == 0,
            "replacing candidate stats in the same native container must expire the old ID");
        var refreshed = CandidateIds(pod);
        Check(!refreshed.Contains(selected), "a refreshed candidate must receive a fresh ID");
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(refreshed[1]), pod);
        Check(!result.IsError && pod.DuplicantDeliveries.Count == 1
            && ReferenceEquals(pod.DuplicantDeliveries[0], second.Stats)
            && !ReferenceEquals(pod.DuplicantDeliveries[0], originalStats)
            && pod.Deliveries.Count == 0 && screen.ProceedCalls == 1
            && Immigration.Instance.EndCalls == 1 && screen.HideAttempts == 1
            && screen.Choices.Count == 0,
            "confirmed selection replaces the prior selection and lets the native screen deliver once");
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(refreshed[1]), pod);
        Check(result.IsError && screen.ProceedCalls == 1 && pod.DuplicantDeliveries.Count == 1
            && Immigration.Instance.EndCalls == 1,
            "repeating a successful claim must not deliver or end immigration again");

        pod = Reset();
        screen = ImmigrantScreen.instance;
        screen.Telepad = pod;
        screen.Choices.Add(Candidate("Mae"));
        screen.Choices.Add(package);
        selected = CandidateIds(pod).Single();
        result = FacilitySideScreenTools.TestClaim(Confirmed(), pod);
        Check(!result.IsError && pod.Deliveries.SequenceEqual(new[] { package.Info })
            && pod.DuplicantDeliveries.Count == 0, "legacy claim still selects only the care package in a mixed offer");
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(selected), pod);
        Check(result.IsError && screen.ProceedCalls == 0 && Immigration.Instance.EndCalls == 1,
            "claiming a package retires all duplicant IDs for that offer");

        pod = Reset();
        screen = ImmigrantScreen.instance;
        screen.Telepad = pod;
        first = Candidate("Mae");
        screen.Choices.Add(first);
        selected = CandidateIds(pod).Single();
        Immigration.Instance.ImmigrantsAvailable = false;
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(selected), pod);
        Check(result.IsError && NoWrites(pod) && screen.ProceedCalls == 0,
            "cooldown rejects an otherwise still materialized candidate");
        Immigration.Instance.ImmigrantsAvailable = true;
        screen.Choices.Clear();
        second = Candidate("Mae");
        screen.Choices.Add(second);
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(selected), pod);
        Check(result.IsError && NoWrites(pod) && screen.ProceedCalls == 0,
            "a new offer at the previous slot cannot be claimed using an old ID");

        selected = CandidateIds(pod).Single();
        screen.ThrowAfterDelivery = true;
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(selected), pod);
        Check(result.IsError && screen.ProceedCalls == 1 && pod.DuplicantDeliveries.Count == 1,
            "a native exception after delivery must not be reported as a successful claim");
        // A native exception may leave the choices and readiness apparently unchanged.
        // Restore readiness independently so only invalidation can prevent this replay.
        Immigration.Instance.ImmigrantsAvailable = true;
        result = FacilitySideScreenTools.TestDuplicantClaim(DuplicantArgs(selected), pod);
        Check(result.IsError && screen.ProceedCalls == 1 && pod.DuplicantDeliveries.Count == 1,
            "a partially completed native proceed must never retry the same candidate");
    }

    private static List<string> CandidateIds(Telepad pod)
    {
        var result = FacilitySideScreenTools.TestImmigrants(pod);
        Check(!result.IsError, "the active pod must list its materialized offer");
        return ((JArray)JObject.Parse(result.Content[0].Text)["candidates"])
            .Select(candidate => (string)candidate["candidateId"]).ToList();
    }

    private static JObject DuplicantArgs(string candidateId)
        => new JObject { ["candidateId"] = candidateId, ["confirm"] = true };

    private static CharacterContainer Candidate(string name)
        => new CharacterContainer { Stats = new MinionStartingStats { Name = name } };

    private static Telepad Reset()
    {
        Immigration.Instance = new Immigration { ImmigrantsAvailable = true };
        ImmigrantScreen.instance = new ImmigrantScreen { Choices = new List<ITelepadDeliverableContainer>() };
        CarePackageContainer.containers.Clear();
        UnityEngine.Object.Destroyed.Clear();
        Components.Telepads.Items.Clear();
        return new Telepad();
    }

    private static CarePackageContainer CarePackage(string id)
        => new CarePackageContainer { Info = new CarePackageInfo { id = id, quantity = 100f } };
    private static JObject Confirmed() => new JObject { ["confirm"] = true };
    private static bool NoWrites(Telepad telepad)
        => telepad.Deliveries.Count == 0 && telepad.DuplicantDeliveries.Count == 0
            && Immigration.Instance.EndCalls == 0 && UnityEngine.Object.Destroyed.Count == 0;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
