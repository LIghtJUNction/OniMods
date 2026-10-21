// Host-only API shapes for invoking the linked production path-probe patches.
// Scheduling/cache decisions remain in production code; this does not run ONI.
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Threading;

internal sealed partial class Navigator
{
    internal PathFinderAbilities abilities = new CreaturePathFinderAbilities();
    internal PathGrid PathGrid { get; set; } = new PathGrid();
    internal bool reportOccupation = false;
    internal PathFinderAbilities GetCurrentAbilities() { return abilities; }
}

internal sealed class PathGrid
{
    internal ulong AllocatedClassification { get; set; } = 1;
}

internal class PathFinderAbilities
{
    internal int prefabInstanceID = 1;
    internal virtual PathFinderAbilities Clone() { return (PathFinderAbilities)MemberwiseClone(); }
}

internal sealed class CreaturePathFinderAbilities : PathFinderAbilities
{
    internal bool canTraverseSubmered = false;
    public void RecycleClone() { }
}

internal static class AsyncPathProber
{
    internal static Manager Instance;

    internal sealed class Manager
    {
        internal Thread[] agents = new Thread[1];
        internal Dictionary<Navigator, int> navigators = new Dictionary<Navigator, int>();
        internal ushort activeSerialNo = 1;
    }

    internal struct WorkOrder
    {
        internal Navigator navigator;
        internal NavGrid navGrid;
        internal ulong gridClassification;
        internal PathFinderAbilities abilities;
        internal int originCell, startingNavType, startingFlags;
        internal ushort serialNo;
        internal bool computeReachables;
    }
}

namespace HarmonyLib
{
    internal sealed class CodeInstruction
    {
        internal OpCode opcode;
        internal object operand;

        internal CodeInstruction(OpCode op, object value = null) { opcode = op; operand = value; }
        internal CodeInstruction(CodeInstruction instruction) : this(instruction.opcode, instruction.operand) { }
    }
}

namespace UnityEngine
{
    internal static class Debug
    {
        internal static void LogWarning(string message) { System.Console.Error.WriteLine(message); }
    }
}
