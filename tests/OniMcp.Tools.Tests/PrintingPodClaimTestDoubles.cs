using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

// The production claim flow and cleanup helper are linked into the test project.
// These game/UI boundaries model the pinned lifecycle, not Unity acceptance.
internal sealed partial class Immigration
{
    internal int EndCalls { get; private set; }
    internal float GetTimeRemaining() => 0f;
    internal void EndImmigration() { EndCalls++; ImmigrantsAvailable = false; }
}

internal sealed class Telepad
{
    internal GameObject gameObject { get; } = new GameObject();
    internal readonly List<CarePackageInfo> Deliveries = new List<CarePackageInfo>();
    internal void OnAcceptDelivery(CarePackageInfo info)
    {
        Deliveries.Add(info);
        // Pinned ONI source: Telepad.OnAcceptDelivery already ends immigration.
        Immigration.Instance.EndImmigration();
    }
}

internal sealed class CarePackageInfo
{
    internal string id { get; set; }
    internal float quantity { get; set; }
    internal string facadeID { get; set; }
    internal Func<bool> requirement { get; set; }
}

internal interface ITelepadDeliverableContainer
{
    GameObject GetGameObject();
}

internal sealed class CarePackageContainer : ITelepadDeliverableContainer
{
    // The static registry may retain stale entries after screen choices clear.
    internal static readonly List<ITelepadDeliverableContainer> containers = new List<ITelepadDeliverableContainer>();
    internal CarePackageInfo Info { get; set; }
    private readonly GameObject gameObject = new GameObject();
    public GameObject GetGameObject() => gameObject;
}

internal sealed class PrintingPodTestDupeContainer : ITelepadDeliverableContainer
{
    private readonly GameObject gameObject = new GameObject();
    public GameObject GetGameObject() => gameObject;
}

internal class CharacterSelectionController
{
    private List<ITelepadDeliverableContainer> containers;
    internal List<ITelepadDeliverableContainer> Choices
    {
        get { return containers; }
        set { containers = value; }
    }
}

internal sealed class ImmigrantScreen : CharacterSelectionController
{
    internal static ImmigrantScreen instance { get; set; }
    internal bool ThrowOnHide { get; set; }
    internal int HideAttempts { get; private set; }
    internal void Show(bool visible)
    {
        if (visible)
            throw new InvalidOperationException("Claim must never open the immigrant screen");
        HideAttempts++;
        if (ThrowOnHide)
            throw new InvalidOperationException("native screen hide failure");
    }
}

internal static partial class Components
{
    internal static class Telepads
    {
        internal static readonly List<Telepad> Items = new List<Telepad>();
    }
}

internal static partial class Grid
{
    internal static int PosToCell(Telepad target) => XYToCell(1, 1);
}

internal static partial class Assets
{
    internal static GameObject GetPrefab(string id) => null;
}

internal static class PrintingPodGameObjectExtensions
{
    internal static int GetMyWorldId(this GameObject target) => 0;
}

namespace UnityEngine
{
    internal static class Object
    {
        internal static readonly List<GameObject> Destroyed = new List<GameObject>();
        internal static void Destroy(GameObject target) { Destroyed.Add(target); }
    }
}

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        internal static CallToolResult TestClaim(JObject args, Telepad telepad)
            => ClaimPrintingReward(args, telepad, new Dictionary<string, object>());
        internal static List<CarePackageInfo> TestRewards() => CurrentCarePackages().ToList();
        private static GameObject FindBuildingTarget(JObject args, Func<GameObject, bool> predicate) => throw new NotSupportedException();
        private static bool HasRectInput(JObject args) => throw new NotSupportedException();
        private static bool MatchesQuery(Dictionary<string, object> info, string query)
            => JsonConvert.SerializeObject(info).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        private static CallToolResult JsonResult(Dictionary<string, object> payload)
            => CallToolResult.Text(JsonConvert.SerializeObject(payload));
        private static Dictionary<string, object> TelepadInfo(Telepad telepad, bool includeVictory)
            => new Dictionary<string, object> { ["deliveries"] = telepad.Deliveries.Count };
    }
}
