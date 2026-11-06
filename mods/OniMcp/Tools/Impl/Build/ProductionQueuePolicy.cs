using System;

namespace OniMcp.Tools
{
    internal static class ProductionQueuePolicy
    {
        internal static bool TryCompute(ComplexRecipe recipe, int before, string mode,
            int requested, out int next, out string error)
        {
            error = null;
            switch (mode)
            {
                case "clear":
                    next = 0;
                    break;
                case "infinite":
                    next = ComplexFabricator.QUEUE_INFINITE;
                    break;
                case "add":
                    next = before == ComplexFabricator.QUEUE_INFINITE
                        ? ComplexFabricator.QUEUE_INFINITE
                        : Clamp((long)before + Math.Max(1, requested));
                    break;
                case "remove":
                    next = Clamp((long)(before == ComplexFabricator.QUEUE_INFINITE
                        ? ComplexFabricator.MAX_QUEUE_SIZE : before) - Math.Max(1, requested));
                    break;
                case "set":
                    next = Clamp(requested);
                    break;
                default:
                    next = before;
                    error = "mode must be set, add, remove, infinite or clear";
                    return false;
            }

            // Clearing or reducing an old queue must remain possible even when
            // the recipe's research or POI is no longer available.
            if (next != 0 && mode != "remove" && !recipe.IsRequiredTechOrPOIUnlocked())
            {
                error = "recipe_locked: required research or POI is not unlocked: " + recipe.id;
                return false;
            }
            return true;
        }

        private static int Clamp(long count)
        {
            return (int)Math.Max(0, Math.Min(ComplexFabricator.MAX_QUEUE_SIZE, count));
        }
    }
}
