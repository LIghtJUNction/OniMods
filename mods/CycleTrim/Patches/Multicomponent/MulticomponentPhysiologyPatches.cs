using Jlib.Multicomponent;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Multicomponent breathing mass evaluation facade delegating to <see cref="Jlib.Multicomponent.MulticomponentBreathingPatch"/>.
    /// </summary>
    public static class MulticomponentBreathingPatch
    {
        public static void EvaluateBreathableCell(
            MulticomponentCellSnapshot snapshot,
            out SimHashes elementID,
            out float breathableMass)
        {
            Jlib.Multicomponent.MulticomponentBreathingPatch.EvaluateBreathableCell(snapshot, out elementID, out breathableMass);
        }
    }
}
