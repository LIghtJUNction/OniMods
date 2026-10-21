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

    // This room instance only. Account bans survive fresh Hello/resume credentials;
    // LAN revocations can only recognize the credentials already seen in this room.
    internal sealed class RoomModeration
    {
        internal const int Limit = 256;
        private sealed class Revocation
        {
            internal string PlayerId;
            internal string SecretHash;
            internal string Account;
            internal bool Banned;
            internal RoomBan Entry;
        }
        private readonly List<Revocation> bans = new List<Revocation>();
        private readonly Queue<Revocation> kicks = new Queue<Revocation>();
        internal RoomBan[] Bans { get { return bans.Select(r => r.Entry).ToArray(); } }

        internal bool Revoke(string playerId, string name, string secret, string account, bool ban, string reason)
        {
            if (ban && bans.Count >= Limit) return false;
            var record = new Revocation { PlayerId = playerId, SecretHash = Hash(secret), Account = account,
                Banned = ban, Entry = new RoomBan(Guid.NewGuid().ToString("N"), name, reason, account != null) };
            if (ban) bans.Add(record);
            else { if (kicks.Count == Limit) kicks.Dequeue(); kicks.Enqueue(record); }
            return true;
        }

        internal string Rejection(string playerId, string initialSecret, string account)
        {
            string hash = string.IsNullOrEmpty(initialSecret) ? null : Hash(initialSecret);
            var record = bans.Concat(kicks).FirstOrDefault(r =>
                (!string.IsNullOrEmpty(playerId) && r.PlayerId == playerId) ||
                (hash != null && Protocol.EqualProof(r.SecretHash, hash)) ||
                (r.Banned && account != null && r.Account == account));
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
