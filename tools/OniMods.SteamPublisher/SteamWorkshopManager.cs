using Steamworks;

internal static class SteamWorkshopManager
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);
    private static bool skipShutdown;

    internal static bool IsMode(string[] args) =>
        args.Length > 0 && args[0] == "--workshop";

    internal static int Run(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Usage: --workshop search <text> | install <id> | uninstall <id> | page show <id> | page set <id> [fields]");
        if (args[1] == "page") return SteamWorkshopPage.Run(args);

        SteamAppContext.Prepare(SteamAppRole.Consumer);
        if (!SteamAPI.IsSteamRunning() || !SteamAPI.Init())
            throw new InvalidOperationException("Steam client must be running and logged in");
        try
        {
            SteamAppContext.VerifyActive(SteamAppRole.Consumer);
            if (!SteamUser.BLoggedOn())
                throw new InvalidOperationException("Steam client is not logged in");

            return args[1] switch
            {
                "search" when args.Length == 3 => Search(args[2]),
                "install" when args.Length == 3 => Install(ParseId(args[2])),
                "inspect" when args.Length == 3 => Inspect(ParseId(args[2])),
                "uninstall" when args.Length == 3 => Uninstall(ParseId(args[2])),
                _ => throw new ArgumentException(
                    "Usage: --workshop search <text> | install <id> | uninstall <id>"),
            };
        }
        finally
        {
            if (!skipShutdown) SteamAPI.Shutdown();
        }
    }

    private static int Search(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Workshop search text cannot be empty");

        var query = SteamUGC.CreateQueryAllUGCRequest(
            EUGCQuery.k_EUGCQuery_RankedByTextSearch,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items,
            new AppId_t(0), new AppId_t(WorkshopTarget.ConsumerAppId), 1);
        if (query.m_UGCQueryHandle == ulong.MaxValue)
            throw new InvalidOperationException("Steam rejected the Workshop search query");
        try
        {
            Require(SteamUGC.SetSearchText(query, text), "SetSearchText");
            Require(SteamUGC.SetLanguage(query, "english"), "SetLanguage");
            var result = Wait<SteamUGCQueryCompleted_t>(
                SteamUGC.SendQueryUGCRequest(query), "Workshop search");
            if (result.m_eResult != EResult.k_EResultOK)
                throw new InvalidOperationException("Workshop search failed: " + result.m_eResult);

            Console.WriteLine($"results={result.m_unNumResultsReturned}");
            Console.WriteLine($"total={result.m_unTotalMatchingResults}");
            for (uint index = 0; index < result.m_unNumResultsReturned; index++)
            {
                if (!SteamUGC.GetQueryUGCResult(query, index, out var item))
                    throw new InvalidOperationException("SteamUGC.GetQueryUGCResult failed");
                Console.WriteLine($"item={item.m_nPublishedFileId.m_PublishedFileId}\t"
                    + $"kind={item.m_eFileType}\t{item.m_rgchTitle}\t"
                    + item.m_rgchDescription.Replace('\n', ' '));
            }
            return 0;
        }
        finally
        {
            SteamUGC.ReleaseQueryUGCRequest(query);
        }
    }

    private static int Install(ulong id)
    {
        var fileId = new PublishedFileId_t(id);
        try
        {
            var subscribed = Wait<RemoteStorageSubscribePublishedFileResult_t>(
                SteamUGC.SubscribeItem(fileId), "Workshop subscribe");
            if (subscribed.m_eResult != EResult.k_EResultOK
                || subscribed.m_nPublishedFileId.m_PublishedFileId != id)
                throw new InvalidOperationException("Workshop subscribe failed: " + subscribed.m_eResult);

            var installed = false;
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                SteamAPI.RunCallbacks();
                var state = (EItemState)SteamUGC.GetItemState(fileId);
                if ((state & EItemState.k_EItemStateNeedsUpdate) != 0)
                {
                    skipShutdown = true;
                    if (!SteamUGC.DownloadItem(fileId, true))
                        throw new InvalidOperationException("Steam refused the Workshop download");
                    WaitForDownload(id);
                    continue;
                }
                if ((state & EItemState.k_EItemStateInstalled) != 0)
                {
                    if (!SteamUGC.GetItemInstallInfo(fileId, out _, out var path, 4096, out _))
                        throw new InvalidOperationException("Steam has no install path for the Workshop item");
                    if ((state & EItemState.k_EItemStateLegacyItem) == 0
                        || !File.Exists(path) || Directory.Exists(path))
                        throw new InvalidOperationException(
                            "This Workshop item is not a Legacy ZIP that ONI can install");
                    Console.WriteLine("installedLegacyZip=" + Path.GetFullPath(path));
                    Console.WriteLine("workshopId=" + id);
                    installed = true;
                    break;
                }
                Thread.Sleep(250);
            }
            if (!installed)
                throw new TimeoutException("Workshop item did not finish installing within fifteen minutes");
            return 0;
        }
        catch
        {
            try { Uninstall(id); }
            catch (Exception) { /* Keep the original installation error. */ }
            throw;
        }
    }

    private static int Uninstall(ulong id)
    {
        var state = (EItemState)SteamUGC.GetItemState(new PublishedFileId_t(id));
        if ((state & EItemState.k_EItemStateSubscribed) == 0)
        {
            Console.WriteLine("alreadyUnsubscribedWorkshopId=" + id);
            return 0;
        }
        var unsubscribed = Wait<RemoteStorageUnsubscribePublishedFileResult_t>(
            SteamUGC.UnsubscribeItem(new PublishedFileId_t(id)), "Workshop unsubscribe");
        if (unsubscribed.m_eResult != EResult.k_EResultOK
            || unsubscribed.m_nPublishedFileId.m_PublishedFileId != id)
            throw new InvalidOperationException("Workshop unsubscribe failed: " + unsubscribed.m_eResult);
        Console.WriteLine("unsubscribedWorkshopId=" + id);
        return 0;
    }

    private static int Inspect(ulong id)
    {
        var fileId = new PublishedFileId_t(id);
        var state = (EItemState)SteamUGC.GetItemState(fileId);
        Console.WriteLine($"workshopId={id}");
        Console.WriteLine($"itemState={state}");
        if ((state & EItemState.k_EItemStateInstalled) != 0
            && SteamUGC.GetItemInstallInfo(fileId, out _, out var path, 4096, out _))
        {
            if ((state & EItemState.k_EItemStateLegacyItem) == 0
                || !File.Exists(path) || Directory.Exists(path))
                throw new InvalidOperationException("Workshop item is not an ONI-compatible Legacy ZIP");
            Console.WriteLine("installedLegacyZip=" + Path.GetFullPath(path));
        }
        else
        {
            Console.WriteLine("installedLegacyZip=");
        }
        return 0;
    }

    private static void WaitForDownload(ulong id)
    {
        var completed = false;
        var resultCode = EResult.k_EResultFail;
        using var callback = Callback<DownloadItemResult_t>.Create(result =>
        {
            if (result.m_nPublishedFileId.m_PublishedFileId != id
                || result.m_unAppID.m_AppId != WorkshopTarget.ConsumerAppId) return;
            resultCode = result.m_eResult;
            completed = true;
        });
        var deadline = DateTime.UtcNow + Timeout;
        while (!completed && DateTime.UtcNow < deadline)
        {
            SteamAPI.RunCallbacks();
            Thread.Sleep(100);
        }
        if (!completed)
            throw new TimeoutException("Workshop download callback timed out");
        if (resultCode != EResult.k_EResultOK)
            throw new InvalidOperationException("Workshop download failed: " + resultCode);
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
        if (!completed)
            throw new TimeoutException(operation + " timed out");
        if (failed)
            throw new InvalidOperationException(operation + " failed due to an I/O error");
        return result;
    }

    private static ulong ParseId(string text) =>
        ulong.TryParse(text, out var id) && id > 0
            ? id : throw new ArgumentException("Workshop ID must be a positive integer");

    private static void Require(bool ok, string operation)
    {
        if (!ok) throw new InvalidOperationException(operation + " failed");
    }
}
