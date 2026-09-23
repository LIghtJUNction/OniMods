using System.Security.Cryptography;
using System.Text;

internal sealed record LegacyCandidateTarget(
    ulong OriginalWorkshopId,
    string DisplayName,
    string SourceTitleContains,
    string CandidateTitle);

internal sealed record LegacyCandidatePlan(
    LegacyCandidateTarget Target,
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
    internal const ulong CycleTrimOriginalWorkshopId = 3766318556;
    internal const string CycleTrimCandidateTitle =
        "CycleTrim [Private Legacy Test Candidate for 3766318556]";

    internal static LegacyCandidatePlan Create(WorkshopMetadata metadata)
    {
        var target = ResolveFixedTarget();
        if (!metadata.Title.Contains(
            target.SourceTitleContains, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Candidate VDF title is not {target.DisplayName}");
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
        var previewName =
            $"onim_candidate_{target.OriginalWorkshopId}_{previewHash[..16]}{extension}";
        var description =
            "PRIVATE LEGACY TEST CANDIDATE — DO NOT SUBSCRIBE. "
            + $"Compatibility test for original Workshop item {target.OriginalWorkshopId}.\n\n"
            + metadata.EnglishDescription;
        if (target.CandidateTitle.Length > 128 || description.Length > 8000)
        {
            throw new InvalidOperationException("Candidate Workshop metadata is too long");
        }
        var packageHash = Convert.ToHexString(SHA256.HashData(package.Bytes));
        var descriptionHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(description)));
        var planData = string.Join('\n',
            target.OriginalWorkshopId,
            WorkshopTarget.CreatorAppId,
            WorkshopTarget.ConsumerAppId,
            WorkshopTarget.ExpectedOwner,
            target.CandidateTitle,
            descriptionHash,
            packageHash,
            previewHash,
            "Private",
            "Community");
        var planHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(planData)));
        return new LegacyCandidatePlan(
            target, metadata, package, previewBytes, previewName,
            target.CandidateTitle, description, planHash);
    }

    internal static LegacyCandidateTarget ResolveFixedTarget()
    {
        if (WorkshopTarget.CreatorAppId != 636750
            || WorkshopTarget.ConsumerAppId != 457140
            || WorkshopTarget.ExpectedOwner != 76561199137573787)
        {
            throw new InvalidOperationException(
                "Private Legacy candidate target has the wrong owner or App IDs");
        }
        return (WorkshopTarget.WorkshopId, WorkshopTarget.DisplayName) switch
        {
            (OriginalWorkshopId, "OniMcp") => new LegacyCandidateTarget(
                OriginalWorkshopId, "OniMcp", "ONI MCP Server", CandidateTitle),
            (CycleTrimOriginalWorkshopId, "CycleTrim") => new LegacyCandidateTarget(
                CycleTrimOriginalWorkshopId, "CycleTrim", "CycleTrim",
                CycleTrimCandidateTitle),
            _ => throw new InvalidOperationException(
                "Private Legacy candidate creation is limited to the two owned OniMods items"),
        };
    }

    internal void Print()
    {
        Console.WriteLine($"sourceWorkshopId={Target.OriginalWorkshopId}");
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
