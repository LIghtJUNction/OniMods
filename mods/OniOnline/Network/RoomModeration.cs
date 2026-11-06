using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OniOnline.Network
{
    public sealed class RoomBan
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Reason { get; private set; }
        public bool AccountVerified { get; private set; }

        internal RoomBan(string id, string name, string reason, bool verified)
        { Id = id; Name = name; Reason = reason; AccountVerified = verified; }
    }

    // Account hashes can be restored from the host colony. LAN revocations can
    // only recognize credentials already seen in this room and are never saved.
    internal sealed class RoomModeration
    {
        internal const int Limit = 256;
        private sealed class Revocation
        {
            internal string PlayerId;
            internal string SecretHash;
            internal string AccountHash;
            internal string Account; // Live transport identity only; never serialized.
            internal bool Banned;
            internal RoomBan Entry;
        }
        private readonly List<Revocation> bans = new List<Revocation>();
        private readonly Queue<Revocation> kicks = new Queue<Revocation>();
        private string scope = Guid.NewGuid().ToString("N");
        internal RoomBan[] Bans { get { return bans.Select(r => r.Entry).ToArray(); } }

        internal void Restore(string homeSystemId, string payload, bool preserveCurrentAccounts = false)
        {
            var saved = RoomModerationPersistence.Decode(homeSystemId, payload);
            var restored = bans.Where(r => r.AccountHash == null).ToList();
            foreach (var record in saved)
                restored.Add(new Revocation { Banned = true, AccountHash = record.AccountHash,
                    Entry = new RoomBan(record.Id, record.Name, record.Reason, true) });
            // A room can be opened before its first colony is generated. Keep bans
            // made in that lobby when the native save becomes available.
            if (preserveCurrentAccounts)
                foreach (var current in bans.Where(r => r.AccountHash != null))
                {
                    if (current.Account == null) throw new InvalidOperationException("moderation_already_active");
                    string hash = RoomModerationPersistence.AccountHash(homeSystemId, current.Account);
                    if (restored.Any(r => r.AccountHash == hash)) continue;
                    restored.Add(new Revocation { Banned = true, Account = current.Account, AccountHash = hash,
                        PlayerId = current.PlayerId, SecretHash = current.SecretHash, Entry = current.Entry });
                }
            if (restored.Count > Limit || restored.Select(r => r.Entry.Id).Distinct().Count() != restored.Count)
                throw new InvalidOperationException("moderation_state_invalid");
            scope = homeSystemId;
            bans.Clear(); bans.AddRange(restored);
        }

        internal RoomBan BanForAccount(string account)
        {
            string hash = RoomModerationPersistence.AccountHash(scope, account);
            return hash == null ? null : bans.FirstOrDefault(r => Protocol.EqualProof(r.AccountHash, hash))?.Entry;
        }

        internal string Capture()
        {
            return RoomModerationPersistence.Encode(scope, bans.Where(r => r.AccountHash != null)
                .Select(r => new SavedAccountBan { Id = r.Entry.Id, AccountHash = r.AccountHash,
                    Name = r.Entry.Name, Reason = r.Entry.Reason }).ToArray());
        }

        internal bool Revoke(string playerId, string name, string secret, string account, bool ban, string reason)
        {
            if (ban && bans.Count >= Limit) return false;
            var record = new Revocation { PlayerId = playerId, SecretHash = Hash(secret),
                Account = account, AccountHash = RoomModerationPersistence.AccountHash(scope, account),
                Banned = ban, Entry = new RoomBan(Guid.NewGuid().ToString("N"), name, reason, account != null) };
            if (ban) bans.Add(record);
            else { if (kicks.Count == Limit) kicks.Dequeue(); kicks.Enqueue(record); }
            return true;
        }

        internal string Rejection(string playerId, string initialSecret, string account)
        {
            string hash = string.IsNullOrEmpty(initialSecret) ? null : Hash(initialSecret);
            string accountHash = RoomModerationPersistence.AccountHash(scope, account);
            var record = bans.Concat(kicks).FirstOrDefault(r =>
                (!string.IsNullOrEmpty(playerId) && r.PlayerId == playerId) ||
                (hash != null && Protocol.EqualProof(r.SecretHash, hash)) ||
                (r.Banned && accountHash != null && Protocol.EqualProof(r.AccountHash, accountHash)));
            return record == null ? null : record.Banned ? "player_banned" : "player_kicked";
        }

        internal bool Unban(string id)
        {
            var record = bans.FirstOrDefault(r => r.Entry.Id == id);
            return record != null && bans.Remove(record);
        }

        private static string Hash(string secret)
        {
            using (var sha = new SHA256Managed())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret)));
        }
    }
}
