using System;
using System.Reflection;

namespace OniMcp.Tools
{
    internal static class PowerConnectionReadiness
    {
        private static readonly FieldInfo Dirty = typeof(CircuitManager).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic);

        // Generator and Battery getters consult the graph themselves. Do not
        // evaluate them before checking both graph objects are available.
        internal static ushort? ReadCircuitId(Func<ushort> read)
            => Game.Instance?.circuitManager == null || Game.Instance.electricalConduitSystem == null
                ? (ushort?)null : read();

        internal static bool Pending(ICircuitConnected connection, ushort circuitId)
        {
            var manager = Game.Instance?.circuitManager;
            return connection == null || manager == null || Dirty == null
                || (Game.Instance.electricalConduitSystem?.IsDirty ?? true)
                || (Dirty.GetValue(manager) is bool dirty && dirty)
                || manager.GetCircuitID(connection) != circuitId;
        }
    }
}
