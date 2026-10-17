using System.Net.Http;
using System.Text.Json;

internal sealed record SteamPublicPage(
    ulong Id, uint CreatorAppId, uint ConsumerAppId,
    ulong OwnerId, string Title, string Description,
    int Visibility, string[] Tags, long FileSize)
{
    internal static SteamPublicPage Parse(string json, ulong expectedId)
    {
        using var document = JsonDocument.Parse(json);
        var details = document.RootElement.GetProperty("response")
            .GetProperty("publishedfiledetails")[0];
        var result = details.GetProperty("result").GetInt32();
        if (result != 1)
        {
            throw new InvalidOperationException(
                $"Public Web API item {expectedId} is not visible: result={result}");
        }
        var id = ulong.Parse(details.GetProperty("publishedfileid").GetString()!);
        if (id != expectedId)
        {
            throw new InvalidOperationException("Public Web API returned a different item ID");
        }
        var tags = details.GetProperty("tags").EnumerateArray()
            .Select(item => item.GetProperty("tag").GetString() ?? string.Empty)
            .ToArray();
        return new SteamPublicPage(
            id,
            details.GetProperty("creator_app_id").GetUInt32(),
            details.GetProperty("consumer_app_id").GetUInt32(),
            ulong.Parse(details.GetProperty("creator").GetString()!),
            details.GetProperty("title").GetString() ?? string.Empty,
            details.GetProperty("description").GetString() ?? string.Empty,
            details.GetProperty("visibility").GetInt32(),
            tags,
            long.Parse(details.GetProperty("file_size").GetString()!));
    }
}

internal static class SteamPublicPageReadback
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    internal static void WaitForReviewedPage(
        WorkshopPromotionPlan plan, bool oldPage)
    {
        var itemId = oldPage ? plan.OriginalId : plan.NewId;
        var deadline = DateTime.UtcNow.AddMinutes(2);
        var lastError = "no response";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var body = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["itemcount"] = "1",
                    ["publishedfileids[0]"] = itemId.ToString(),
                });
                using var response = Client.PostAsync(
                    "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    body).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                var json = response.Content.ReadAsStringAsync()
                    .GetAwaiter().GetResult();
                var page = SteamPublicPage.Parse(json, itemId);
                if (Matches(plan, page, oldPage))
                {
                    Console.WriteLine($"publicWebReadback={itemId} title={page.Title}");
                    return;
                }
                lastError = $"visibility={page.Visibility}, title={page.Title}, "
                    + $"descriptionChars={page.Description.Length}, "
                    + $"tags={string.Join(",", page.Tags)}";
            }
            catch (Exception error) when (
                error is HttpRequestException or TaskCanceledException
                    or JsonException or InvalidOperationException or FormatException)
            {
                lastError = error.Message;
            }
            Thread.Sleep(5000);
        }
        throw new TimeoutException(
            $"Public Web API did not show reviewed item {itemId}: {lastError}");
    }

    internal static bool Matches(
        WorkshopPromotionPlan plan, SteamPublicPage page, bool oldPage)
    {
        if (page.CreatorAppId != 636750 || page.ConsumerAppId != 457140
            || page.OwnerId != 76561199137573787 || page.Visibility != 0)
        {
            return false;
        }
        if (oldPage)
        {
            return page.Id == plan.OriginalId
                && page.Title == plan.OldTitle
                && page.Description == plan.OldDescriptionEnglish
                && page.Tags.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(plan.Baseline.Original.Tags);
        }
        return page.Id == plan.NewId
            && page.Title == plan.NewTitle
            && page.Description == plan.NewDescriptionEnglish
            && page.Tags.ToHashSet(StringComparer.Ordinal).SetEquals(plan.NewTags)
            && (page.FileSize == 0 || page.FileSize == plan.ZipBytes);
    }
}
