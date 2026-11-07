using System;
using System.Collections.Generic;
using System.Linq;

namespace OniOnline.Sdk
{
    public sealed class PresenceCursor
    {
        public string PlayerId { get; private set; }
        public int WorldId { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }

        public PresenceCursor(string playerId, int worldId, int x, int y)
        {
            if (!ValidPlayer(playerId)) throw new ArgumentException("invalid_presence_player");
            PlayerId = playerId;
            WorldId = worldId;
            X = x;
            Y = y;
        }

        internal static bool ValidPlayer(string playerId)
        { return !string.IsNullOrWhiteSpace(playerId) && playerId.Length <= 64 && playerId.IndexOfAny(new[] { '\r', '\n' }) < 0; }
    }

    public sealed class PresenceMark
    {
        public string PlayerId { get; private set; }
        public int WorldId { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public long Sequence { get; private set; }

        public PresenceMark(string playerId, int worldId, int x, int y, long sequence)
        {
            if (!PresenceCursor.ValidPlayer(playerId) || sequence <= 0) throw new ArgumentException("invalid_presence_mark");
            PlayerId = playerId;
            WorldId = worldId;
            X = x;
            Y = y;
            Sequence = sequence;
        }
    }

    // Latest cursor per player and a bounded mark list. Disabled until the session negotiates presence.
    public sealed class PresenceBoard
    {
        public const int MaximumPlayers = 16;
        public const int MaximumMarks = 32;
        private readonly Dictionary<string, PresenceCursor> cursors = new Dictionary<string, PresenceCursor>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> markSequence = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly List<PresenceMark> marks = new List<PresenceMark>();

        public bool Enabled { get; set; }
        public PresenceMark[] Marks { get { return marks.ToArray(); } }

        public bool TryCursor(PresenceCursor cursor)
        {
            if (!Enabled || cursor == null) return false;
            if (!cursors.ContainsKey(cursor.PlayerId) && cursors.Count >= MaximumPlayers) return false;
            cursors[cursor.PlayerId] = cursor;
            return true;
        }

        public PresenceCursor CursorFor(string playerId)
        {
            PresenceCursor cursor;
            return playerId != null && cursors.TryGetValue(playerId, out cursor) ? cursor : null;
        }

        public bool TryMark(PresenceMark mark)
        {
            if (!Enabled || mark == null) return false;
            long previous;
            if (markSequence.TryGetValue(mark.PlayerId, out previous) && mark.Sequence <= previous) return false;
            if (!markSequence.ContainsKey(mark.PlayerId) && markSequence.Count >= MaximumPlayers) return false;
            markSequence[mark.PlayerId] = mark.Sequence;
            marks.Add(mark);
            if (marks.Count > MaximumMarks) marks.RemoveAt(0);
            return true;
        }

        public void Drop(string playerId)
        {
            if (playerId == null) return;
            cursors.Remove(playerId);
            markSequence.Remove(playerId);
            marks.RemoveAll(mark => mark.PlayerId == playerId);
        }
    }
}
