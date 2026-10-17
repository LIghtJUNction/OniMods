namespace OniMcp.Tools
{
    internal static class AutomatableControlPolicy
    {
        internal static string RejectionReason(Automatable target)
        {
            if (target == null)
                return "automatable_not_found";
            // AutomatableSideScreen accepts the vanilla component only. Derived
            // components may give the same flag a different gameplay meaning.
            return target.GetType() == typeof(Automatable)
                ? null : "custom_automatable_semantics";
        }
    }
}
