using UnityEngine;

namespace OniMcp.Tools
{
    internal static class ConduitEmptyingPolicy
    {
        internal static bool HasContents(GameObject target)
        {
            if (target == null || Game.Instance == null)
                return false;
            int cell = Grid.PosToCell(target);
            if (!Grid.IsValidCell(cell))
                return false;
            if (target.GetComponent<EmptySolidConduitWorkable>() != null)
                return Game.Instance.solidConduitFlow.GetContents(cell).pickupableHandle.IsValid();
            var conduit = target.GetComponent<Conduit>();
            if (conduit == null || target.GetComponent<EmptyConduitWorkable>() == null)
                return false;
            var flow = conduit.type == ConduitType.Gas
                ? Game.Instance.gasConduitFlow : Game.Instance.liquidConduitFlow;
            return flow.GetContents(cell).mass > 0f;
        }
    }
}
