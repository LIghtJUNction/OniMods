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
        Reset();
        ImmigrantScreen.instance = null;
        Immigration.Instance = null;
        Console.WriteLine("Printing-pod production claim regression checks passed: " + assertions);
    }

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
        => telepad.Deliveries.Count == 0 && Immigration.Instance.EndCalls == 0 && UnityEngine.Object.Destroyed.Count == 0;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
