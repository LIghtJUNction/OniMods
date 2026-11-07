using System;
using Jlib.Multicomponent;

namespace CycleTrim.Core
{
    /// <summary>
    /// Backward-compatibility facade delegating to <see cref="Jlib.Multicomponent.MulticomponentCellAccess"/>.
    /// New code should directly use Jlib.Multicomponent or JlibApi.
    /// </summary>
    public static class MulticomponentCellAccess
    {
        public static bool RegisterProvider(IMulticomponentCellProvider value)
        {
            return Jlib.Multicomponent.MulticomponentCellAccess.RegisterProvider(value);
        }

        public static bool UnregisterProvider(IMulticomponentCellProvider value)
        {
            return Jlib.Multicomponent.MulticomponentCellAccess.UnregisterProvider(value);
        }

        public static bool IsReady
        {
            get { return Jlib.Multicomponent.MulticomponentCellAccess.IsReady; }
        }

        public static bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot)
        {
            if (!CycleTrimPatchOptions.EnableMulticomponentShim)
            {
                snapshot = default(MulticomponentCellSnapshot);
                return false;
            }
            return Jlib.Multicomponent.MulticomponentCellAccess.TryGetCell(cell, out snapshot);
        }
    }
}
