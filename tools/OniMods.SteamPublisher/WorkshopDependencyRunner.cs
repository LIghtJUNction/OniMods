using System.Text.Json;
using Steamworks;

internal static class WorkshopDependencyRunner
{
    internal static int Run(string[] args)
    {
        var options = WorkshopDependencyOptions.Parse(args);
        FirstPublicationPlan.RequireTarget();
        if (FirstPublicationPlan.Name != "Jlib")
            throw new InvalidOperationException("Dependency mode requires the fixed Jlib first-publication target");
        var plan = FirstPublicationPlan.Load(options.PlanPath, options.PlanSha256);
        ulong child = FirstPublicationJournal.ReadPublicVerifiedId(FirstPublicationJournal.DefaultPath, plan.PlanSha256);
        var target = WorkshopDependencyTarget.Create(options.Parent, child, plan.Title, options.ParentId);
        SteamAppContext.Prepare(SteamAppRole.Consumer);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
            throw new InvalidOperationException("Steam Consumer dependency context could not initialize");
        DependencyWriteJournal? journal = null;
        try
        {
            SteamAppContext.VerifyActive(SteamAppRole.Consumer);
            SteamWorkshopPublisher.ValidateAccount();
            bool added = WorkshopDependency.Run(target, new SteamWorkshopDependencyApi(), options.Write,
                () => journal = DependencyWriteJournal.Begin(
                    Path.Combine(CandidateCreationJournal.DefaultDirectory(), "dependencies"), target, plan.PlanSha256),
                entry => {
                    journal?.Record(entry);
                    Console.WriteLine(JsonSerializer.Serialize(entry));
                });
            Console.WriteLine(JsonSerializer.Serialize(new { phase = "complete", target.ParentName,
                target.ParentId, target.ChildId, added, dependencyVerified = true, contentUpload = false,
                journal = journal?.Path }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { phase = "failed", target.ParentId,
                target.ChildId, dependencyVerified = false, retryWrite = false,
                error = error.Message, journal = journal?.Path }));
            throw;
        }
        finally { SteamAPI.Shutdown(); }
    }
}
