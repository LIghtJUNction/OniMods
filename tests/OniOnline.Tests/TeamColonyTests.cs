using System;
using OniOnline.Core;

internal static class TeamColonyTests
{
    private const string Epoch = "authority-epoch-1";
    private const string Host = "host";
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Eve = "eve";

    internal static void Run(Action<bool, string> check)
    {
        var team = new TeamColonyState("team-1", "planet-1", Host, Epoch, Alice);
        check(team.TeamId == "team-1" && team.PlanetId == "planet-1" && team.AuthorityPeerId == Host &&
            team.AuthorityEpoch == Epoch, "stable team, planet, computation authority and epoch are independent");
        check(!team.HostAvailable && !team.CanContinue, "team starts unavailable until native authority confirms host");
        check(!team.SetHostAvailable(Alice, Epoch, true), "team founder cannot impersonate computation authority");
        check(!team.SetHostAvailable(Host, "old-epoch", true), "old native authority epoch rejected");
        check(team.SetHostAvailable(Host, Epoch, true) && !team.CanContinue, "host alone does not impersonate online team member");
        check(!team.Invite(Alice, Bob, Epoch), "offline founder cannot invite");
        check(!team.SetMemberOnline(Bob, Alice, Epoch, true), "only authority sets online facts");
        check(!team.SetMemberOnline(Host, Alice, "old-epoch", true), "old session cannot set online facts");
        check(team.SetMemberOnline(Host, Alice, Epoch, true) && team.CanContinue, "one online member and live host enable team");
        check(team.GetEffectiveFlags(Alice, true) == TeamPermissionFlags.None && !team.CanIssue(Alice, true),
            "administrator starts with no implicit gameplay rights");
        check(!team.CanIssue(Host, true), "host computation is not automatic team command permission");
        check(!team.SetPermissions(Eve, Alice, TeamPermissionFlags.IssueTasks, Epoch), "outsider cannot assign permissions");
        check(!team.Invite(Eve, Bob, Epoch), "outsider cannot invite");
        check(team.Invite(Alice, Bob, Epoch) && team.IsInvited(Bob), "administrator creates pending invitation");
        check(team.GetMembers().Length == 1 && !team.CanIssue(Bob, true), "invitation alone grants no membership or rights");
        check(!team.AcceptInvitation(Eve, Epoch), "other actor cannot accept target invitation");
        check(!team.AcceptInvitation(Bob, "old-epoch"), "stale invitation acceptance rejected");
        check(team.AcceptInvitation(Bob, Epoch), "target explicitly accepts membership");
        check(!team.AcceptInvitation(Bob, Epoch), "replayed acceptance cannot duplicate membership");
        check(team.GetMembers().Length == 2 && team.GetEffectiveFlags(Bob, true) == TeamPermissionFlags.None,
            "accepted member initially has no gameplay rights");
        check(!team.Invite(Bob, Eve, Epoch), "ordinary member cannot invite");
        check(!team.SetPermissions(Bob, Bob, TeamPermissionFlags.IssueTasks, Epoch), "member cannot elevate own rights");
        check(!team.SetAdministrator(Bob, Bob, true, Epoch), "member cannot self-promote");
        check(!team.SetPermissions(Alice, Bob, (TeamPermissionFlags)8, Epoch), "unknown permission bits rejected");
        check(team.SetPermissions(Alice, Bob, TeamPermissionFlags.ViewPlans, Epoch), "administrator grants view only");
        check(team.CanViewPlans(Bob, true) && !team.CanIssue(Bob, true) && !team.CanControl(Bob, "dupe-1", true),
            "view, task publication and dupe control remain separate");
        check(team.SetPermissions(Alice, Bob, TeamPermissionFlags.IssueTasks | TeamPermissionFlags.CommandDuplicants, Epoch),
            "administrator can grant explicit task and dupe rights");
        check(team.CanIssue(Bob, true) && !team.CanViewPlans(Bob, true), "task rights do not implicitly reveal other plans");
        check(!team.CanIssue(Bob, false), "native host availability gate denies command even with stale cached online members");
        check(!team.RegisterDuplicant(Alice, "dupe-1", Alice, Epoch), "administrator cannot invent native ownership facts");
        check(team.RegisterDuplicant(Host, "dupe-1", Alice, Epoch), "authority registers existing dupe with independent personal owner");
        check(!team.RegisterDuplicant(Host, "dupe-1", Bob, Epoch) && team.GetDuplicantOwner("dupe-1") == Alice,
            "registration never reassigns existing dupe ownership");
        long before = team.Revision;
        check(team.RegisterDuplicant(Host, "dupe-1", Alice, Epoch) && team.Revision == before,
            "native ownership registration replay is idempotent");
        check(!team.CanControl(Bob, "dupe-1", true), "dupe permission alone is insufficient without owner consent");
        check(!team.DelegateDuplicant(Bob, "dupe-1", Bob, true, Epoch), "nonowner cannot consent to dupe delegation");
        check(team.DelegateDuplicant(Alice, "dupe-1", Bob, true, Epoch), "owner voluntarily delegates one existing dupe");
        check(team.CanControl(Bob, "dupe-1", true) && !team.CanControl(Bob, "dupe-2", true), "delegation is scoped to exact dupe");
        check(team.GetDuplicantOwner("dupe-1") == Alice, "shared control preserves original owner");
        check(team.DelegateDuplicant(Alice, "dupe-1", Bob, false, Epoch) && !team.CanControl(Bob, "dupe-1", true),
            "owner can revoke individual delegation immediately");
        check(team.DelegateDuplicantToTeam(Alice, "dupe-1", true, Epoch) && team.CanControl(Bob, "dupe-1", true),
            "owner can opt dupe into team control with per-member permissions");
        check(team.SetMemberOnline(Host, Alice, Epoch, false) && team.CanContinue && team.CanControl(Bob, "dupe-1", true),
            "offline dupe owner retains consent while another authorized team member is online");
        check(!team.SetPermissions(Alice, Bob, TeamPermissionFlags.None, Epoch), "offline administrator cannot mutate team");
        check(team.SetMemberOnline(Host, Bob, Epoch, false) && !team.CanContinue && !team.CanIssue(Bob, true),
            "no online team members means no active team commands");
        check(team.SetMemberOnline(Host, Bob, Epoch, true) && team.CanContinue, "any member can reactivate team with running native host");
        check(team.SetHostAvailable(Host, Epoch, false) && !team.CanContinue && !team.CanControl(Bob, "dupe-1", true),
            "host shutdown cannot be disguised by team members staying online");
        check(!team.AcceptInvitation(Eve, Epoch) && !team.OfferContribution(Bob, "offer-1", "oxygen", 10, Epoch),
            "host unavailable rejects team operations");
        check(team.SetHostAvailable(Host, Epoch, true) && team.CanControl(Bob, "dupe-1", true),
            "availability restoration preserves consent without transferring computation");
        check(team.OfferContribution(Bob, "offer-1", "oxygen", 10, Epoch), "member may voluntarily offer resource contribution");
        before = team.Revision;
        check(team.OfferContribution(Bob, "offer-1", "oxygen", 10, Epoch) && team.Revision == before &&
            team.GetContributionOffers().Length == 1, "offer retry cannot duplicate claimed contribution");
        check(!team.OfferContribution(Bob, "offer-1", "oxygen", 20, Epoch), "conflicting replay cannot change offered quantity");
        check(!team.OfferContribution(Bob, "offer-2", "oxygen", 0, Epoch) &&
            !team.OfferContribution(Bob, "offer-2", "oxygen", -1, Epoch), "invalid contribution quantity denied");
        check(!team.OfferContribution(Eve, "offer-2", "oxygen", 100, Epoch), "outsider cannot contribute in another member's name");
        check(team.GetContributionOffers()[0].ActorId == Bob && team.GetContributionOffers()[0].Amount == 10,
            "ledger records actor's own voluntary offer only; it supplies no inventory mutation");
        check(team.Leave(Bob, Epoch) && !team.CanIssue(Bob, true) && !team.CanControl(Bob, "dupe-1", true),
            "leaving immediately removes all effective team command rights");
        check(team.GetDuplicantOwner("dupe-1") == Alice && team.GetContributionOffers().Length == 1,
            "departure leaves personal ownership and historical contribution offers unchanged");
        check(team.SetMemberOnline(Host, Alice, Epoch, true), "founder returns without ownership changes");
        check(!team.Leave(Alice, Epoch) && !team.SetAdministrator(Alice, Alice, false, Epoch),
            "last administrator cannot abandon team without explicit handoff");
        check(team.Invite(Alice, Bob, Epoch) && team.AcceptInvitation(Bob, Epoch), "former member must accept a fresh invitation");
        check(!team.CanIssue(Bob, true) && !team.CanControl(Bob, "dupe-1", true), "rejoin does not restore old member permissions");
        check(team.SetPermissions(Alice, Bob, TeamPermissionFlags.CommandDuplicants, Epoch) &&
            team.CanControl(Bob, "dupe-1", true), "current team-wide delegation includes newly authorized members");
        check(team.SetMemberOnline(Host, Bob, Epoch, false) && !team.TransferAdministration(Alice, Bob, Epoch),
            "handoff requires online accepted successor");
        check(team.SetMemberOnline(Host, Bob, Epoch, true) && team.TransferAdministration(Alice, Bob, Epoch),
            "administrator explicitly hands management to successor");
        check(!team.Invite(Alice, Eve, Epoch) && team.Invite(Bob, Eve, Epoch), "handoff revokes former administrator role");
        check(team.Leave(Alice, Epoch) && team.GetDuplicantOwner("dupe-1") == Alice && !team.CanControl(Bob, "dupe-1", true),
            "owner departure revokes outstanding consent without gifting dupe to remaining manager");
        check(team.Invite(Bob, Alice, Epoch) && team.AcceptInvitation(Alice, Epoch), "owner may rejoin later");
        check(!team.CanControl(Bob, "dupe-1", true), "owner rejoin does not resurrect old dupe consent");
        check(team.DelegateDuplicant(Alice, "dupe-1", Bob, true, Epoch) && team.CanControl(Bob, "dupe-1", true),
            "returned owner must explicitly renew consent");
        check(team.RemoveMember(Bob, Alice, Epoch) && !team.CanControl(Bob, "dupe-1", true),
            "administrator removal also clears departed owner's delegation");
        check(!team.RemoveMember(Bob, Bob, Epoch), "last administrator cannot remove self");
        check(team.CancelInvitation(Bob, Eve, Epoch) && !team.AcceptInvitation(Eve, Epoch), "cancelled invitations cannot be accepted");
        check(team.Invite(Bob, Eve, Epoch) && team.DeclineInvitation(Eve, Epoch) && !team.IsInvited(Eve),
            "invited target can decline without joining");
        check(!team.Invite(Bob, "bad\nidentity", Epoch) && !team.OfferContribution(Bob, "bad\0id", "oxygen", 1, Epoch),
            "identity controls reject control characters and ambiguous ledger keys");
        check(team.SetPermissions(Bob, Bob, TeamPermissionFlags.CommandDuplicants, Epoch) &&
            team.RegisterDuplicant(Host, "dupe-2", Bob, Epoch) && team.CanControl(Bob, "dupe-2", true),
            "owner with explicit command permission controls own registered dupe");
        check(team.SetPermissions(Bob, Bob, TeamPermissionFlags.None, Epoch) && !team.CanControl(Bob, "dupe-2", true),
            "personal ownership and current team command permission remain separate");
        TeamMemberState[] snapshot = team.GetMembers();
        snapshot[0] = null;
        check(team.GetMembers()[0] != null, "member snapshot array cannot mutate authority membership");
        TeamContributionOffer[] offers = team.GetContributionOffers();
        offers[0] = null;
        check(team.GetContributionOffers()[0] != null, "offer snapshot array cannot mutate authoritative ledger");
        check(!team.SetPermissions(Bob, Bob, TeamPermissionFlags.IssueTasks, "old-epoch") &&
            !team.DelegateDuplicantToTeam(Bob, "dupe-2", true, "old-epoch"), "old authority epoch cannot revive command rights");
        bool invalid = false;
        try { new TeamColonyState("", "planet", Host, Epoch, Alice); }
        catch (ArgumentException) { invalid = true; }
        check(invalid, "invalid stable identity rejected before team creation");
    }
}
