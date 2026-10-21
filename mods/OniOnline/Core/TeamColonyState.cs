using System;
using System.Collections.Generic;
using System.Linq;

namespace OniOnline.Core
{
    [Flags]
    public enum TeamPermissionFlags
    {
        None = 0,
        ViewPlans = 1,
        IssueTasks = 2,
        CommandDuplicants = 4
    }

    public sealed class TeamMemberState
    {
        public string ActorId { get; private set; }
        public bool Administrator { get; private set; }
        public bool Online { get; private set; }
        public TeamPermissionFlags Permissions { get; private set; }

        internal TeamMemberState(string actorId, bool administrator, bool online, TeamPermissionFlags permissions)
        { ActorId = actorId; Administrator = administrator; Online = online; Permissions = permissions; }
    }

    /// <summary>A voluntary offer, not an inventory transaction or proof of delivery.</summary>
    public sealed class TeamContributionOffer
    {
        public string ActorId { get; private set; }
        public string RequestId { get; private set; }
        public string ResourceId { get; private set; }
        public decimal Amount { get; private set; }

        internal TeamContributionOffer(string actorId, string requestId, string resourceId, decimal amount)
        { ActorId = actorId; RequestId = requestId; ResourceId = resourceId; Amount = amount; }
    }

    /// <summary>
    /// Pure host-authoritative team policy for one planet. Callers must be authenticated by the
    /// transport; strings here are identities, never authentication credentials. No simulation,
    /// resource transfer, command merging, persistence, or authority migration is performed here.
    /// A loaded host simulation must separately prove availability and apply accepted operations.
    /// </summary>
    public sealed class TeamColonyState
    {
        private sealed class Member
        {
            internal bool Administrator;
            internal bool Online;
            internal TeamPermissionFlags Permissions;
        }

        private sealed class Duplicant
        {
            internal string OwnerId;
            internal bool DelegatedToTeam;
            internal readonly HashSet<string> Delegates = new HashSet<string>(StringComparer.Ordinal);
        }

        private const TeamPermissionFlags AllPermissions = TeamPermissionFlags.ViewPlans |
            TeamPermissionFlags.IssueTasks | TeamPermissionFlags.CommandDuplicants;
        private readonly Dictionary<string, Member> members = new Dictionary<string, Member>(StringComparer.Ordinal);
        private readonly HashSet<string> invitations = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Duplicant> duplicants = new Dictionary<string, Duplicant>(StringComparer.Ordinal);
        private readonly Dictionary<string, TeamContributionOffer> contributions =
            new Dictionary<string, TeamContributionOffer>(StringComparer.Ordinal);

        public string TeamId { get; private set; }
        public string PlanetId { get; private set; }
        public string AuthorityPeerId { get; private set; }
        public string AuthorityEpoch { get; private set; }
        public long Revision { get; private set; } = 1;
        public bool HostAvailable { get; private set; }
        public bool CanContinue { get { return HostAvailable && members.Values.Any(m => m.Online); } }

        public TeamColonyState(string teamId, string planetId, string authorityPeerId,
            string authorityEpoch, string founderId)
        {
            RequireId(teamId); RequireId(planetId); RequireId(authorityPeerId);
            RequireId(authorityEpoch); RequireId(founderId);
            TeamId = teamId; PlanetId = planetId; AuthorityPeerId = authorityPeerId;
            AuthorityEpoch = authorityEpoch;
            // Founding confers administration only; gameplay permissions remain explicit.
            members.Add(founderId, new Member { Administrator = true });
        }

        public TeamMemberState[] GetMembers()
        {
            return members.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
                new TeamMemberState(p.Key, p.Value.Administrator, p.Value.Online, p.Value.Permissions)).ToArray();
        }

        public bool IsInvited(string actorId) { return actorId != null && invitations.Contains(actorId); }

        public bool SetHostAvailable(string callerPeerId, string epoch, bool available)
        {
            if (!IsAuthority(callerPeerId, epoch)) return false;
            if (HostAvailable != available) { HostAvailable = available; Revision++; }
            return true;
        }

        public bool SetMemberOnline(string callerPeerId, string actorId, string epoch, bool online)
        {
            Member member;
            if (!IsAuthority(callerPeerId, epoch) || actorId == null || !members.TryGetValue(actorId, out member)) return false;
            if (member.Online != online) { member.Online = online; Revision++; }
            return true;
        }

        public bool Invite(string actorId, string targetId, string epoch)
        {
            if (!IsAdministrator(actorId, epoch) || !ValidId(targetId) || members.ContainsKey(targetId)) return false;
            if (invitations.Add(targetId)) Revision++;
            return true;
        }

        public bool CancelInvitation(string actorId, string targetId, string epoch)
        {
            if (!IsAdministrator(actorId, epoch) || targetId == null) return false;
            if (invitations.Remove(targetId)) Revision++;
            return true;
        }

        public bool AcceptInvitation(string actorId, string epoch)
        {
            if (!HostAvailable || epoch != AuthorityEpoch || actorId == null || !invitations.Remove(actorId)) return false;
            members.Add(actorId, new Member { Online = true });
            Revision++;
            return true;
        }

        public bool DeclineInvitation(string actorId, string epoch)
        {
            if (!HostAvailable || epoch != AuthorityEpoch || actorId == null || !invitations.Remove(actorId)) return false;
            Revision++;
            return true;
        }

        public bool SetPermissions(string actorId, string targetId, TeamPermissionFlags permissions, string epoch)
        {
            Member member;
            if (!IsAdministrator(actorId, epoch) || targetId == null ||
                (permissions & ~AllPermissions) != 0 || !members.TryGetValue(targetId, out member)) return false;
            if (member.Permissions != permissions) { member.Permissions = permissions; Revision++; }
            return true;
        }

        public bool SetAdministrator(string actorId, string targetId, bool administrator, string epoch)
        {
            Member member;
            if (!IsAdministrator(actorId, epoch) || targetId == null || !members.TryGetValue(targetId, out member) ||
                (!administrator && member.Administrator && AdministratorCount() == 1)) return false;
            if (member.Administrator != administrator) { member.Administrator = administrator; Revision++; }
            return true;
        }

        /// <summary>The successor explicitly accepted membership and must be online for handoff.</summary>
        public bool TransferAdministration(string actorId, string successorId, string epoch)
        {
            Member successor;
            if (!IsAdministrator(actorId, epoch) || successorId == actorId || successorId == null ||
                !members.TryGetValue(successorId, out successor) || !successor.Online) return false;
            successor.Administrator = true;
            members[actorId].Administrator = false;
            Revision++;
            return true;
        }

        public bool Leave(string actorId, string epoch)
        {
            if (!CanAct(actorId, epoch)) return false;
            return RemoveMember(actorId);
        }

        public bool RemoveMember(string actorId, string targetId, string epoch)
        {
            if (!IsAdministrator(actorId, epoch)) return false;
            return RemoveMember(targetId);
        }

        private bool RemoveMember(string actorId)
        {
            Member member;
            if (actorId == null || !members.TryGetValue(actorId, out member) ||
                (member.Administrator && AdministratorCount() == 1)) return false;
            members.Remove(actorId);
            // Membership never transfers ownership. Rejoining must not resurrect old consent.
            foreach (Duplicant duplicant in duplicants.Values)
            {
                duplicant.Delegates.Remove(actorId);
                if (duplicant.OwnerId == actorId)
                { duplicant.DelegatedToTeam = false; duplicant.Delegates.Clear(); }
            }
            Revision++;
            return true;
        }

        public TeamPermissionFlags GetEffectiveFlags(string actorId, bool hostAvailable)
        {
            Member member;
            return hostAvailable && CanContinue && actorId != null && members.TryGetValue(actorId, out member) && member.Online
                ? member.Permissions : TeamPermissionFlags.None;
        }

        public bool CanViewPlans(string actorId, bool hostAvailable)
        { return (GetEffectiveFlags(actorId, hostAvailable) & TeamPermissionFlags.ViewPlans) != 0; }

        public bool CanIssue(string actorId, bool hostAvailable)
        { return (GetEffectiveFlags(actorId, hostAvailable) & TeamPermissionFlags.IssueTasks) != 0; }

        /// <summary>Register only an already existing native dupe whose ownership the host verified.</summary>
        public bool RegisterDuplicant(string callerPeerId, string dupeId, string ownerId, string epoch)
        {
            if (!HostAvailable || !IsAuthority(callerPeerId, epoch) || !ValidId(dupeId) || !ValidId(ownerId)) return false;
            Duplicant existing;
            if (duplicants.TryGetValue(dupeId, out existing)) return existing.OwnerId == ownerId;
            duplicants.Add(dupeId, new Duplicant { OwnerId = ownerId });
            Revision++;
            return true;
        }

        // Only the native authority may refresh the current room projection of an already
        // verified immutable owner. Ownership itself is held in the save-level origin ledger.
        public bool RefreshVerifiedDuplicantOwner(string callerPeerId, string dupeId, string ownerPeer, string epoch)
        {
            Duplicant duplicant;
            if (!HostAvailable || !IsAuthority(callerPeerId, epoch) || !ValidId(ownerPeer) || dupeId == null ||
                !duplicants.TryGetValue(dupeId, out duplicant)) return false;
            if (duplicant.OwnerId == ownerPeer) return true;
            duplicant.OwnerId = ownerPeer;
            duplicant.DelegatedToTeam = false; duplicant.Delegates.Clear();
            Revision++;
            return true;
        }
        public string GetDuplicantOwner(string dupeId)
        {
            Duplicant duplicant;
            return dupeId != null && duplicants.TryGetValue(dupeId, out duplicant) ? duplicant.OwnerId : null;
        }

        public bool DelegateDuplicant(string actorId, string dupeId, string targetId, bool allow, string epoch)
        {
            Duplicant duplicant;
            if (!CanAct(actorId, epoch) || dupeId == null || !duplicants.TryGetValue(dupeId, out duplicant) ||
                duplicant.OwnerId != actorId || !ValidId(targetId) || !members.ContainsKey(targetId)) return false;
            bool changed = allow ? duplicant.Delegates.Add(targetId) : duplicant.Delegates.Remove(targetId);
            if (changed) Revision++;
            return true;
        }

        public bool DelegateDuplicantToTeam(string actorId, string dupeId, bool allow, string epoch)
        {
            Duplicant duplicant;
            if (!CanAct(actorId, epoch) || dupeId == null || !duplicants.TryGetValue(dupeId, out duplicant) ||
                duplicant.OwnerId != actorId) return false;
            if (duplicant.DelegatedToTeam != allow) { duplicant.DelegatedToTeam = allow; Revision++; }
            return true;
        }

        public bool CanControl(string actorId, string dupeId, bool hostAvailable)
        {
            Duplicant duplicant;
            if ((GetEffectiveFlags(actorId, hostAvailable) & TeamPermissionFlags.CommandDuplicants) == 0 ||
                dupeId == null || !duplicants.TryGetValue(dupeId, out duplicant)) return false;
            return duplicant.OwnerId == actorId || duplicant.DelegatedToTeam || duplicant.Delegates.Contains(actorId);
        }

        public bool OfferContribution(string actorId, string requestId, string resourceId, decimal amount, string epoch)
        {
            if (!CanAct(actorId, epoch) || !ValidId(requestId) || !ValidId(resourceId) || amount <= 0) return false;
            string key = actorId + "\0" + requestId;
            TeamContributionOffer existing;
            if (contributions.TryGetValue(key, out existing))
                return existing.ResourceId == resourceId && existing.Amount == amount;
            contributions.Add(key, new TeamContributionOffer(actorId, requestId, resourceId, amount));
            Revision++;
            return true;
        }

        public TeamContributionOffer[] GetContributionOffers()
        {
            return contributions.Values.OrderBy(c => c.ActorId, StringComparer.Ordinal)
                .ThenBy(c => c.RequestId, StringComparer.Ordinal).ToArray();
        }

        private bool CanAct(string actorId, string epoch)
        {
            Member member;
            return HostAvailable && epoch == AuthorityEpoch && actorId != null &&
                members.TryGetValue(actorId, out member) && member.Online;
        }

        private bool IsAdministrator(string actorId, string epoch)
        { return CanAct(actorId, epoch) && members[actorId].Administrator; }
        private bool IsAuthority(string callerPeerId, string epoch)
        { return callerPeerId == AuthorityPeerId && epoch == AuthorityEpoch; }
        private int AdministratorCount() { return members.Values.Count(m => m.Administrator); }
        private static bool ValidId(string id)
        { return !string.IsNullOrWhiteSpace(id) && id.Length <= 128 && !id.Any(char.IsControl); }
        private static void RequireId(string id)
        { if (!ValidId(id)) throw new ArgumentException("invalid_team_identity"); }
    }
}
