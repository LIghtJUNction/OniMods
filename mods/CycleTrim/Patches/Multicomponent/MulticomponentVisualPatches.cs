using System;
using Jlib.Multicomponent;
using UnityEngine;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Mass-weighted gas and liquid color blending facade delegating to <see cref="Jlib.Multicomponent.MulticomponentVisualPatch"/>.
    /// </summary>
    public static class MulticomponentVisualPatches
    {
        public static void ComputeBlendedGasColor(
            MulticomponentCellSnapshot snapshot,
            bool cellAboveValid,
            out byte r,
            out byte g,
            out byte b,
            out byte a)
        {
            MulticomponentVisualPatch.ComputeBlendedGasColor(snapshot, cellAboveValid, out r, out g, out b, out a);
        }
    }
}
