using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class SandboxFloodFillExecution
    {
        internal static void VisitPreviewCells(IReadOnlyList<int> cells, Action<int> visit)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (visit == null)
                throw new ArgumentNullException(nameof(visit));

            for (int i = 0; i < cells.Count; i++)
                visit(cells[i]);
        }
    }
}
