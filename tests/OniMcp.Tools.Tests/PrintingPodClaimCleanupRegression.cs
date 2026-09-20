using System;
using System.Collections.Generic;
using OniMcp.Tools;

internal static class PrintingPodClaimCleanupRegression
{
    internal static void Run()
    {
        var containers = new List<string> { "care-package", "duplicant" };
        var destroyed = new List<string>();

        PrintingPodClaimCleanup.Clear(containers, container => destroyed.Add(container));

        if (containers.Count != 0)
            throw new InvalidOperationException("printing-pod cleanup must invalidate the materialized option set synchronously");
        if (destroyed.Count != 2)
            throw new InvalidOperationException("printing-pod cleanup must destroy every materialized option exactly once");
        if (destroyed[0] != "care-package" || destroyed[1] != "duplicant")
            throw new InvalidOperationException("printing-pod cleanup must preserve native option traversal order");
    }
}
