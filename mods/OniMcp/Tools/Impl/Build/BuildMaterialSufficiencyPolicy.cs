namespace OniMcp.Tools
{
    internal static class BuildMaterialSufficiencyPolicy
    {
        internal static float RequiredPrimaryMass(BuildingDef def)
        {
            return def == null || def.Mass == null || def.Mass.Length == 0 ? 0f : def.Mass[0];
        }

        internal static bool IsSatisfied(float selectedAvailableKg, float requiredKg)
        {
            if (float.IsNaN(requiredKg) || float.IsInfinity(requiredKg)
                || float.IsNaN(selectedAvailableKg) || float.IsInfinity(selectedAvailableKg))
                return false;

            if (requiredKg <= 0f)
                return true;

            return selectedAvailableKg >= 0f && selectedAvailableKg >= requiredKg;
        }
    }
}
