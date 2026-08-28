using System.Globalization;
using Steamworks;

internal static class Program
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary converts every publisher failure into a non-zero exit code.")]
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var queryOnly = args.Contains("--query-only", StringComparer.Ordinal);
        var validateOnly = args.Contains("--validate-vdf", StringComparer.Ordinal);
        var metadataOnly = args.Contains("--metadata-only", StringComparer.Ordinal);
        var updatePreview = args.Contains("--update-preview", StringComparer.Ordinal);
        var noChangeNote = args.Contains("--no-change-note", StringComparer.Ordinal);
        var vdfPath = ReadOption(args, "--vdf");
        if ((queryOnly && validateOnly)
            || (!queryOnly && string.IsNullOrWhiteSpace(vdfPath)))
        {
            throw new ArgumentException(
                "Usage: OniMods.SteamPublisher --query-only | --validate-vdf --vdf <path> | --metadata-only --vdf <path> | --vdf <path>");
        }

        var metadata = queryOnly
            ? null
            : WorkshopMetadataReader.Read(Path.GetFullPath(vdfPath));
        if (noChangeNote && metadata is not null)
        {
            metadata = metadata with { ChangeNote = string.Empty };
        }
        if (validateOnly)
        {
            Console.WriteLine($"title={metadata!.Title}");
            Console.WriteLine($"englishDescriptionChars={metadata.EnglishDescription.Length}");
            Console.WriteLine($"chineseDescriptionChars={metadata.ChineseDescription.Length}");
            Console.WriteLine($"{WorkshopTarget.DisplayName} Workshop VDF is valid");
            return 0;
        }
        var appId = WorkshopTarget.AppId.ToString(CultureInfo.InvariantCulture);
        Environment.SetEnvironmentVariable("SteamAppId", appId);
        Environment.SetEnvironmentVariable("SteamGameId", appId);
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        if (!SteamAPI.IsSteamRunning())
        {
            throw new InvalidOperationException("Steam client is not running");
        }
        if (!SteamAPI.Init())
        {
            throw new InvalidOperationException("SteamAPI.Init failed; Steam must be logged in");
        }

        try
        {
            SteamWorkshopPublisher.ValidateAccount();
            var before = SteamWorkshopPublisher.QueryTarget();
            SteamWorkshopPublisher.PrintTarget(before);
            if (queryOnly)
            {
                Console.WriteLine($"{WorkshopTarget.DisplayName} Workshop target is valid");
                return 0;
            }

            if (metadataOnly)
            {
                SteamWorkshopPublisher.SubmitLocalizedMetadata(metadata!);
            }
            else
            {
                SteamWorkshopPublisher.SubmitUpdate(metadata!, before, updatePreview);
            }
            var after = SteamWorkshopPublisher.QueryTarget();
            SteamWorkshopPublisher.PrintTarget(after);
            if (after.m_rtimeUpdated < before.m_rtimeUpdated)
            {
                throw new InvalidOperationException("Workshop timestamp moved backwards after upload");
            }
            Console.WriteLine($"{WorkshopTarget.DisplayName} Steam Workshop update completed");
            Console.WriteLine(WorkshopTarget.Url);
            return 0;
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    private static string ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }
        return string.Empty;
    }
}
