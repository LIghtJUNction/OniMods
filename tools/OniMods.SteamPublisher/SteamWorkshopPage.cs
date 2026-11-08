using System.Diagnostics;
using Steamworks;

internal static class SteamWorkshopPage
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    internal static int Run(string[] args)
    {
        var request = PageRequest.Parse(args);
        if (request.Role is null)
        {
            if (request.Action == "show")
            {
                WithSteam(SteamAppRole.Consumer, () => Show(request.Id));
                return 0;
            }
            if (!request.Edit.HasMetadata && request.Edit.Preview is null)
                throw new ArgumentException("Specify at least one page field to change");
            if (request.Edit.HasMetadata) RunChild(SteamAppRole.Consumer, request);
            if (request.Edit.Preview is not null) RunChild(SteamAppRole.Creator, request);
            return 0;
        }

        var role = request.Role == "creator" ? SteamAppRole.Creator : SteamAppRole.Consumer;
        WithSteam(role, () =>
        {
            if (role == SteamAppRole.Consumer) ApplyMetadata(request.Id, request.Edit);
            else ApplyPreview(request.Id, request.Edit);
        });
        return 0;
    }

    private static void Show(ulong id)
    {
        var english = Read(id, "english");
        var chinese = Read(id, "schinese");
        Print(english, chinese.Title, chinese.Description);
    }

    private static void ApplyMetadata(ulong id, PageEdit edit)
    {
        var before = Read(id, "english");
        RequireLegacy(before, "before the page edit");
        if (edit.Title is not null || edit.Description is not null || edit.Tags is not null
            || edit.Visibility is not null)
        {
            var handle = Start(id);
            Require(SteamUGC.SetItemUpdateLanguage(handle, "english"), "SetItemUpdateLanguage english");
            if (edit.Title is not null)
                Require(SteamUGC.SetItemTitle(handle, edit.Title), "SetItemTitle");
            if (edit.Description is not null)
                Require(SteamUGC.SetItemDescription(handle, edit.Description), "SetItemDescription");
            if (edit.Tags is not null)
                Require(SteamUGC.SetItemTags(handle, edit.Tags), "SetItemTags");
            if (edit.Visibility is not null)
                Require(SteamUGC.SetItemVisibility(handle, edit.Visibility.Value), "SetItemVisibility");
            Submit(handle, edit.ChangeNote ?? "");
        }
        if (edit.ChineseTitle is not null || edit.ChineseDescription is not null)
        {
            var current = Read(id, "schinese");
            var handle = Start(id);
            Require(SteamUGC.SetItemUpdateLanguage(handle, "schinese"), "SetItemUpdateLanguage schinese");
            Require(SteamUGC.SetItemTitle(handle, edit.ChineseTitle ?? current.Title), "SetItemTitle schinese");
            Require(SteamUGC.SetItemDescription(
                handle, edit.ChineseDescription ?? current.Description), "SetItemDescription schinese");
            var englishAlsoUpdated = edit.Title is not null || edit.Description is not null
                || edit.Tags is not null || edit.Visibility is not null;
            Submit(handle, englishAlsoUpdated ? "" : edit.ChangeNote ?? "");
        }
        var after = WaitForReadback(id, before.FileBytes, edit);
        if (edit.ChineseTitle is null && edit.ChineseDescription is null)
        {
            Print(after, null, null);
            return;
        }
        var chinese = Read(id, "schinese");
        Print(after, chinese.Title, chinese.Description);
    }

    private static void ApplyPreview(ulong id, PageEdit edit)
    {
        var preview = edit.Preview ?? throw new InvalidOperationException("Preview path is missing");
        var before = Read(id, "english");
        RequireLegacy(before, "before the preview edit");
        var extension = Path.GetExtension(preview).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif"))
            throw new ArgumentException("Preview must be PNG, JPEG, or GIF");
        var bytes = File.ReadAllBytes(preview);
        if (bytes.Length is 0 or > 10 * 1024 * 1024)
            throw new ArgumentException("Preview must be between 1 byte and 10 MiB");
        var cloudName = $"onim_{id}_preview_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{extension}";
        Require(SteamRemoteStorage.FileWrite(cloudName, bytes, bytes.Length), "Preview FileWrite");
        var shared = Wait<RemoteStorageFileShareResult_t>(
            SteamRemoteStorage.FileShare(cloudName), "Workshop preview share");
        if (shared.m_eResult != EResult.k_EResultOK)
            throw new InvalidOperationException("Workshop preview share failed: " + shared.m_eResult);
        var handle = SteamRemoteStorage.CreatePublishedFileUpdateRequest(new PublishedFileId_t(id));
        if (handle.m_PublishedFileUpdateHandle == ulong.MaxValue)
            throw new InvalidOperationException("Legacy Workshop update handle is invalid");
        Require(SteamRemoteStorage.UpdatePublishedFilePreviewFile(handle, cloudName),
            "UpdatePublishedFilePreviewFile");
        if (!string.IsNullOrWhiteSpace(edit.ChangeNote))
            Require(SteamRemoteStorage.UpdatePublishedFileSetChangeDescription(handle, edit.ChangeNote),
                "UpdatePublishedFileSetChangeDescription");
        var result = Wait<RemoteStorageUpdatePublishedFileResult_t>(
            SteamRemoteStorage.CommitPublishedFileUpdate(handle), "Legacy preview update");
        if (result.m_eResult != EResult.k_EResultOK
            || result.m_nPublishedFileId.m_PublishedFileId != id
            || result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            throw new InvalidOperationException(
                $"Preview update failed: result={result.m_eResult}, legalAgreement={result.m_bUserNeedsToAcceptWorkshopLegalAgreement}");
        var after = WaitForReadback(id, before.FileBytes, new PageEdit());
        Print(after, null, null);
    }

    private static PageSnapshot WaitForReadback(ulong id, uint fileBytes, PageEdit edit)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var lastError = "no result";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var page = Read(id, "english");
                RequireLegacy(page, "after the page edit");
                if (page.FileBytes != fileBytes)
                    throw new InvalidOperationException(
                        $"Workshop file size changed from {fileBytes} to {page.FileBytes}; the Legacy ZIP was not meant to change");
                if (edit.Title is not null && page.Title != edit.Title)
                    throw new InvalidOperationException("Steam title readback differs");
                if (edit.Description is not null && Normalize(page.Description) != Normalize(edit.Description))
                    throw new InvalidOperationException("Steam description readback differs");
                if (edit.Visibility is not null && page.Visibility != edit.Visibility)
                    throw new InvalidOperationException("Steam visibility readback differs");
                if (edit.Tags is not null && !page.Tags.ToHashSet(StringComparer.Ordinal).SetEquals(edit.Tags))
                    throw new InvalidOperationException("Steam tags readback differs");
                if (edit.ChineseTitle is not null || edit.ChineseDescription is not null)
                {
                    var chinese = Read(id, "schinese");
                    if (edit.ChineseTitle is not null && chinese.Title != edit.ChineseTitle)
                        throw new InvalidOperationException("Steam Chinese title readback differs");
                    if (edit.ChineseDescription is not null
                        && Normalize(chinese.Description) != Normalize(edit.ChineseDescription))
                        throw new InvalidOperationException("Steam Chinese description readback differs");
                }
                return page;
            }
            catch (InvalidOperationException error) when (!IsFatalReadback(error))
            {
                lastError = error.Message;
                Thread.Sleep(3000);
            }
        }
        throw new TimeoutException("Workshop page readback timed out: " + lastError);
    }

    private static PageSnapshot Read(ulong id, string language)
    {
        var query = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(id)], 1);
        if (query.m_UGCQueryHandle == ulong.MaxValue)
            throw new InvalidOperationException("Steam rejected the Workshop page query");
        try
        {
            Require(SteamUGC.SetLanguage(query, language), "SetLanguage");
            Require(SteamUGC.SetReturnLongDescription(query, true), "SetReturnLongDescription");
            SteamUGC.SetAllowCachedResponse(query, 0);
            var completed = Wait<SteamUGCQueryCompleted_t>(
                SteamUGC.SendQueryUGCRequest(query), "Workshop page query");
            if (completed.m_eResult != EResult.k_EResultOK || completed.m_unNumResultsReturned != 1
                || !SteamUGC.GetQueryUGCResult(query, 0, out var details)
                || details.m_eResult != EResult.k_EResultOK
                || details.m_nPublishedFileId.m_PublishedFileId != id)
                throw new InvalidOperationException("Workshop page query failed: " + completed.m_eResult);
            EnsureOwner(details);
            var tags = new List<string>();
            var tagCount = SteamUGC.GetQueryUGCNumTags(query, 0);
            for (uint index = 0; index < tagCount; index++)
            {
                if (!SteamUGC.GetQueryUGCTag(query, 0, index, out var tag, 1024))
                    throw new InvalidOperationException($"Could not read Workshop tag {index}");
                tags.Add(tag);
            }
            if (details.m_nFileSize < 0)
                throw new InvalidOperationException("Workshop file size is negative");
            return new PageSnapshot(
                id, details.m_rgchTitle, details.m_rgchDescription, tags.ToArray(),
                details.m_eVisibility, (uint)details.m_nFileSize, details.m_hFile.m_UGCHandle,
                details.m_eFileType, details.m_nCreatorAppID.m_AppId, details.m_nConsumerAppID.m_AppId);
        }
        finally
        {
            SteamUGC.ReleaseQueryUGCRequest(query);
        }
    }

    private static void Print(PageSnapshot page, string? chineseTitle, string? chineseDescription)
    {
        Console.WriteLine($"workshopId={page.Id}");
        Console.WriteLine($"url=https://steamcommunity.com/sharedfiles/filedetails/?id={page.Id}");
        Console.WriteLine($"visibility={VisibilityName(page.Visibility)}");
        Console.WriteLine($"legacyFile={(page.FileHandle != ulong.MaxValue)}");
        Console.WriteLine($"fileType={page.FileType}");
        Console.WriteLine($"fileBytes={page.FileBytes}");
        Console.WriteLine($"creatorApp={page.CreatorApp}");
        Console.WriteLine($"consumerApp={page.ConsumerApp}");
        Console.WriteLine("tags=" + string.Join(", ", page.Tags));
        Console.WriteLine("englishTitle=" + page.Title);
        Console.WriteLine("englishDescription<<");
        Console.WriteLine(page.Description);
        Console.WriteLine("<<");
        if (chineseTitle is not null || chineseDescription is not null)
        {
            Console.WriteLine("schineseTitle=" + (chineseTitle ?? ""));
            Console.WriteLine("schineseDescription<<");
            Console.WriteLine(chineseDescription ?? "");
            Console.WriteLine("<<");
        }
    }

    private static void RequireLegacy(PageSnapshot page, string when)
    {
        if (page.FileHandle == ulong.MaxValue)
            throw new InvalidOperationException(
                $"Workshop item has no Legacy file handle {when}. Refusing to continue, because a directory-mode item cannot be installed by ONI.");
        if (page.CreatorApp != WorkshopTarget.CreatorAppId || page.ConsumerApp != WorkshopTarget.ConsumerAppId)
            throw new InvalidOperationException(
                $"Workshop item belongs to creator {page.CreatorApp} and consumer {page.ConsumerApp}, not ONI");
    }

    private static void EnsureOwner(SteamUGCDetails_t details)
    {
        var account = SteamUser.GetSteamID().m_SteamID;
        if (details.m_ulSteamIDOwner != account)
            throw new InvalidOperationException($"Steam account {account} does not own this Workshop item");
        if (WorkshopTarget.ConfiguredOwner is ulong expected && account != expected)
            throw new InvalidOperationException($"Steam account {account} is not the expected Workshop owner");
    }

    private static UGCUpdateHandle_t Start(ulong id)
    {
        SteamAppContext.VerifyActive(SteamAppRole.Consumer);
        var handle = SteamUGC.StartItemUpdate(
            new AppId_t(WorkshopTarget.ConsumerAppId), new PublishedFileId_t(id));
        if (handle.m_UGCUpdateHandle == ulong.MaxValue)
            throw new InvalidOperationException("Steam returned an invalid item update handle");
        return handle;
    }

    private static void Submit(UGCUpdateHandle_t handle, string changeNote)
    {
        var result = Wait<SubmitItemUpdateResult_t>(
            SteamUGC.SubmitItemUpdate(handle, changeNote), "Workshop page update");
        if (result.m_eResult != EResult.k_EResultOK || result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            throw new InvalidOperationException(
                $"Workshop page update failed: result={result.m_eResult}, legalAgreement={result.m_bUserNeedsToAcceptWorkshopLegalAgreement}");
    }

    private static void RunChild(SteamAppRole role, PageRequest request)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false,
            WorkingDirectory = SteamAppContext.HintDirectory(role) };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--workshop");
        start.ArgumentList.Add("page");
        start.ArgumentList.Add("set");
        start.ArgumentList.Add(request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--page-role");
        start.ArgumentList.Add(role == SteamAppRole.Creator ? "creator" : "consumer");
        var appId = (role == SteamAppRole.Creator ? WorkshopTarget.CreatorAppId : WorkshopTarget.ConsumerAppId)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["SteamAppId"] = appId;
        start.Environment["SteamGameId"] = appId;
        if (WorkshopTarget.ConfiguredOwner is ulong owner)
            start.Environment["ONIM_PUBLISH_EXPECTED_OWNER"] = owner.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var edit = request.Edit;
        if (role == SteamAppRole.Consumer)
        {
            Add(start, "--title", edit.Title);
            Add(start, "--description-file", edit.DescriptionPath);
            Add(start, "--chinese-title", edit.ChineseTitle);
            Add(start, "--chinese-description-file", edit.ChineseDescriptionPath);
            if (edit.Tags is not null) Add(start, "--tags", string.Join(",", edit.Tags));
            if (edit.Visibility is not null) Add(start, "--visibility", VisibilityName(edit.Visibility.Value));
            if (edit.HasMetadata) Add(start, "--change-note", edit.ChangeNote);
        }
        else
        {
            Add(start, "--preview", edit.Preview);
            if (!edit.HasMetadata) Add(start, "--change-note", edit.ChangeNote);
        }
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start Workshop page editor");
        if (!child.WaitForExit(Timeout))
        {
            child.Kill(entireProcessTree: true);
            throw new TimeoutException("Workshop page edit timed out");
        }
        if (child.ExitCode != 0)
            throw new InvalidOperationException("Workshop page edit failed");
    }

    private static void WithSteam(SteamAppRole role, Action action)
    {
        SteamAppContext.Prepare(role);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
            throw new InvalidOperationException("Steam client must be running and logged in");
        try
        {
            SteamAppContext.VerifyActive(role);
            if (!SteamUser.BLoggedOn())
                throw new InvalidOperationException("Steam client is not logged in");
            action();
        }
        finally
        {
            SteamAPI.Shutdown();
        }
    }

    private static void Add(ProcessStartInfo start, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        start.ArgumentList.Add(name);
        start.ArgumentList.Add(value);
    }

    private static string VisibilityName(ERemoteStoragePublishedFileVisibility visibility) => visibility switch
    {
        ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic => "public",
        ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly => "friends",
        ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate => "private",
        ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted => "unlisted",
        _ => throw new InvalidOperationException("Unsupported Workshop visibility: " + visibility),
    };

    private static bool IsFatalReadback(InvalidOperationException error) =>
        error.Message.Contains("file size changed", StringComparison.Ordinal)
        || error.Message.Contains("no Legacy file handle", StringComparison.Ordinal)
        || error.Message.Contains("not ONI", StringComparison.Ordinal)
        || error.Message.Contains("does not own", StringComparison.Ordinal)
        || error.Message.Contains("expected Workshop owner", StringComparison.Ordinal);

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    private static void Require(bool ok, string operation)
    {
        if (!ok) throw new InvalidOperationException(operation + " failed");
    }

    private static T Wait<T>(SteamAPICall_t call, string operation) where T : struct
    {
        var completed = false;
        var failed = false;
        var result = default(T);
        using var callback = CallResult<T>.Create((value, ioFailure) =>
        {
            result = value;
            failed = ioFailure;
            completed = true;
        });
        callback.Set(call);
        var deadline = DateTime.UtcNow + Timeout;
        while (!completed && DateTime.UtcNow < deadline)
        {
            SteamAPI.RunCallbacks();
            Thread.Sleep(100);
        }
        if (!completed) throw new TimeoutException(operation + " timed out");
        if (failed) throw new InvalidOperationException(operation + " failed due to an I/O error");
        return result;
    }
}

internal sealed class PageEdit
{
    internal string? Title;
    internal string? Description;
    internal string? DescriptionPath;
    internal string? ChineseTitle;
    internal string? ChineseDescription;
    internal string? ChineseDescriptionPath;
    internal string[]? Tags;
    internal ERemoteStoragePublishedFileVisibility? Visibility;
    internal string? Preview;
    internal string? ChangeNote;
    internal bool HasMetadata => Title is not null || Description is not null || ChineseTitle is not null
        || ChineseDescription is not null || Tags is not null || Visibility is not null;
}

internal sealed record PageSnapshot(
    ulong Id, string Title, string Description, string[] Tags,
    ERemoteStoragePublishedFileVisibility Visibility, uint FileBytes, ulong FileHandle,
    EWorkshopFileType FileType, uint CreatorApp, uint ConsumerApp);

internal sealed record PageRequest(string Action, ulong Id, string? Role, PageEdit Edit)
{
    internal static PageRequest Parse(string[] args)
    {
        if (args.Length < 4 || args[0] != "--workshop" || args[1] != "page" || args[2] is not ("show" or "set"))
            throw new ArgumentException(Usage);
        if (!ulong.TryParse(args[3], out var id) || id == 0)
            throw new ArgumentException("Workshop ID must be a positive integer");
        var edit = new PageEdit();
        string? role = null;
        for (var index = 4; index < args.Length; index++)
        {
            var option = args[index];
            if (option is "--content" or "--contentfolder" or "--steamcmd" or "--file")
                throw new ArgumentException("页面编辑不能替换 Legacy ZIP。更新模组文件请用 onim publish。");
            switch (option)
            {
                case "--page-role": role = Take(args, ref index); break;
                case "--title": edit.Title = Limit(Take(args, ref index), 128, "title"); break;
                case "--description-file":
                    edit.DescriptionPath = Path.GetFullPath(Take(args, ref index));
                    edit.Description = ReadText(edit.DescriptionPath);
                    break;
                case "--chinese-title": edit.ChineseTitle = Limit(Take(args, ref index), 128, "Chinese title"); break;
                case "--chinese-description-file":
                    edit.ChineseDescriptionPath = Path.GetFullPath(Take(args, ref index));
                    edit.ChineseDescription = ReadText(edit.ChineseDescriptionPath);
                    break;
                case "--tags":
                    edit.Tags = Take(args, ref index).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (edit.Tags.Length == 0) throw new ArgumentException("Tags must contain at least one value");
                    break;
                case "--visibility": edit.Visibility = ParseVisibility(Take(args, ref index)); break;
                case "--preview": edit.Preview = Path.GetFullPath(Take(args, ref index)); break;
                case "--change-note": edit.ChangeNote = Limit(Take(args, ref index), 8000, "change note"); break;
                default: throw new ArgumentException("Unknown page option: " + option);
            }
        }
        if (args[2] == "show" && (role is not null || edit.HasMetadata || edit.Preview is not null || edit.ChangeNote is not null))
            throw new ArgumentException("page show takes only a Workshop ID");
        if (role is not (null or "consumer" or "creator"))
            throw new ArgumentException("Invalid page role");
        if (edit.ChangeNote is not null && !edit.HasMetadata && edit.Preview is null)
            throw new ArgumentException("A change note needs a page field to update");
        return new PageRequest(args[2], id, role, edit);
    }

    private static string Take(string[] args, ref int index)
    {
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Missing value for " + args[index - 1]);
        return args[index];
    }

    private static string ReadText(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Page text file is missing", path);
        var text = File.ReadAllText(path).Trim();
        if (text.Length is 0 or > 8000) throw new ArgumentException("Page text must be 1 to 8000 characters: " + path);
        return text;
    }

    private static string Limit(string text, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum)
            throw new ArgumentException($"{name} must be 1 to {maximum} characters");
        return text;
    }

    private static ERemoteStoragePublishedFileVisibility ParseVisibility(string text) => text switch
    {
        "public" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
        "friends" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
        "private" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
        "unlisted" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
        _ => throw new ArgumentException("Visibility must be public, friends, private, or unlisted"),
    };

    private const string Usage =
        "Usage: --workshop page show <id> | --workshop page set <id> [--title <text>] [--description-file <path>] [--chinese-title <text>] [--chinese-description-file <path>] [--tags <a,b>] [--visibility public|friends|private|unlisted] [--preview <image>] [--change-note <text>]";
}
