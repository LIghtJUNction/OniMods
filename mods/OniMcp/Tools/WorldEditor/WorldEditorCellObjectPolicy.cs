namespace OniMcp.Tools
{
    internal static class WorldEditorCellObjectPolicy
    {
        internal static T SelectBuildingCandidate<T>(T building, T logicGate, T gantry)
            where T : class
        {
            return building ?? logicGate ?? gantry;
        }
    }
}
