namespace OniMcp.Tools
{
    // Keep the starting native instance: ONI advances active research within the same update
    // that purchases a technology, so polling the active slot can skip its empty transition.
    internal sealed class StepResearchTracker
    {
        private readonly TechInstance _initialResearch;

        internal StepResearchTracker(TechInstance initialResearch)
        {
            _initialResearch = initialResearch;
            InitiallyComplete = initialResearch?.IsComplete() ?? false;
        }

        internal string InitialTechId => _initialResearch?.tech?.Id;
        internal bool InitiallyComplete { get; }
        internal bool HasCompleted => _initialResearch != null && !InitiallyComplete && _initialResearch.IsComplete();
    }
}
