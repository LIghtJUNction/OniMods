namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static FacadeSelection ResolveFacade(BuildingDef def, string facade)
        {
            if (string.IsNullOrWhiteSpace(facade))
                return FacadeSelection.Default();

            var facadeId = facade.Trim();
            if (facadeId.Equals("default", System.StringComparison.OrdinalIgnoreCase) || facadeId == "DEFAULT_FACADE")
                return FacadeSelection.Default();

            if (def.AvailableFacades == null || !def.AvailableFacades.Contains(facadeId))
                return FacadeSelection.Invalid($"Facade '{facadeId}' is not available for {def.PrefabID}");

            var permit = Db.Get().Permits.TryGet(facadeId);
            if (permit == null)
                return FacadeSelection.Invalid($"Facade '{facadeId}' has no permit resource");
            if (!permit.IsUnlocked())
                return FacadeSelection.Invalid($"Facade '{facadeId}' is locked");
            return FacadeSelection.Custom(facadeId);
        }

        private struct FacadeSelection
        {
            public readonly bool Valid;
            public readonly string TryPlaceId;
            public readonly string ResponseId;
            public readonly string Error;

            private FacadeSelection(bool valid, string tryPlaceId, string responseId, string error)
            {
                Valid = valid;
                TryPlaceId = tryPlaceId;
                ResponseId = responseId;
                Error = error;
            }

            public static FacadeSelection Default() => new FacadeSelection(true, null, "DEFAULT_FACADE", null);
            public static FacadeSelection Custom(string facadeId) => new FacadeSelection(true, facadeId, facadeId, null);
            public static FacadeSelection Invalid(string error) => new FacadeSelection(false, null, null, error);
        }
    }
}
