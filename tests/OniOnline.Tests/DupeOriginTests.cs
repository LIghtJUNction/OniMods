using System;
using System.Globalization;
using OniOnline.Core;

internal static class DupeOriginTests
{
    private static string Id(int value) { return value.ToString("x32", CultureInfo.InvariantCulture); }
    internal static void Run(System.Action<bool, string> check)
    {
        string home = Id(10), planet = Id(20), ownerA = Id(1), ownerB = Id(2), error;
        var ledger = DupeOriginLedger.Load(0, null, home);
        check(ledger.LegacyPending && !ledger.Blocked, "only absent legacy sidecar is bootstrap eligible");
        var native = new NativeDupeOriginBinding { NativeInstanceId = 7, NativeWorldId = 0, CurrentPlanetId = planet,
            AdmissionKind = DupeAdmissionKind.LegacyLocal, AdmissionTransferId = "" };
        check(!ledger.TryBootstrapLegacyLocal(ownerA, new[] { native, native }, out error) && ledger.LegacyPending,
            "duplicate native IDs reject legacy bootstrap without partial adoption");
        check(ledger.TryBootstrapLegacyLocal(ownerA, new[] { native }, out error), "ordinary legacy local duplicant initializes once");
        check(!ledger.TryBootstrapLegacyLocal(ownerB, new[] { native }, out error), "bootstrap cannot later reassign original owner");
        DupeOriginRecord local;
        check(ledger.TryResolve(7, 0, planet, out local) && local.OriginalOwnerId == ownerA, "exact native binding resolves permanent owner");
        string stable = local.StableDupeId;
        local.OriginalOwnerId = ownerB;
        check(ledger.TryResolve(7, 0, planet, out local) && local.OriginalOwnerId == ownerA, "returned records cannot mutate authoritative ownership");
        string originalPayload = ledger.CapturePayload();
        var restored = DupeOriginLedger.Load(1, originalPayload, home);
        check(restored.TryResolve(7, 0, planet, out local) && local.StableDupeId == stable && local.OriginalOwnerId == ownerA,
            "permanent identity and owner survive codec save/load");
        var empty = DupeOriginLedger.Load(0, null, Id(11));
        check(empty.TryBootstrapLegacyLocal(ownerB, Array.Empty<NativeDupeOriginBinding>(), out error) &&
            !DupeOriginLedger.Load(1, empty.CapturePayload(), Id(11)).Blocked, "empty but valid initialized ledger survives save/load");
        foreach (var bad in new[] { DupeOriginLedger.Load(2, originalPayload, home), DupeOriginLedger.Load(1, null, home),
            DupeOriginLedger.Load(0, "", home), DupeOriginLedger.Load(1, "broken", home), DupeOriginLedger.Load(1, originalPayload, Id(999)) })
            check(bad.Blocked && !bad.LegacyPending && !bad.TryBootstrapLegacyLocal(ownerA, new[] { native }, out error),
                "unknown, missing, corrupt or mismatched origin metadata never becomes a host-owned empty ledger");
        var broken = DupeOriginLedger.Load(77, "broken", home);
        check(broken.SchemaToSave == 77 && broken.CapturePayload() == "broken", "failed reads preserve original schema and payload for the next native save");
        var importedOrigin = new DupeOriginRecord { StableDupeId = Id(30), OriginalOwnerId = ownerB, OriginPlanetId = Id(21),
            OriginHomeSystemId = Id(11), Kind = DupeOriginKind.NativeLocal };
        var imported = new NativeDupeOriginBinding { StableDupeId = importedOrigin.StableDupeId, NativeInstanceId = 8,
            NativeWorldId = 0, CurrentPlanetId = planet, AdmissionKind = DupeAdmissionKind.Imported, AdmissionTransferId = Id(40) };
        check(ledger.TryCommitVerifiedImports(new[] { new VerifiedDupeImport { Origin = importedOrigin, Binding = imported } }, out error),
            "verified foreign admission preserves immutable birth origin and separate import provenance");
        DupeOriginRecord visitor;
        check(ledger.TryResolve(8, 0, planet, out visitor) && visitor.SameAs(importedOrigin), "visitor is still owned by B while computed on A's planet");
        string before = ledger.CapturePayload();
        var changedOwner = importedOrigin.Copy(); changedOwner.OriginalOwnerId = ownerA;
        check(!ledger.TryCommitVerifiedImports(new[] { new VerifiedDupeImport { Origin = changedOwner, Binding = imported,
            ExpectedPreviousBinding = imported } }, out error) && ledger.CapturePayload() == before, "import cannot overwrite immutable original owner");
        foreach (string invalid in new string[] { null, "", "bad", new string('0', 32) })
        {
            var invalidOrigin = importedOrigin.Copy(); invalidOrigin.StableDupeId = invalid;
            var invalidBinding = imported.Copy(); invalidBinding.StableDupeId = invalid;
            check(!ledger.TryCommitVerifiedImports(new[] { new VerifiedDupeImport { Origin = invalidOrigin, Binding = invalidBinding } }, out error) &&
                ledger.CapturePayload() == before, "invalid import stable ID returns false without throwing or changing ledger");
        }
        var badLate = importedOrigin.Copy(); badLate.StableDupeId = Id(31);
        var collision = imported.Copy(); collision.StableDupeId = badLate.StableDupeId; collision.NativeInstanceId = 7;
        var goodFirst = importedOrigin.Copy(); goodFirst.StableDupeId = Id(32);
        var goodBinding = imported.Copy(); goodBinding.StableDupeId = goodFirst.StableDupeId; goodBinding.NativeInstanceId = 9;
        check(!ledger.TryCommitVerifiedImports(new[] { new VerifiedDupeImport { Origin = goodFirst, Binding = goodBinding },
            new VerifiedDupeImport { Origin = badLate, Binding = collision } }, out error) && ledger.CapturePayload() == before,
            "late conflict rejects complete import batch without committing earlier records");
        var rebound = imported.Copy(); rebound.NativeInstanceId = 18; rebound.NativeWorldId = 1; rebound.CurrentPlanetId = Id(22);
        check(ledger.TryRebindVerifiedNative(imported, rebound, out error) && ledger.TryResolve(18, 1, Id(22), out visitor) && visitor.SameAs(importedOrigin),
            "exact verified native rebind changes computation location but not stable ID, original owner, planet or home");
        before = ledger.CapturePayload();
        check(!ledger.TryRebindVerifiedNative(imported, rebound, out error) && ledger.CapturePayload() == before,
            "stale expected binding cannot rebind a second time");
        var strippedImport = rebound.Copy(); strippedImport.AdmissionKind = DupeAdmissionKind.LegacyLocal; strippedImport.AdmissionTransferId = "";
        check(!ledger.TryRebindVerifiedNative(rebound, strippedImport, out error) && ledger.CapturePayload() == before,
            "ordinary rebind cannot erase foreign admission provenance");
        check(!DupeOwnershipPolicy.CanControl(visitor, ownerA, ownerA, true, false),
            "host's local identity and planet-wide grants do not control B's visitor");
        check(DupeOwnershipPolicy.CanControl(visitor, ownerB, null, false, false), "original owner B can control its own resolved entity");
        check(DupeOwnershipPolicy.CanControl(visitor, ownerA, ownerB, true, false), "explicit personal grant is accepted only from that entity's original owner");
        check(DupeOwnershipPolicy.CanControl(visitor, ownerA, null, false, true), "explicit entity team delegation permits another registered owner");
        check(!DupeOwnershipPolicy.CanControl(visitor, null, null, false, true) && !DupeOwnershipPolicy.CanControl(null, ownerA, ownerA, true, true),
            "missing actor registration or missing origin never falls back to host");
        NativeBirths(check);
        Retention(check);
        RegisteredOwners(check);
        TeamOwnerProjection(check);
    }
    private static void NativeBirths(System.Action<bool, string> check)
    {
        string error; DupeOriginRecord origin, restored;
        var candidate = new NativeDupeOriginBinding { NativeInstanceId = 70, NativeWorldId = 0,
            CurrentPlanetId = Id(20), AdmissionKind = DupeAdmissionKind.NativeLocal };
        var pending = DupeOriginLedger.Load(0, null, Id(10));
        var blocked = DupeOriginLedger.Load(77, "preserve bytes", Id(10));
        check(!pending.TryRegisterVerifiedNativeBirth(Id(1), candidate, out origin, out error) && pending.LegacyPending &&
            pending.CapturePayload() == null, "birth cannot bootstrap an incomplete legacy load");
        check(!blocked.TryRegisterVerifiedNativeBirth(Id(1), candidate, out origin, out error) &&
            blocked.CapturePayload() == "preserve bytes", "birth cannot replace a blocked sidecar");
        var ledger = DupeOriginLedger.Load(0, null, Id(10));
        check(ledger.TryBootstrapLegacyLocal(Id(1), Array.Empty<NativeDupeOriginBinding>(), out error), "birth fixture initialized");
        check(ledger.TryRegisterVerifiedNativeBirth(Id(1), candidate, out origin, out error) && origin != null &&
            origin.Kind == DupeOriginKind.NativeLocal && origin.OriginalOwnerId == Id(1) && origin.OriginPlanetId == Id(20) &&
            origin.OriginHomeSystemId == Id(10), "verified birth commits the original owner, planet and home");
        string payload = ledger.CapturePayload(), stable = origin.StableDupeId;
        check(candidate.StableDupeId == null && ledger.CaptureDocument().Origins.Length == 1 &&
            ledger.CaptureDocument().Bindings.Length == 1, "birth adds one origin and one binding without modifying its candidate");
        check(!ledger.TryRegisterVerifiedNativeBirth(Id(2), candidate, out restored, out error) && restored == null &&
            ledger.CapturePayload() == payload, "duplicate native ID cannot create or reassign a second identity");
        var collision = candidate.Copy(); collision.NativeWorldId = 1; collision.CurrentPlanetId = Id(21);
        check(!ledger.TryRegisterVerifiedNativeBirth(Id(1), collision, out restored, out error) && ledger.CapturePayload() == payload,
            "native ID reuse is rejected even on a different planet");
        var imported = candidate.Copy(); imported.NativeInstanceId = 71; imported.AdmissionKind = DupeAdmissionKind.Imported;
        var transferred = candidate.Copy(); transferred.NativeInstanceId = 72; transferred.AdmissionTransferId = Id(40);
        var identified = candidate.Copy(); identified.NativeInstanceId = 73; identified.StableDupeId = Id(30);
        var invalidWorld = candidate.Copy(); invalidWorld.NativeInstanceId = 74; invalidWorld.NativeWorldId = 255;
        foreach (var invalid in new[] { imported, transferred, identified, invalidWorld })
            check(!ledger.TryRegisterVerifiedNativeBirth(Id(1), invalid, out restored, out error) && restored == null &&
                ledger.CapturePayload() == payload, "non-birth candidate is rejected without partial persistence");
        check(DupeOriginLedger.Load(1, payload, Id(10)).TryResolve(70, 0, Id(20), out restored) &&
            restored.SameAs(origin) && restored.StableDupeId == stable, "new birth survives the existing codec save/readback");
        origin.OriginalOwnerId = Id(2);
        check(ledger.CapturePayload() == payload, "returned birth record cannot mutate durable ownership");
        var binding = ledger.GetBinding(stable); var retired = binding.Copy(); retired.NativeWorldId = DupeOriginCodec.RetiredWorldId;
        check(ledger.TryRebindVerifiedNative(binding, retired, out error), "confirmed native retirement keeps an ID tombstone");
        ledger = DupeOriginLedger.Load(1, ledger.CapturePayload(), Id(10)); payload = ledger.CapturePayload();
        check(!ledger.TryResolve(70, DupeOriginCodec.RetiredWorldId, Id(20), out restored) &&
            !ledger.TryRegisterVerifiedNativeBirth(Id(1), candidate, out restored, out error) && ledger.CapturePayload() == payload,
            "retired native ID remains unresolvable and cannot be reused after save/readback");
        var revived = retired.Copy(); revived.NativeWorldId = 0;
        check(!ledger.TryRebindVerifiedNative(retired, revived, out error) && ledger.CapturePayload() == payload,
            "native rebind cannot revive a retired identity");
        check(!ledger.TryRemoveVerifiedBinding(retired, out error) && ledger.CapturePayload() == payload,
            "binding removal cannot release a retired native ID");
        revived.AdmissionKind = DupeAdmissionKind.Imported; revived.AdmissionTransferId = Id(40);
        check(!ledger.TryCommitVerifiedImports(new[] { new VerifiedDupeImport { Origin = ledger.CaptureDocument().Origins[0],
            Binding = revived, ExpectedPreviousBinding = retired } }, out error) && ledger.CapturePayload() == payload,
            "verified import cannot replace a retired binding even with matching immutable provenance");
    }
    private static void Retention(System.Action<bool, string> check)
    {
        check(!DupeOriginRetentionPolicy.MayRemoveBinding(false, false, false), "never-indexed native object is unknown, not dead; save preserves mapping");
        check(!DupeOriginRetentionPolicy.MayRemoveBinding(true, false, false), "unexplained position or missing native lookup cannot delete known mapping");
        check(!DupeOriginRetentionPolicy.MayRemoveBinding(true, true, true), "duplicate or reused live native ID prevents removal even after prior object destruction");
        check(!DupeOriginRetentionPolicy.MayRemoveBinding(false, true, false), "an unconfirmed object cannot supply death evidence");
        check(DupeOriginRetentionPolicy.MayRemoveBinding(true, true, false), "only a confirmed exact object destroyed with no matching live ID may remove its native binding");
    }
    private static void RegisteredOwners(System.Action<bool, string> check)
    {
        var owners = new RegisteredRoomOwners(); string value;
        var a = new RegisteredOwnerClaim { PeerId = Id(101), OwnerId = Id(1), Connected = true };
        var b = new RegisteredOwnerClaim { PeerId = Id(102), OwnerId = Id(2), Connected = true };
        owners.Update(new[] { a, b });
        check(owners.TryResolveActor(a.PeerId, out value) && value == Id(1), "host profile identity is an explicit registered owner, not whichever save is open");
        check(owners.TryResolveActor(b.PeerId, out value) && value == Id(2), "registered peer resolves its own persistent identity");
        b.Connected = false; owners.Update(new[] { a, b });
        check(!owners.TryResolveActor(b.PeerId, out value) && owners.ProjectionPeer(Id(2)) == b.PeerId,
            "offline owner cannot issue commands but keeps last owner projection for existing team consent");
        var b2 = new RegisteredOwnerClaim { PeerId = Id(103), OwnerId = Id(2), Connected = true };
        owners.Update(new[] { a, b, b2 });
        check(owners.TryResolveActor(b2.PeerId, out value) && value == Id(2), "same permanent owner may register a different room seat");
        b2.Connected = false; b.Connected = true; owners.Update(new[] { a, b, b2 });
        check(!owners.TryResolveActor(b.PeerId, out value) && owners.ProjectionPeer(Id(2)) == b2.PeerId,
            "superseded old seat cannot regain ownership authority when new seat goes offline");
        var conflict = new RegisteredRoomOwners(); b2.Connected = true;
        conflict.Update(new[] { b, b2 });
        check(!conflict.TryResolveActor(b.PeerId, out value) && !conflict.TryResolveActor(b2.PeerId, out value),
            "simultaneous duplicate owner claims are ambiguous and both denied");
        var changed = new RegisteredRoomOwners(); changed.Update(new[] { a });
        a.OwnerId = Id(2); changed.Update(new[] { a });
        check(!changed.TryResolveActor(a.PeerId, out value), "a registered room seat cannot change its persistent owner declaration");
    }
    private static void TeamOwnerProjection(System.Action<bool, string> check)
    {
        var team = new TeamColonyState("team", "planet", "host", "epoch", "host");
        team.SetHostAvailable("host", "epoch", true); team.SetMemberOnline("host", "host", "epoch", true);
        team.SetPermissions("host", "host", TeamPermissionFlags.CommandDuplicants, "epoch");
        team.Invite("host", "B-old", "epoch"); team.AcceptInvitation("B-old", "epoch");
        team.RegisterDuplicant("host", "stable-dupe", "B-old", "epoch");
        check(!team.CanControl("host", "stable-dupe", true), "host admin role does not itself grant control over B's entity");
        check(team.DelegateDuplicantToTeam("B-old", "stable-dupe", true, "epoch") && team.CanControl("host", "stable-dupe", true),
            "B explicitly delegates this entity to the team");
        team.SetMemberOnline("host", "B-old", "epoch", false);
        check(team.CanControl("host", "stable-dupe", true), "B temporarily offline does not withdraw earlier team entity delegation");
        check(!team.RefreshVerifiedDuplicantOwner("B-old", "stable-dupe", "B-new", "epoch"), "only native authority refreshes a verified owner-peer projection");
        check(team.RefreshVerifiedDuplicantOwner("host", "stable-dupe", "B-new", "epoch") && !team.CanControl("host", "stable-dupe", true),
            "verified new seat replaces owner projection and clears old explicit team delegation");
        check(!team.DelegateDuplicantToTeam("B-old", "stable-dupe", true, "epoch"), "superseded owner peer cannot regrant control");
        team.Invite("host", "B-new", "epoch"); team.AcceptInvitation("B-new", "epoch");
        check(team.DelegateDuplicantToTeam("B-new", "stable-dupe", true, "epoch") && team.CanControl("host", "stable-dupe", true),
            "same owner on a new registered seat explicitly renews delegation");
    }
}
