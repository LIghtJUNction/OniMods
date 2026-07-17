namespace CycleTrim.Core
{
    public static class DirtyInvalidationPolicy
    {
        public static bool ShouldBumpCell(
            bool valid,
            bool suppressed,
            bool wasDirty,
            bool isDirty)
        {
            return valid && !suppressed && !wasDirty && isDirty;
        }

        public static bool ShouldBumpBatch(bool suppressed, bool hasValidCell)
        {
            return !suppressed && hasValidCell;
        }
    }
}
