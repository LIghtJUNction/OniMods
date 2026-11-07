using Jlib.Multicomponent;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Multicomponent hover summary formatting delegating to <see cref="Jlib.Multicomponent.MulticomponentHoverPatch"/>.
    /// </summary>
    public static class MulticomponentHoverPatches
    {
        public static string FormatCompositionSuffix(MulticomponentCellSnapshot snapshot)
        {
            return Jlib.Multicomponent.MulticomponentHoverPatch.FormatCompositionSuffix(snapshot);
        }
    }
}
