using UnityEngine;

namespace OniMcp.Tools
{
    internal static class TileDigPlacement
    {
        internal static string RejectionReason(int cell, int worldId)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return "invalid_or_wrong_world";
            if (!Grid.IsVisible(cell))
                return "not_visible";
            if (!Grid.Solid[cell])
                return "not_solid";
            if (Grid.Foundation[cell])
                return "foundation_or_constructed_tile";
            if (Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null)
                return null;
            for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
            {
                var target = Grid.Objects[cell, layer];
                if (target != null && target.GetComponent<Constructable>() != null)
                    return "construction_blueprint";
            }
            return null;
        }

        internal static GameObject Place(int cell, int worldId, int animationDelay = 0)
        {
            if (RejectionReason(cell, worldId) != null || DigTool.Instance == null)
                return null;
            var existing = Grid.Objects[cell, (int)ObjectLayer.DigPlacer];
            if (existing != null)
                return existing;
            var prefab = Assets.GetPrefab(new Tag("DigPlacer"));
            if (prefab == null)
                return null;
            var placer = Util.KInstantiate(prefab, null, null);
            if (placer == null)
                return null;
            // This tool designates natural tiles only; it must not inherit or
            // mutate the player's tile/backwall/uproot UI filter state.
            placer.GetComponent<Diggable>().digTypeFlags = 1;
            placer.SetActive(true);
            Grid.Objects[cell, (int)ObjectLayer.DigPlacer] = placer;
            var position = Grid.CellToPosCBC(cell, DigTool.Instance.visualizerLayer);
            position.z -= 0.15f;
            placer.transform.SetPosition(position);
            placer.GetComponentInChildren<EasingAnimations>().PlayAnimation(
                "ScaleUp", Mathf.Max(0f, animationDelay * 0.02f));
            return placer;
        }
    }
}
