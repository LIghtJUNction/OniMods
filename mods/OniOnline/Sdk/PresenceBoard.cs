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

    public enum ToolDragMode
    {
        Box = 0,
        Line = 1,
        Point = 2
    }

    public sealed class ToolDragPresence
    {
        public string PlayerId { get; private set; }
        public int WorldId { get; private set; }
        public string ToolId { get; private set; }
        public ToolDragMode Mode { get; private set; }
        public int StartX { get; private set; }
        public int StartY { get; private set; }
        public int EndX { get; private set; }
        public int EndY { get; private set; }
        public int Priority { get; private set; }
        public string MaterialTag { get; private set; }
        public uint ColorRgba { get; private set; }

        public ToolDragPresence(
            string playerId, int worldId, string toolId, ToolDragMode mode,
            int startX, int startY, int endX, int endY, int priority = 5,
            string materialTag = "", uint colorRgba = 0xFFFFFFFF)
        {
            if (!PresenceCursor.ValidPlayer(playerId)) throw new ArgumentException("invalid_presence_player");
            if (string.IsNullOrEmpty(toolId) || toolId.Length > 32) throw new ArgumentException("invalid_tool_id");
            if (priority < 1 || priority > 9) throw new ArgumentOutOfRangeException(nameof(priority), "priority must be 1..9");

            PlayerId = playerId;
            WorldId = worldId;
            ToolId = toolId;
            Mode = mode;
            StartX = startX;
            StartY = startY;
            EndX = endX;
            EndY = endY;
            Priority = priority;
            MaterialTag = materialTag ?? string.Empty;
            ColorRgba = colorRgba;
        }
    }

    public sealed class PlayerPing
    {
        public string PlayerId { get; private set; }
        public int WorldId { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public string Category { get; private set; }
        public double Timestamp { get; private set; }

        public PlayerPing(string playerId, int worldId, int x, int y, string category = "look", double timestamp = 0)
        {
            if (!PresenceCursor.ValidPlayer(playerId)) throw new ArgumentException("invalid_presence_player");
            if (category == null || category.Length > 32) throw new ArgumentException("invalid_ping_category");

            PlayerId = playerId;
            WorldId = worldId;
            X = x;
            Y = y;
            Category = category;
            Timestamp = timestamp;
        }
    }

    // Latest cursor per player and a bounded mark list. Disabled until the session negotiates presence.
    public sealed class PresenceBoard
    {
        public const int MaximumPlayers = 16;
        public const int MaximumMarks = 32;
        public const int MaximumPings = 32;

        private readonly Dictionary<string, PresenceCursor> cursors = new Dictionary<string, PresenceCursor>(StringComparer.Ordinal);
        private readonly Dictionary<string, ToolDragPresence> toolDrags = new Dictionary<string, ToolDragPresence>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> markSequence = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly List<PresenceMark> marks = new List<PresenceMark>();
        private readonly List<PlayerPing> pings = new List<PlayerPing>();

        public bool Enabled { get; set; }
        public PresenceMark[] Marks { get { return marks.ToArray(); } }
        public PlayerPing[] Pings { get { return pings.ToArray(); } }

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

        public bool TryToolDrag(ToolDragPresence drag)
        {
            if (!Enabled || drag == null) return false;
            if (!toolDrags.ContainsKey(drag.PlayerId) && toolDrags.Count >= MaximumPlayers) return false;
            toolDrags[drag.PlayerId] = drag;
            return true;
        }

        public ToolDragPresence ToolDragFor(string playerId)
        {
            ToolDragPresence drag;
            return playerId != null && toolDrags.TryGetValue(playerId, out drag) ? drag : null;
        }

        public void ClearToolDrag(string playerId)
        {
            if (playerId != null) toolDrags.Remove(playerId);
        }

        public bool TryPing(PlayerPing ping)
        {
            if (!Enabled || ping == null) return false;
            pings.Add(ping);
            if (pings.Count > MaximumPings) pings.RemoveAt(0);
            return true;
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
            toolDrags.Remove(playerId);
            markSequence.Remove(playerId);
            marks.RemoveAll(mark => mark.PlayerId == playerId);
            pings.RemoveAll(ping => ping.PlayerId == playerId);
        }
    }
}
