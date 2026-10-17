using System.Diagnostics;
using System.Globalization;
using Steamworks;

internal static class WorkshopPromotionRecovery
{
    internal static int Run(string planPath, string expectedPlanSha256,
        SteamAppRole? childRole)
    {
        var plan = WorkshopPromotionPlan.Load(
            Path.GetFullPath(planPath), expectedPlanSha256);
        var target = LegacyCandidatePlan.ResolveFixedTarget();
        if (plan.OriginalId != target.OriginalWorkshopId)
        {
            throw new InvalidOperationException(
                "Recovery plan does not match the selected fixed Workshop item");
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
                "Recovery plan differs from the verified private creation journal");
        }

        var journalPath = PromotionStageJournal.DefaultPath(
            plan.OriginalId, plan.NewId);
        PromotionStageJournal.RequireRecoverablePrivateMetadata(
            journalPath, plan.PlanSha256);
        if (childRole is { } role)
        {
            return ReadBack(plan, role);
        }

        // Each Steam App ID gets its own short-lived process and matching
        // steam_appid.txt. None of these processes submits a Steam update.
        RunChild(SteamAppRole.Creator, planPath, expectedPlanSha256);
        RunChild(SteamAppRole.Consumer, planPath, expectedPlanSha256);
        RunChild(SteamAppRole.Creator, planPath, expectedPlanSha256);
        PromotionStageJournal.CompleteRecoveredPrivateMetadata(
            journalPath, plan.PlanSha256);
        Console.WriteLine($"privateMetadataRecoveredReadOnly=true journal={journalPath}");
        return 0;
    }

    private static int ReadBack(WorkshopPromotionPlan plan, SteamAppRole role)
    {
        SteamAppContext.Prepare(role);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
        {
            throw new InvalidOperationException(
                $"Steam {role} context could not initialize for recovery readback");
        }

        try
        {
            SteamAppContext.VerifyActive(role);
            SteamWorkshopPublisher.ValidateAccount();
            var snapshot = SteamWorkshopPublisher.CapturePromotionSnapshot(
                plan.OriginalId, plan.NewId);
            WorkshopPromotionRecoveryReadback.RequireMatchingPages(plan, snapshot);
            if (role == SteamAppRole.Consumer)
            {
                SteamWorkshopPublisher.RequireInstalledLegacyReadOnly(plan);
            }
            Console.WriteLine($"privateMetadataRecoveryReadback={role} "+
                $"oldId={plan.OriginalId} newId={plan.NewId}");
            return 0;
        }
        finally
        {
            // Recovery uses only page and install-info queries; it never
            // calls DownloadItem, whose callback can block shutdown.
            SteamAPI.Shutdown();
        }
    }

    private static void RunChild(
        SteamAppRole role, string planPath, string expectedPlanSha256)
    {
        SteamAppContext.ValidateHints();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = SteamAppContext.HintDirectory(role),
        };
        var appId = (role == SteamAppRole.Creator
            ? WorkshopTarget.CreatorAppId : WorkshopTarget.ConsumerAppId)
            .ToString(CultureInfo.InvariantCulture);
        start.Environment["SteamAppId"] = appId;
        start.Environment["SteamGameId"] = appId;
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--recover-private-metadata");
        start.ArgumentList.Add("--plan");
        start.ArgumentList.Add(Path.GetFullPath(planPath));
        start.ArgumentList.Add("--expected-plan-sha256");
        start.ArgumentList.Add(expectedPlanSha256);
        start.ArgumentList.Add("--confirm-readback-recovery");
        start.ArgumentList.Add("--recovery-role");
        start.ArgumentList.Add(role.ToString());
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException(
                $"Could not start Steam {role} recovery readback");
        if (!child.WaitForExit(TimeSpan.FromMinutes(4)))
        {
            child.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Steam {role} recovery readback timed out; journal remains pending");
        }
        if (child.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Steam {role} recovery readback failed: exit={child.ExitCode}; "
                + "journal remains pending");
        }
    }
}
