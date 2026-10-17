internal static class WorkshopPromotionRecoveryReadback
{
    internal static void RequireMatchingPages(
        WorkshopPromotionPlan plan, WorkshopPromotionSnapshot current)
    {
        var old = current.Original;
        var baseline = plan.Baseline.Original;
        if (!MatchesIdentity(old, plan.OriginalId)
            || old.Title != baseline.Title
            || old.TitleChinese != baseline.TitleChinese
            || old.DescriptionEnglish != baseline.DescriptionEnglish
            || old.DescriptionChinese != baseline.DescriptionChinese
            || !old.Tags.SequenceEqual(baseline.Tags)
            || old.Visibility != baseline.Visibility
            || old.Visibility != "Public"
            || old.FileSize != baseline.FileSize
            || old.UpdatedAt != baseline.UpdatedAt)
        {
            throw new InvalidOperationException(
                "Old Workshop page differs from the reviewed recovery baseline");
        }

        var candidate = current.Candidate;
        if (!MatchesIdentity(candidate, plan.NewId)
            || candidate.Title != plan.NewTitle
            || candidate.TitleChinese != plan.NewTitleChinese
            || candidate.DescriptionEnglish != plan.NewDescriptionEnglish
            || candidate.DescriptionChinese != plan.NewDescriptionChinese
            || candidate.Tags.Length != plan.NewTags.Length
            || !candidate.Tags.ToHashSet(StringComparer.Ordinal)
                .SetEquals(plan.NewTags)
            || candidate.Visibility != "Private"
            || candidate.FileSize != plan.ZipBytes)
        {
            throw new InvalidOperationException(
                "Private Workshop page does not match the reviewed formal copy and ZIP size");
        }
    }

    private static bool MatchesIdentity(WorkshopPageSnapshot page, ulong id) =>
        page.Id == id
        && page.CreatorAppId == WorkshopTarget.CreatorAppId
        && page.ConsumerAppId == WorkshopTarget.ConsumerAppId
        && page.OwnerId == WorkshopTarget.ExpectedOwner;
}
