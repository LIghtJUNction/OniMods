using System.Reflection;

namespace OniMcp.Tools
{
    internal static class PowerConnectionReadiness
    {
        private static readonly FieldInfo Dirty = typeof(CircuitManager).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic);

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
