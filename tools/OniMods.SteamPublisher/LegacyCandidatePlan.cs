using System.Security.Cryptography;
using System.Text;

internal sealed record LegacyCandidatePlan(
    WorkshopMetadata Metadata,
    LegacyPackage Package,
    byte[] PreviewBytes,
    string PreviewCloudFileName,
    string Title,
    string Description,
    string PlanSha256)
{
    internal const ulong OriginalWorkshopId = 3731864673;
    internal const string CandidateTitle =
        "ONI MCP Server [Private Legacy Test Candidate for 3731864673]";

    internal static LegacyCandidatePlan Create(WorkshopMetadata metadata)
    {
        ValidateFixedTarget();
        if (!metadata.Title.Contains("ONI MCP Server", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Candidate VDF title is not OniMcp");
        }

        var package = LegacyPackage.Create(metadata);
        var previewBytes = File.ReadAllBytes(metadata.PreviewFile);
        if (previewBytes.Length == 0 || previewBytes.Length > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException("Candidate preview must be 1 byte to 10 MiB");
        }
        var extension = Path.GetExtension(metadata.PreviewFile).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
        {
            throw new InvalidOperationException("Candidate preview must be PNG or JPEG");
        }
        var previewHash = Convert.ToHexString(SHA256.HashData(previewBytes));
        var previewName = $"onim_candidate_{OriginalWorkshopId}_{previewHash[..16]}{extension}";
        var description =
            "PRIVATE LEGACY TEST CANDIDATE — DO NOT SUBSCRIBE. "
            + $"Compatibility test for original Workshop item {OriginalWorkshopId}.\n\n"
            + metadata.EnglishDescription;
        if (CandidateTitle.Length > 128 || description.Length > 8000)
        {
            throw new InvalidOperationException("Candidate Workshop metadata is too long");
        }
        var packageHash = Convert.ToHexString(SHA256.HashData(package.Bytes));
        var descriptionHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(description)));
        var planData = string.Join('\n',
            OriginalWorkshopId,
            WorkshopTarget.CreatorAppId,
            WorkshopTarget.ConsumerAppId,
            WorkshopTarget.ExpectedOwner,
            CandidateTitle,
            descriptionHash,
            packageHash,
            previewHash,
            "Private",
            "Community");
        var planHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(planData)));
        return new LegacyCandidatePlan(
            metadata, package, previewBytes, previewName,
            CandidateTitle, description, planHash);
    }

    internal static void ValidateFixedTarget()
    {
        if (WorkshopTarget.WorkshopId != OriginalWorkshopId
            || WorkshopTarget.CreatorAppId != 636750
            || WorkshopTarget.ConsumerAppId != 457140
            || WorkshopTarget.ExpectedOwner != 76561199137573787
            || !string.Equals(WorkshopTarget.DisplayName, "OniMcp", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Private Legacy candidate creation is limited to the owned OniMcp item 3731864673");
        }
    }

    internal void Print()
    {
        Console.WriteLine($"sourceWorkshopId={OriginalWorkshopId}");
        Console.WriteLine($"candidateTitle={Title}");
        Console.WriteLine("candidateVisibility=Private");
        Console.WriteLine($"creatorApp={WorkshopTarget.CreatorAppId}");
        Console.WriteLine($"consumerApp={WorkshopTarget.ConsumerAppId}");
        Console.WriteLine($"owner={WorkshopTarget.ExpectedOwner}");
        Console.WriteLine($"legacyZip={Package.Path}");
        Console.WriteLine($"legacyZipBytes={Package.Bytes.Length}");
        Console.WriteLine($"legacyZipSha256={Convert.ToHexString(SHA256.HashData(Package.Bytes))}");
        Console.WriteLine($"previewFile={Metadata.PreviewFile}");
        Console.WriteLine($"previewBytes={PreviewBytes.Length}");
        Console.WriteLine($"previewSha256={Convert.ToHexString(SHA256.HashData(PreviewBytes))}");
        Console.WriteLine($"planSha256={PlanSha256}");
    }
}
