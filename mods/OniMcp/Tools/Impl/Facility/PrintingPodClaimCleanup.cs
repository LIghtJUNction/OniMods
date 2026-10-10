using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class PrintingPodClaimCleanup
    {
        internal static void Clear<T>(IList<T> containers, Action<T> destroy)
        {
            if (containers == null)
                throw new ArgumentNullException(nameof(containers));
            if (destroy == null)
                throw new ArgumentNullException(nameof(destroy));

            var snapshot = new List<T>(containers);
            foreach (var container in snapshot)
                destroy(container);
            containers.Clear();
        }
    }
}
