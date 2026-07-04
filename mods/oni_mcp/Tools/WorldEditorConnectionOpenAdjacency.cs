using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string OpenAdjacentConnectionText(int cell, ObjectLayer[] layers, List<ConnectionNeighbor> dirs)
        {
            var open = new List<ConnectionNeighbor>();
            AddOpenAdjacentIf(open, cell, "U", 0, 1, layers, dirs);
            AddOpenAdjacentIf(open, cell, "D", 0, -1, layers, dirs);
            AddOpenAdjacentIf(open, cell, "L", -1, 0, layers, dirs);
            AddOpenAdjacentIf(open, cell, "R", 1, 0, layers, dirs);
            return open.Count == 0 ? "." : ConnectionLinkText(open);
        }

        private static void AddOpenAdjacentIf(
            List<ConnectionNeighbor> open,
            int cell,
            string dir,
            int dx,
            int dy,
            ObjectLayer[] layers,
            List<ConnectionNeighbor> linked)
        {
            if (linked.Any(d => d.Dir == dir))
                return;

            int neighbor = NeighborCell(cell, dx, dy);
            if (Grid.IsValidCell(neighbor) && HasLayer(neighbor, layers))
                open.Add(new ConnectionNeighbor(dir, neighbor));
        }
    }
}
