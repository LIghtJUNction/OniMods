using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        // The native controller keeps its list while an offer is materialized and
        // allocates a fresh list for the next offer. Let the game own its lifetime.
        private static readonly ConditionalWeakTable<List<ITelepadDeliverableContainer>, ImmigrantOffer>
            ImmigrantOffers = new ConditionalWeakTable<List<ITelepadDeliverableContainer>, ImmigrantOffer>();

        private static readonly MethodInfo ProceedWithImmigrant = typeof(ImmigrantScreen).GetMethod(
            "OnProceed", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

        private sealed class ImmigrantOffer
        {
            internal ImmigrantScreen Screen;
            internal Telepad Pod;
            internal bool DeliveryAttempted;
            internal readonly Dictionary<CharacterContainer, ImmigrantCandidate> Candidates =
                new Dictionary<CharacterContainer, ImmigrantCandidate>();
        }

        private sealed class ImmigrantCandidate
        {
            internal readonly string Id = Guid.NewGuid().ToString("N");
            internal readonly MinionStartingStats Stats;

            internal ImmigrantCandidate(MinionStartingStats stats) { Stats = stats; }

            internal Dictionary<string, object> Describe()
            {
                var result = DuplicantCandidateInfo.Describe(Stats);
                result["candidateId"] = Id;
                return result;
            }
        }

        private static bool HasImmigrantOffer(Telepad pod, out List<ITelepadDeliverableContainer> containers)
        {
            containers = null;
            var screen = ImmigrantScreen.instance;
            return pod != null && screen != null && screen.Telepad == pod
                && Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable
                && TryGetImmigrantScreenContainers(out containers);
        }

        private static CallToolResult ListPrintingImmigrants(Telepad telepad)
        {
            var candidates = new List<Dictionary<string, object>>();
            List<ITelepadDeliverableContainer> containers;
            if (HasImmigrantOffer(telepad, out containers))
            {
                var offer = ImmigrantOffers.GetValue(containers, choices => new ImmigrantOffer());
                var screen = ImmigrantScreen.instance;
                if (offer.Screen != screen || offer.Pod != telepad)
                {
                    offer.Candidates.Clear();
                    offer.Screen = screen;
                    offer.Pod = telepad;
                }
                if (!offer.DeliveryAttempted)
                {
                    foreach (var card in containers.OfType<CharacterContainer>())
                    {
                        if (card == null || card.Stats == null || !card.Stats.IsValid)
                            continue;
                        ImmigrantCandidate candidate;
                        if (!offer.Candidates.TryGetValue(card, out candidate) || !ReferenceEquals(candidate.Stats, card.Stats))
                            offer.Candidates[card] = candidate = new ImmigrantCandidate(card.Stats);
                        candidates.Add(candidate.Describe());
                    }
                }
            }
            return JsonResult(new Dictionary<string, object>
            {
                ["candidateCount"] = candidates.Count,
                ["candidates"] = candidates
            });
        }

        private static CallToolResult ClaimPrintingDuplicant(JObject args, Telepad telepad)
        {
            if (args["candidateId"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)args["candidateId"]))
                return CallToolResult.Error("Provide the exact candidateId returned by list_immigrants.");
            if (args["dryRun"] != null && args["dryRun"].Type != JTokenType.Boolean)
                return CallToolResult.Error("dryRun must be a boolean.");
            bool preview = (bool?)args["dryRun"] == true;
            if (!preview && (args["confirm"]?.Type != JTokenType.Boolean || !(bool)args["confirm"]))
                return CallToolResult.Error("confirm=true required to recruit a duplicant.");

            List<ITelepadDeliverableContainer> containers;
            ImmigrantOffer offer;
            var screen = ImmigrantScreen.instance;
            if (!HasImmigrantOffer(telepad, out containers) || !ImmigrantOffers.TryGetValue(containers, out offer)
                || offer.DeliveryAttempted || offer.Pod != telepad || offer.Screen != screen)
                return CallToolResult.Error("No listed duplicant offer is claimable at this pod.");

            string id = (string)args["candidateId"];
            foreach (var card in containers.OfType<CharacterContainer>())
            {
                ImmigrantCandidate candidate;
                if (card == null || !offer.Candidates.TryGetValue(card, out candidate) || candidate.Id != id
                    || !ReferenceEquals(candidate.Stats, card.Stats) || !candidate.Stats.IsValid)
                    continue;

                var selected = candidate.Describe();
                if (preview)
                    return JsonResult(new Dictionary<string, object> { ["dryRun"] = true, ["candidate"] = selected });
                if (ProceedWithImmigrant == null)
                    return CallToolResult.Error("The native Printing Pod confirmation is unavailable.");
                if (!screen.IsSelected(candidate.Stats))
                {
                    screen.RemoveLast();
                    screen.AddDeliverable(candidate.Stats);
                }
                if (!screen.IsSelected(candidate.Stats))
                    return CallToolResult.Error("The native Printing Pod did not select this candidate.");

                // OnProceed delivers, ends immigration and retires the cards. A
                // native exception may occur after delivery, so never replay it.
                offer.DeliveryAttempted = true;
                try
                {
                    ProceedWithImmigrant.Invoke(screen, null);
                }
                catch (TargetInvocationException e)
                {
                    return CallToolResult.Error("Native duplicant delivery outcome is unknown. Inspect the colony before continuing: "
                        + (e.InnerException ?? e).Message);
                }
                return JsonResult(new Dictionary<string, object> { ["claimed"] = true, ["candidate"] = selected });
            }
            return CallToolResult.Error("Candidate is stale or belongs to a different offer. Call list_immigrants again.");
        }
    }
}
