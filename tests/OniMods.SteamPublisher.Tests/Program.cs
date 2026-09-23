using System.Text.Json;
using System.Security.Cryptography;
using System.IO.Compression;

Environment.SetEnvironmentVariable("ONIM_PUBLISH_CREATOR_APP_ID", "636750");
Environment.SetEnvironmentVariable("ONIM_PUBLISH_CONSUMER_APP_ID", "457140");
Environment.SetEnvironmentVariable("ONIM_PUBLISH_EXPECTED_OWNER", "76561199137573787");
var oniPlanSha = RunPlanCase(
    "OniMcp", LegacyCandidatePlan.OriginalWorkshopId,
    "ONI MCP Server Test", LegacyCandidatePlan.CandidateTitle);
var cyclePlanSha = RunPlanCase(
    "CycleTrim", LegacyCandidatePlan.CycleTrimOriginalWorkshopId,
    "CycleTrim Test", LegacyCandidatePlan.CycleTrimCandidateTitle);
if (oniPlanSha == cyclePlanSha)
{
    throw new InvalidOperationException("Different source items shared a candidate plan SHA");
}

var directory = Path.Combine(Path.GetTempPath(),
    "onim-candidate-journal-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new HashSet<string>(StringComparer.Ordinal);
    foreach (var oldId in new[]
    {
        LegacyCandidatePlan.OriginalWorkshopId,
        LegacyCandidatePlan.CycleTrimOriginalWorkshopId,
    })
    {
        var first = CandidateCreationJournal.Begin(directory, oldId, new string('A', 64));
        if (!File.Exists(first.Path) || !paths.Add(first.Path))
        {
            throw new InvalidOperationException("Candidate journal is missing or shared");
        }
        var expectedPrefix = oldId == LegacyCandidatePlan.OriginalWorkshopId
            ? "onimcp-" : "cycletrim-";
        if (!Path.GetFileName(first.Path).StartsWith(
            expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Candidate journal filename changed");
        }
        try
        {
            CandidateCreationJournal.Begin(directory, oldId, new string('B', 64));
            throw new InvalidOperationException("A second candidate creation acquired the same journal");
        }
        catch (IOException)
        {
            // FileMode.CreateNew is the cross-process one-shot gate.
        }
        first.RecordUncertain("Steam callback timed out");
        try
        {
            CandidateCreationJournal.RequireNoPriorAttempt(directory, oldId);
            throw new InvalidOperationException("Uncertain callback did not block a retry");
        }
        catch (InvalidOperationException error) when (
            error.Message.Contains("already recorded", StringComparison.Ordinal))
        {
            // This item remains blocked, independently of the other item.
        }
        var lines = File.ReadAllLines(first.Path);
        if (lines.Length != 2)
        {
            throw new InvalidOperationException("Journal lost its intent or uncertain outcome");
        }
        using var intent = JsonDocument.Parse(lines[0]);
        using var uncertain = JsonDocument.Parse(lines[1]);
        if (intent.RootElement.GetProperty("event").GetString()
                != "create-intent-before-steam-call"
            || uncertain.RootElement.GetProperty("event").GetString()
                != "create-outcome-uncertain-no-retry")
        {
            throw new InvalidOperationException("Journal events are incomplete");
        }
    }
    Console.WriteLine("independent one-shot candidate journals passed");
}
finally
{
    if (Directory.Exists(directory))
    {
        Directory.Delete(directory, recursive: true);
    }
}

static string RunPlanCase(
    string name, ulong oldId, string sourceTitle, string expectedTitle)
{
    Environment.SetEnvironmentVariable("ONIM_PUBLISH_WORKSHOP_ID", oldId.ToString());
    Environment.SetEnvironmentVariable("ONIM_PUBLISH_NAME", name);
    var planDirectory = Path.Combine(Path.GetTempPath(),
        "onim-candidate-plan-" + Guid.NewGuid().ToString("N"));
    try
    {
        var content = Path.Combine(planDirectory, name);
        Directory.CreateDirectory(content);
        File.WriteAllBytes(Path.Combine(content, name + ".dll"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(content, "mod.yaml"), "title: Test\n");
        File.WriteAllText(Path.Combine(content, "mod_info.yaml"), "version: Test\n");
        var preview = Path.Combine(content, "preview.png");
        File.WriteAllBytes(preview, [0x89, 0x50, 0x4E, 0x47]);
        var metadata = new WorkshopMetadata(
            content, preview, sourceTitle,
            "English description", "中文说明", "Test");
        var firstPlan = LegacyCandidatePlan.Create(metadata);
        var repeatedPlan = LegacyCandidatePlan.Create(metadata);
        if (firstPlan.PlanSha256 != repeatedPlan.PlanSha256
            || !firstPlan.Package.Bytes.SequenceEqual(repeatedPlan.Package.Bytes))
        {
            throw new InvalidOperationException("Repeated offline plan changed its ZIP or SHA");
        }
        if (firstPlan.Target.OriginalWorkshopId != oldId
            || firstPlan.Title != expectedTitle
            || !firstPlan.Description.StartsWith(
                "PRIVATE LEGACY TEST CANDIDATE", StringComparison.Ordinal)
            || !firstPlan.PreviewBytes.SequenceEqual(File.ReadAllBytes(preview)))
        {
            throw new InvalidOperationException("Candidate target, title, notice, or preview changed");
        }
        using (var zip = ZipFile.OpenRead(firstPlan.Package.Path))
        {
            var entries = zip.Entries.Select(entry => entry.FullName).ToHashSet();
            if (!entries.Contains(name + ".dll")
                || !entries.Contains("mod.yaml")
                || !entries.Contains("mod_info.yaml"))
            {
                throw new InvalidOperationException("Legacy ZIP is missing ONI root files");
            }
        }
        var previousOutput = Console.Out;
        using var planOutput = new StringWriter();
        try
        {
            Console.SetOut(planOutput);
            firstPlan.Print();
        }
        finally
        {
            Console.SetOut(previousOutput);
        }
        var printed = planOutput.ToString();
        var zipSha = Convert.ToHexString(SHA256.HashData(firstPlan.Package.Bytes));
        var previewSha = Convert.ToHexString(SHA256.HashData(firstPlan.PreviewBytes));
        if (!printed.Contains("candidateVisibility=Private", StringComparison.Ordinal)
            || !printed.Contains("creatorApp=636750", StringComparison.Ordinal)
            || !printed.Contains("consumerApp=457140", StringComparison.Ordinal)
            || !printed.Contains($"sourceWorkshopId={oldId}", StringComparison.Ordinal)
            || !printed.Contains("legacyZipSha256=" + zipSha, StringComparison.Ordinal)
            || !printed.Contains("previewSha256=" + previewSha, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Offline plan output lost identity or exact hashes");
        }
        Console.WriteLine($"{name} private candidate plan and repeated SHA passed");
        return firstPlan.PlanSha256;
    }
    finally
    {
        if (Directory.Exists(planDirectory))
        {
            Directory.Delete(planDirectory, recursive: true);
        }
    }
}
