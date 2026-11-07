using System;
using OniOnline.Network;

namespace OniOnline.Sdk
{
    // Last accepted revision wins. Any participant may pause. Speed changes require
    // both the shared-simulation and shared-clock contracts before Enabled is set.
    public sealed class SharedClockPolicy
    {
        public bool Enabled { get; set; }
        public bool Paused { get; private set; }
        public int Speed { get; private set; } = 1;
        public long Revision { get; private set; }
        public string ActorId { get; private set; }

        public static bool Allows(System.Collections.Generic.IEnumerable<string> negotiated)
        { return SessionCapabilities.UsesSharedClock(negotiated); }

        public bool TryPause(string playerId, bool paused, long revision)
        { return Apply(playerId, paused, Speed, revision, true); }

        public bool TrySpeed(string playerId, int speed, long revision)
        {
            if (speed < 1 || speed > 3) return false;
            return Apply(playerId, Paused, speed, revision, false);
        }

        private bool Apply(string playerId, bool paused, int speed, long revision, bool pause)
        {
            if (!Enabled || !PresenceCursor.ValidPlayer(playerId) || revision <= Revision) return false;
            Paused = pause ? paused : Paused;
            Speed = speed;
            Revision = revision;
            ActorId = playerId;
            return true;
        }

        public void Reset()
        {
            Paused = false;
            Speed = 1;
            Revision = 0;
            ActorId = null;
        }
    }
}
