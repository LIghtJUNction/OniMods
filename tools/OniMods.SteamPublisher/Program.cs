using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
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
        var consumerContext = args.Contains("--consumer-context", StringComparer.Ordinal);
        var validateOnly = args.Contains("--validate-vdf", StringComparer.Ordinal);
        var metadataOnly = args.Contains("--metadata-only", StringComparer.Ordinal);
        var verifyInstalled = args.Contains("--verify-installed", StringComparer.Ordinal);
        var updatePreview = args.Contains("--update-preview", StringComparer.Ordinal);
        var noChangeNote = args.Contains("--no-change-note", StringComparer.Ordinal);
        if (verifyInstalled)
        {
            if (queryOnly || validateOnly || metadataOnly || updatePreview || noChangeNote)
            {
                throw new ArgumentException("--verify-installed cannot be combined with another mode");
            }
            return VerifyInConsumerContext(args);
        }
        if (consumerContext && !queryOnly)
        {
            throw new ArgumentException("--consumer-context is only valid with --query-only");
        }
        var vdfPath = ReadOption(args, "--vdf");
        if ((queryOnly && validateOnly)
            || (!queryOnly && string.IsNullOrWhiteSpace(vdfPath)))
        {
            throw new ArgumentException(
                "Usage: OniMods.SteamPublisher --query-only [--consumer-context] | --validate-vdf --vdf <path> | --metadata-only --vdf <path> | --vdf <path>");
        }

        SteamAppContext.ValidateHints();
        var metadata = queryOnly
            ? null
            : WorkshopMetadataReader.Read(Path.GetFullPath(vdfPath));
        if (noChangeNote && metadata is not null)
        {
            metadata = metadata with { ChangeNote = string.Empty };
        }
        if (validateOnly)
        {
            var package = LegacyPackage.Create(metadata!);
            Console.WriteLine($"title={metadata!.Title}");
            Console.WriteLine($"englishDescriptionChars={metadata.EnglishDescription.Length}");
            Console.WriteLine($"chineseDescriptionChars={metadata.ChineseDescription.Length}");
            Console.WriteLine($"legacyZip={package.Path}");
            Console.WriteLine($"legacyZipBytes={package.Bytes.Length}");
            Console.WriteLine($"creatorApp={WorkshopTarget.CreatorAppId}");
            Console.WriteLine($"consumerApp={WorkshopTarget.ConsumerAppId}");
            Console.WriteLine($"{WorkshopTarget.DisplayName} legacy Workshop ZIP is valid");
            return 0;
        }
        var role = consumerContext ? SteamAppRole.Consumer : SteamAppRole.Creator;
        SteamAppContext.Prepare(role);
        if (!SteamAPI.IsSteamRunning())
        {
            throw new InvalidOperationException("Steam client is not running");
        }
        if (!SteamAPI.Init())
        {
            throw new InvalidOperationException("SteamAPI.Init failed; Steam must be logged in");
        }

        LegacyPackage? uploadedPackage = null;
        uint previousServerUpdated = 0;
        try
        {
            SteamAppContext.VerifyActive(role);
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
                var package = LegacyPackage.Create(metadata!);
                Console.WriteLine($"legacyZip={package.Path}");
                Console.WriteLine($"legacyZipBytes={package.Bytes.Length}");
                SteamWorkshopPublisher.SubmitLegacyUpdate(
                    metadata!, package, before, updatePreview);
                uploadedPackage = package;
                previousServerUpdated = before.m_rtimeUpdated;
            }
            var after = SteamWorkshopPublisher.QueryTarget();
            SteamWorkshopPublisher.PrintTarget(after);
            if (after.m_rtimeUpdated < before.m_rtimeUpdated)
            {
                throw new InvalidOperationException("Workshop timestamp moved backwards after upload");
            }
        }
        finally
        {
            SteamAPI.Shutdown();
        }

        if (uploadedPackage is not null)
        {
            RunConsumerVerifier(uploadedPackage, previousServerUpdated);
        }
        Console.WriteLine($"{WorkshopTarget.DisplayName} Steam Workshop update completed");
        Console.WriteLine(WorkshopTarget.Url);
        return 0;
    }

    private static int VerifyInConsumerContext(string[] args)
    {
        var zipPath = ReadOption(args, "--zip");
        var updatedText = ReadOption(args, "--previous-updated");
        var expectedHash = ReadOption(args, "--expected-sha256");
        if (string.IsNullOrWhiteSpace(zipPath)
            || !uint.TryParse(updatedText, NumberStyles.None,
                CultureInfo.InvariantCulture, out var previousUpdated)
            || expectedHash.Length != 64
            || !expectedHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Usage: OniMods.SteamPublisher --verify-installed --zip <path> --previous-updated <timestamp> --expected-sha256 <hash>");
        }
        var package = LegacyPackage.LoadForVerification(zipPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(package.Bytes));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Workshop ZIP changed after upload; verification cannot use a different package");
        }
        SteamAppContext.Prepare(SteamAppRole.Consumer);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
        {
            throw new InvalidOperationException(
                "Steam consumer context could not initialize for install verification");
        }
        try
        {
            SteamAppContext.VerifyActive(SteamAppRole.Consumer);
            SteamWorkshopPublisher.ValidateAccount();
            var current = SteamWorkshopPublisher.QueryTarget();
            SteamWorkshopPublisher.VerifyInstalledLegacy(
                package, current, previousUpdated);
            return 0;
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    private static void RunConsumerVerifier(
        LegacyPackage package, uint previousUpdated)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = SteamAppContext.HintDirectory(SteamAppRole.Consumer),
        };
        var consumerAppId = WorkshopTarget.ConsumerAppId.ToString(
            CultureInfo.InvariantCulture);
        start.Environment["SteamAppId"] = consumerAppId;
        start.Environment["SteamGameId"] = consumerAppId;
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--verify-installed");
        start.ArgumentList.Add("--zip");
        start.ArgumentList.Add(package.Path);
        start.ArgumentList.Add("--previous-updated");
        start.ArgumentList.Add(previousUpdated.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--expected-sha256");
        start.ArgumentList.Add(Convert.ToHexString(SHA256.HashData(package.Bytes)));
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start consumer verification process");
        if (!child.WaitForExit(TimeSpan.FromMinutes(7)))
        {
            child.Kill(entireProcessTree: true);
            throw new TimeoutException("Consumer verification process timed out");
        }
        if (child.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Consumer verification process failed: exit={child.ExitCode}");
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
