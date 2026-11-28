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

        private static readonly MethodInfo RejectImmigrantOffer = typeof(ImmigrantScreen).GetMethod(
            "OnRejectionConfirmed", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

        private sealed class ImmigrantOffer
        {
            internal ImmigrantScreen Screen;
            internal Telepad Pod;
            internal bool ResolutionAttempted;
            internal RejectionPreview Rejection;
            internal readonly Dictionary<CharacterContainer, ImmigrantCandidate> Candidates =
                new Dictionary<CharacterContainer, ImmigrantCandidate>();
        }

        private sealed class RejectionPreview
        {
            internal readonly string Id = Guid.NewGuid().ToString("N");
            private readonly ITelepadDeliverableContainer[] cards;
            private readonly object[] rewards;

            internal RejectionPreview(List<ITelepadDeliverableContainer> options)
            {
                cards = options.ToArray();
                rewards = cards.Select(RewardIdentity).ToArray();
            }

            internal bool Matches(List<ITelepadDeliverableContainer> options)
            {
                return cards.Length == options.Count && Enumerable.Range(0, cards.Length).All(index =>
                    ReferenceEquals(cards[index], options[index]) &&
                    ReferenceEquals(rewards[index], RewardIdentity(options[index])));
            }

            private static object RewardIdentity(ITelepadDeliverableContainer card)
                => card is CharacterContainer duplicant ? (object)duplicant.Stats : (card as CarePackageContainer)?.Info;
        }

        private sealed class ImmigrantCandidate
        {
            internal readonly string Id = Guid.NewGuid().ToString("N");
            internal readonly MinionStartingStats Stats;

            internal ImmigrantCandidate(MinionStartingStats stats) { Stats = stats; }

            internal Dictionary<string, object> Describe()
            {
                return new Dictionary<string, object>
                {
                    ["candidateId"] = Id,
                    ["name"] = Stats.Name,
                    ["personalityId"] = Stats.personality?.Id,
                    ["startingLevels"] = Stats.StartingLevels,
                    ["traits"] = Stats.Traits.Where(t => t != null).Select(t => t.Id).ToArray(),
                    ["aptitudes"] = Stats.skillAptitudes.ToDictionary(p => p.Key.Id, p => p.Value),
                    ["stressTrait"] = Stats.stressTrait?.Id,
                    ["joyTrait"] = Stats.joyTrait?.Id
                };
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

        private static ImmigrantOffer GetImmigrantOffer(
            List<ITelepadDeliverableContainer> containers, ImmigrantScreen screen, Telepad pod)
        {
            var offer = ImmigrantOffers.GetValue(containers, choices => new ImmigrantOffer());
            if (offer.Screen != screen || offer.Pod != pod)
            {
                offer.Candidates.Clear();
                offer.Rejection = null;
                offer.Screen = screen;
                offer.Pod = pod;
            }
            return offer;
        }

        private static CallToolResult ListPrintingImmigrants(Telepad telepad)
        {
            var candidates = new List<Dictionary<string, object>>();
            List<ITelepadDeliverableContainer> containers;
            if (HasImmigrantOffer(telepad, out containers))
            {
                var offer = GetImmigrantOffer(containers, ImmigrantScreen.instance, telepad);
                if (!offer.ResolutionAttempted)
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

        private static CallToolResult RejectPrintingOffer(JObject args, Telepad telepad)
        {
            if (args["dryRun"] != null && args["dryRun"].Type != JTokenType.Boolean)
                return CallToolResult.Error("dryRun must be a boolean.");
            bool preview = (bool?)args["dryRun"] == true;
            if (!preview && (args["confirm"]?.Type != JTokenType.Boolean || !(bool)args["confirm"]))
                return CallToolResult.Error("confirm=true required to decline a Printing Pod offer.");
            if (RejectImmigrantOffer == null)
                return CallToolResult.Error("The native Printing Pod rejection confirmation is unavailable.");

            var screen = ImmigrantScreen.instance;
            if (!HasImmigrantOffer(telepad, out var containers) || containers.Count == 0)
                return CallToolResult.Error("No materialized offer at this pod. Use open_immigrants when the pod is ready.");

            if (preview)
            {
                var current = GetImmigrantOffer(containers, screen, telepad);
                if (current.ResolutionAttempted)
                    return CallToolResult.Error("Resolution of this Printing Pod offer has already been attempted.");
                if (current.Rejection == null || !current.Rejection.Matches(containers))
                    current.Rejection = new RejectionPreview(containers);
                return JsonResult(new Dictionary<string, object>
                {
                    ["dryRun"] = true,
                    ["offerId"] = current.Rejection.Id,
                    ["optionCount"] = containers.Count
                });
            }

            if (args["offerId"]?.Type != JTokenType.String
                || !ImmigrantOffers.TryGetValue(containers, out var offer)
                || offer.Screen != screen || offer.Pod != telepad || offer.ResolutionAttempted
                || offer.Rejection == null || offer.Rejection.Id != (string)args["offerId"]
                || !offer.Rejection.Matches(containers))
                return CallToolResult.Error("Provide the current offerId from a reject_all dry run at this pod.");

            // The native callback consumes the offer. It may throw after
            // rejection, so the preview cannot authorize a second attempt.
            offer.ResolutionAttempted = true;
            try
            {
                RejectImmigrantOffer.Invoke(screen, null);
            }
            catch (TargetInvocationException e)
            {
                return CallToolResult.Error("Native offer rejection outcome is unknown. Inspect the pod before continuing: "
                    + (e.InnerException ?? e).Message);
            }
            return JsonResult(new Dictionary<string, object> { ["rejected"] = true, ["offerId"] = offer.Rejection.Id });
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
                || offer.ResolutionAttempted || offer.Pod != telepad || offer.Screen != screen)
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
                offer.ResolutionAttempted = true;
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
