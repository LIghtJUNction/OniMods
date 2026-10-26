using System.Diagnostics;
using Steamworks;

internal static class FirstPublicationRunner
{
    private static readonly string[] Modes = ["--prepare-first-item", "--create-first-item", "--publish-first-item", "--verify-first-item"];
    internal static bool IsMode(string[] args) => args.Any(value => Modes.Contains(value, StringComparer.Ordinal));

    internal static int Run(string[] args)
    {
        FirstPublicationPlan.RequireTarget();
        if (args.Count(value => Modes.Contains(value, StringComparer.Ordinal)) != 1)
            throw new ArgumentException("Select exactly one first-publication mode");
        var valueOptions = new[] { "--vdf", "--output", "--plan", "--expected-plan-sha256" };
        var flags = new[] { "--confirm-first-create-once", "--confirm-first-publication", "--first-public-readback" };
        for (int index = 0; index < args.Length; index++)
        {
            if (valueOptions.Contains(args[index], StringComparer.Ordinal))
            {
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Missing first-publication option value");
            }
            else if (!Modes.Contains(args[index], StringComparer.Ordinal) && !flags.Contains(args[index], StringComparer.Ordinal))
                throw new ArgumentException("First-publication mode cannot be combined with other publisher modes");
        }
        if (args.Contains("--prepare-first-item", StringComparer.Ordinal))
        {
            var plan = FirstPublicationPlan.Create(WorkshopMetadataReader.Read(Required(args, "--vdf")));
            plan.Save(Required(args, "--output"));
            Console.WriteLine($"firstPublicationPlan={Path.GetFullPath(Required(args, "--output"))}");
            Console.WriteLine($"planSha256={plan.PlanSha256}");
            Console.WriteLine($"title={plan.Title}; visibility=PrivateThenPublic; zipBytes={plan.ZipBytes}");
            Console.WriteLine($"creationJournal={FirstPublicationJournal.DefaultPath}");
            Console.WriteLine("firstPublicationPreparedOffline=true; Steam API not initialized");
            return 0;
        }
        var planPath = Path.GetFullPath(Required(args, "--plan"));
        var expectedHash = Required(args, "--expected-plan-sha256");
        var reviewed = FirstPublicationPlan.Load(planPath, expectedHash);
        if (args.Contains("--verify-first-item", StringComparer.Ordinal))
            return Verify(reviewed, args.Contains("--first-public-readback", StringComparer.Ordinal));
        bool create = args.Contains("--create-first-item", StringComparer.Ordinal);
        var confirmation = create ? "--confirm-first-create-once" : "--confirm-first-publication";
        if (!args.Contains(confirmation, StringComparer.Ordinal))
            throw new ArgumentException($"First-publication write requires {confirmation}");
        if (create && File.Exists(FirstPublicationJournal.DefaultPath))
            throw new InvalidOperationException($"{FirstPublicationPlan.Name} first creation was already attempted; inspect its journal and owned items, never automatically retry create");
        // DownloadItem belongs to a short-lived child. Steam shutdown can hang
        // after downloads; the process that performs writes must not download.
        if (!create) RunConsumerVerifier(planPath, reviewed.PlanSha256, publicItem: false);
        Initialize(create ? SteamAppRole.Creator : SteamAppRole.Consumer);
        FirstPublicationJournal? journal = null;
        ulong id;
        try
        {
            if (create)
                (id, journal) = SteamWorkshopPublisher.PublishFirstPrivate(reviewed);
            else
            {
                id = FirstPublicationJournal.ReadCreatedId(FirstPublicationJournal.DefaultPath, reviewed.PlanSha256);
                SteamWorkshopPublisher.RequireFirstCreatedPage(reviewed, id);
                var stage = FirstPublicationJournal.BeginPublic(FirstPublicationJournal.DefaultPath, reviewed.PlanSha256);
                journal = stage.Journal;
                SteamWorkshopPublisher.PublishFirstPublic(reviewed, id);
            }
        }
        catch (Exception error)
        {
            journal?.RecordUncertain(error.Message);
            throw;
        }
        finally { SteamAPI.Shutdown(); }
        try
        {
            RunConsumerVerifier(planPath, reviewed.PlanSha256, publicItem: !create);
            if (!create)
            {
                SteamPublicPageReadback.WaitForFirstPublication(reviewed, id);
                journal!.RecordPublicVerified();
            }
        }
        catch (Exception error)
        {
            journal?.RecordUncertain(error.Message);
            throw;
        }
        Console.WriteLine($"firstWorkshopId={id}; visibility={(create ? "Private" : "Public")}");
        Console.WriteLine($"https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
        return 0;
    }

    private static int Verify(FirstPublicationPlan plan, bool publicItem)
    {
        var id = FirstPublicationJournal.ReadCreatedId(FirstPublicationJournal.DefaultPath, plan.PlanSha256);
        Initialize(SteamAppRole.Consumer);
        var details = SteamWorkshopPublisher.QueryItem(id, plan.Title, requirePrivate: !publicItem, requirePublic: publicItem);
        SteamWorkshopPublisher.VerifyInstalledLegacy(plan.ReadPackage(), details,
            previousServerUpdated: 0, candidateId: id, expectedCandidateTitle: plan.Title, expectPublic: publicItem);
        if (!publicItem) FirstPublicationJournal.RecordPrivateVerified(FirstPublicationJournal.DefaultPath, plan.PlanSha256);
        Console.WriteLine($"firstItemConsumerVerified={id}; public={publicItem}");
        // Dedicated verifier process: avoid Steam shutdown hanging after DownloadItem.
        return 0;
    }

    private static void Initialize(SteamAppRole role)
    {
        SteamAppContext.Prepare(role);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
            throw new InvalidOperationException("First-publication Steam context could not initialize");
        SteamAppContext.VerifyActive(role);
        SteamWorkshopPublisher.ValidateAccount();
    }

    private static void RunConsumerVerifier(string path, string hash, bool publicItem)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = SteamAppContext.HintDirectory(SteamAppRole.Consumer) };
        start.Environment["SteamAppId"] = "457140";
        start.Environment["SteamGameId"] = "457140";
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--verify-first-item");
        start.ArgumentList.Add("--plan"); start.ArgumentList.Add(path);
        start.ArgumentList.Add("--expected-plan-sha256"); start.ArgumentList.Add(hash);
        if (publicItem) start.ArgumentList.Add("--first-public-readback");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start first-item Consumer verifier");
        if (!child.WaitForExit(TimeSpan.FromMinutes(7)))
        {
            child.Kill(entireProcessTree: true);
            throw new TimeoutException("First-item Consumer verification timed out; do not retry create");
        }
        if (child.ExitCode != 0) throw new InvalidOperationException("First-item Consumer verification failed; do not retry create");
    }

    private static string Required(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"First-publication mode requires {option}");
        return args[index + 1];
    }
}
