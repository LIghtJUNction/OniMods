using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Network;

internal static class ModerationPersistenceTests
{
    private const string Scope = "d423f2cb305a4bfb930c9a8719d8570c";
    private static Action<bool, string> check;

    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        VerifiedBansSurviveRoomRestart();
        InvalidSavesDoNotPartiallyRestore();
        LiveAccountRevocationAndPersistence();
        FirstColonyAndReloadReplaceTheSaveBinding();
        SaveFailureStillRevokesAccess();
        Console.WriteLine("Moderation save: verified-account bans, bounded save decoding, fresh-room denial and immediate multi-seat revocation.");
    }

    private static void VerifiedBansSurviveRoomRestart()
    {
        var policy = new RoomModeration(); policy.Restore(Scope, null);
        string secret = Protocol.NewNonce();
        check(policy.Revoke("old-player", "Visitor", secret, "steam:verified-visitor", true, "Return later"), "verified account ban created");
        check(policy.Revoke("lan-player", "LAN", "lan-secret", null, true, "") &&
            policy.Revoke("kicked-player", "Kicked", "kick-secret", "steam:kicked", false, ""), "temporary LAN and kick decisions retained in current room");
        string payload = policy.Capture();
        var records = RoomModerationPersistence.Decode(Scope, payload);
        check(records.Length == 1 && records[0].Name == "Visitor" && records[0].Reason == "Return later", "save includes verified bans and their UI details only");
        string raw = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        check(!raw.Contains("steam:verified-visitor") && !raw.Contains(secret) && !raw.Contains("old-player"), "save excludes raw account, connection identity and resume secret");
        var reopened = new RoomModeration(); reopened.Restore(Scope, payload);
        check(reopened.Rejection("fresh-id", Protocol.NewNonce(), "steam:verified-visitor") == "player_banned", "new room denies verified account with fresh credentials");
        check(reopened.Rejection("lan-player", "lan-secret", null) == null &&
            reopened.Rejection("kicked-player", "kick-secret", "steam:kicked") == null, "LAN credentials and kicks do not become persistent identity");
        check(reopened.Rejection(null, null, "steam:another") == null, "other account remains admitted");
        check(reopened.Bans[0].Id == records[0].Id && reopened.Bans[0].AccountVerified, "saved ban keeps stable UI ID and verified scope");
        check(reopened.Unban(records[0].Id), "restored ban can be removed");
        var pardoned = new RoomModeration(); pardoned.Restore(Scope, reopened.Capture());
        check(pardoned.Bans.Length == 0 && pardoned.Rejection(null, null, "steam:verified-visitor") == null, "unban survives another room restart");
        check(RoomModerationPersistence.AccountHash(Scope, "steam:verified-visitor") !=
            RoomModerationPersistence.AccountHash("87d35c954e6b4c67a7bc6e2f9932f458", "steam:verified-visitor"), "account hashes are scoped to the colony's system");
    }

    private static void InvalidSavesDoNotPartiallyRestore()
    {
        var policy = new RoomModeration(); policy.Restore(Scope, null);
        policy.Revoke("id", "Name", Protocol.NewNonce(), "account", true, "");
        string valid = policy.Capture();
        byte[] corrupt = Convert.FromBase64String(valid); corrupt[30] ^= 1;
        string[] invalid = { "not-base64", valid + "\n", Convert.ToBase64String(corrupt),
            Convert.ToBase64String(new byte[RoomModerationPersistence.MaxBytes + 1]),
            RoomModerationPersistence.Encode("87d35c954e6b4c67a7bc6e2f9932f458", new SavedAccountBan[0]) };
        foreach (string payload in invalid)
        {
            var rejected = new RoomModeration();
            check(ThrowsInvalid(() => rejected.Restore(Scope, payload)) && rejected.Bans.Length == 0,
                "invalid or foreign moderation save cannot partially replace the ban list");
        }
        var record = RoomModerationPersistence.Decode(Scope, valid)[0];
        check(ThrowsInvalid(() => RoomModerationPersistence.Encode(Scope, new[] { record, record })), "duplicate ban IDs or account hashes are rejected");
        check(ThrowsInvalid(() => RoomModerationPersistence.Decode("", null)), "missing system identity cannot initialize persistent moderation");
    }

    private static void LiveAccountRevocationAndPersistence()
    {
        string saved = null; string banId;
        using (var room = new Room(payload => saved = payload))
        {
            var first = room.Join("steam:shared-account");
            var second = room.Join("steam:shared-account");
            var other = room.Join("steam:other-account");
            room.Until(() => first.Joined && second.Joined && other.Joined);
            check(room.Host.RemovePlayer(first.LocalPlayerId, true, "No entry"), "live account ban succeeds");
            check(room.Host.Players.Length == 2 && room.Host.Players.Any(p => p.Id == other.LocalPlayerId), "account ban immediately removes every admitted seat of that account");
            room.Until(() => first.Closed && second.Closed);
            check(first.LastError == "player_banned" && second.LastError == "player_banned", "all revoked connections receive the ban result");
            var records = RoomModerationPersistence.Decode(Scope, saved);
            check(records.Length == 1, "multi-seat ban saves one account decision");
            banId = records[0].Id;
            bool lateBindRejected = false;
            try { room.Host.BindModeration(Scope, null, payload => { }); }
            catch (InvalidOperationException) { lateBindRejected = true; }
            check(lateBindRejected, "active room cannot replace its moderation store");
        }
        using (var room = new Room(payload => saved = payload, saved))
        {
            var rejected = room.Join("steam:shared-account");
            room.Until(() => rejected.Closed);
            check(rejected.LastError == "player_banned" && room.Host.Players.Length == 1, "restored ban rejects new handshake before admission");
            check(room.Host.Unban(banId) && RoomModerationPersistence.Decode(Scope, saved).Length == 0, "unban updates the native-save payload");
            var admitted = room.Join("steam:shared-account"); room.Until(() => admitted.Joined);
            check(room.Host.Players.Length == 2, "pardoned account can create a fresh seat");
        }
    }

    private static void SaveFailureStillRevokesAccess()
    {
        using (var room = new Room(payload => { throw new InvalidOperationException("save unavailable"); }))
        {
            var client = room.Join("steam:account"); room.Until(() => client.Joined);
            check(room.Host.RemovePlayer(client.LocalPlayerId, true), "persistence failure does not undo an account ban");
            check(room.Host.Players.Length == 1 && room.Host.LastError == "moderation_save_failed", "persistence failure is visible while routing authority stays revoked");
            room.Until(() => client.Closed);
            check(client.LastError == "player_banned", "removed player still receives the ban when save fails");
        }
    }

    private static void FirstColonyAndReloadReplaceTheSaveBinding()
    {
        var savedPolicy = new RoomModeration(); savedPolicy.Restore(Scope, null);
        savedPolicy.Revoke("previous-id", "Saved visitor", Protocol.NewNonce(), "steam:saved-ban", true, "Saved decision");
        using (var room = new Room(null, bind: false))
        {
            var lobbyVisitor = room.Join("steam:lobby-ban");
            var savedVisitor = room.Join("steam:saved-ban");
            room.Until(() => lobbyVisitor.Joined && savedVisitor.Joined);
            check(room.Host.RemovePlayer(lobbyVisitor.LocalPlayerId, true), "lobby ban can precede native colony creation");
            string firstPayload = null; int firstWrites = 0;
            room.Host.LoadModeration(Scope, savedPolicy.Capture(), payload => { firstPayload = payload; firstWrites++; });
            check(RoomModerationPersistence.Decode(Scope, firstPayload).Length == 2, "first colony binding merges existing lobby bans with the loaded save");
            check(room.Host.Players.Length == 1, "first native load revokes already admitted saved-banned account before the next pump");
            room.Until(() => lobbyVisitor.Closed && savedVisitor.Closed);
            int previousWrites = firstWrites; string reloadedPayload = null;
            room.Host.LoadModeration(Scope, null, payload => reloadedPayload = payload);
            check(room.Host.Bans.Length == 0 && RoomModerationPersistence.Decode(Scope, reloadedPayload).Length == 0,
                "same-colony reload replaces account decisions with the loaded save's state");
            var newlyAdmitted = room.Join("steam:saved-ban"); room.Until(() => newlyAdmitted.Joined);
            check(room.Host.RemovePlayer(newlyAdmitted.LocalPlayerId, true) && firstWrites == previousWrites &&
                RoomModerationPersistence.Decode(Scope, reloadedPayload).Length == 1,
                "post-reload moderation writes to the new colony object and never calls the old save callback");
            string unchanged = reloadedPayload;
            check(ThrowsInvalid(() => room.Host.LoadModeration(Scope, "bad-data", payload => reloadedPayload = payload)) &&
                room.Host.Bans.Length == 1 && reloadedPayload == unchanged, "invalid reload leaves live decisions and save callback intact");
        }
    }

    private static bool ThrowsInvalid(Action action)
    { try { action(); return false; } catch (InvalidDataException) { return true; } }

    private sealed class Room : IDisposable
    {
        private readonly DeterministicRoomNetwork network = new DeterministicRoomNetwork(731);
        private readonly List<RoomSession> clients = new List<RoomSession>();
        internal readonly RoomSession Host;
        private static SessionProfile Profile()
        { return new SessionProfile { Name = "Player", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }; }
        internal Room(Action<string> save, string payload = null, bool bind = true)
        {
            Host = new RoomSession(network.Host, Profile(), "", "Saved system");
            if (bind) Host.BindModeration(Scope, payload, save);
        }
        internal RoomSession Join(string account)
        {
            var endpoint = (DeterministicRoomNetwork.Endpoint)network.Connect(); endpoint.AccountIdentity = account;
            var client = new RoomSession(endpoint, Profile(), ""); clients.Add(client); return client;
        }
        internal void Until(Func<bool> predicate)
        {
            double deadline = network.Now + 10;
            do
            {
                network.Advance(0.05); Host.Pump(network.Now);
                foreach (var client in clients) client.Pump(network.Now);
                if (predicate()) return;
            } while (network.Now < deadline);
            throw new Exception("Saved moderation session did not reach the expected state");
        }
        public void Dispose()
        { foreach (var client in clients) client.Dispose(); Host.Dispose(); network.Dispose(); }
    }
}
