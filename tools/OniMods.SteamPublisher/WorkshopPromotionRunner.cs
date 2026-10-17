using Steamworks;

internal static class WorkshopPromotionRunner
{
    internal static int Run(
        PromotionStage stage, string planPath, string expectedPlanSha256)
    {
        var plan = WorkshopPromotionPlan.Load(
            Path.GetFullPath(planPath), expectedPlanSha256);
        var target = LegacyCandidatePlan.ResolveFixedTarget();
        if (plan.OriginalId != target.OriginalWorkshopId)
        {
            throw new InvalidOperationException(
                "Promotion plan does not match the selected fixed Workshop item");
        }
        var creation = VerifiedCandidateRecord.Load(
            CandidateCreationJournal.JournalPath(
                CandidateCreationJournal.DefaultDirectory(), plan.OriginalId),
            plan.OriginalId);
        if (creation.NewId != plan.NewId
            || !string.Equals(creation.CreationPlanSha256,
                plan.CreationPlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Promotion plan differs from the verified private creation journal");
        }
        var journalPath = PromotionStageJournal.DefaultPath(
            plan.OriginalId, plan.NewId);
        PromotionStageJournal? journal = null;
        try
        {
            SteamAppContext.Prepare(SteamAppRole.Consumer);
            if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
            {
                throw new InvalidOperationException(
                    "Steam consumer context could not initialize for promotion");
            }
            try
            {
                SteamAppContext.VerifyActive(SteamAppRole.Consumer);
                SteamWorkshopPublisher.ValidateAccount();
                RequirePrerequisites(plan, stage, journalPath);
                journal = PromotionStageJournal.Start(
                    journalPath, plan.PlanSha256, stage);
                switch (stage)
                {
                    case PromotionStage.PrivateMetadata:
                        SteamWorkshopPublisher.SubmitPrivateMetadata(plan);
                        SteamWorkshopPublisher.RequireOriginalBaseline(plan);
                        break;
                    case PromotionStage.PublishNew:
                        SteamWorkshopPublisher.SubmitPublicVisibility(plan);
                        SteamWorkshopPublisher.RequireOriginalBaseline(plan);
                        break;
                    case PromotionStage.LinkOld:
                        SteamWorkshopPublisher.SubmitOldMigrationCopy(plan);
                        SteamWorkshopPublisher.RequireFormalNewPage(
                            plan, requirePublic: true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(stage));
                }
            }
            finally
            {
                SteamAPI.Shutdown();
            }

            if (stage == PromotionStage.PrivateMetadata)
            {
                VerifyConsumerPackage(plan, expectPublic: false);
                journal.Complete(stage,
                    "Formal bilingual copy and tags read back while Private; Legacy ZIP SHA matched");
            }
            else if (stage == PromotionStage.PublishNew)
            {
                SteamPublicPageReadback.WaitForReviewedPage(plan, oldPage: false);
                VerifyConsumerPackage(plan, expectPublic: true);
                journal.Complete(stage, "Public Steam item and Legacy ZIP SHA matched");
                journal.CompletePublicWebReadback(
                    "Public Web API returned reviewed title, description, owner, Apps, and tags");
            }
            else
            {
                SteamPublicPageReadback.WaitForReviewedPage(plan, oldPage: true);
                journal.Complete(stage,
                    "Old public page links new ID with complete original body; content size unchanged");
            }
            Console.WriteLine($"promotionStageVerified={stage} journal={journalPath}");
            return 0;
        }
        catch (Exception error)
        {
            if (journal is not null)
            {
                try
                {
                    journal.Fail(stage, error.Message);
                }
                catch (Exception journalError)
                {
                    throw new AggregateException(
                        "Promotion failed and its journal could not record failure",
                        error, journalError);
                }
            }
            throw;
        }
    }

    private static void RequirePrerequisites(
        WorkshopPromotionPlan plan, PromotionStage stage, string journalPath)
    {
        switch (stage)
        {
            case PromotionStage.PrivateMetadata:
                SteamWorkshopPublisher.RequireOriginalBaseline(plan);
                SteamWorkshopPublisher.RequirePrivateCandidateBaseline(plan);
                break;
            case PromotionStage.PublishNew:
                PromotionStageJournal.RequireCompleted(
                    journalPath, plan.PlanSha256, PromotionStage.PrivateMetadata);
                SteamWorkshopPublisher.RequireOriginalBaseline(plan);
                SteamWorkshopPublisher.RequireFormalNewPage(plan, requirePublic: false);
                break;
            case PromotionStage.LinkOld:
                PromotionStageJournal.RequireCompleted(
                    journalPath, plan.PlanSha256, PromotionStage.PublishNew);
                SteamWorkshopPublisher.RequireOriginalBaseline(plan);
                SteamWorkshopPublisher.RequireFormalNewPage(plan, requirePublic: true);
                SteamPublicPageReadback.WaitForReviewedPage(plan, oldPage: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage));
        }
    }

    private static void VerifyConsumerPackage(
        WorkshopPromotionPlan plan, bool expectPublic)
    {
        var package = LegacyPackage.LoadForVerification(plan.ZipPath);
        Program.RunConsumerVerifier(
            package, previousUpdated: 0, candidateId: plan.NewId,
            candidateTitle: plan.NewTitle, candidatePublic: expectPublic);
    }
}
