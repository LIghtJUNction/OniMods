using System.Globalization;
using Steamworks;

internal enum SteamAppRole
{
    Creator,
    Consumer,
}

internal static class SteamAppContext
{
    internal static void ValidateHints()
    {
        ValidateHint(SteamAppRole.Creator);
        ValidateHint(SteamAppRole.Consumer);
    }

    internal static void Prepare(SteamAppRole role)
    {
        ValidateHints();
        var appId = ExpectedAppId(role).ToString(CultureInfo.InvariantCulture);
        foreach (var name in new[] { "SteamAppId", "SteamGameId" })
        {
            var launchValue = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(launchValue)
                && !string.Equals(launchValue, appId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Steam {role} process was launched with {name}={launchValue}; "
                    + $"expected {appId}. Set it before starting the process.");
            }
        }
        Environment.SetEnvironmentVariable("SteamAppId", appId);
        Environment.SetEnvironmentVariable("SteamGameId", appId);
        Directory.SetCurrentDirectory(HintDirectory(role));
    }

    internal static void VerifyActive(SteamAppRole role)
    {
        var expected = ExpectedAppId(role);
        var actual = SteamUtils.GetAppID().m_AppId;
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Steam initialized with App ID {actual}, expected {expected} for {role}");
        }
        Console.WriteLine($"steamContext={role} appId={actual}");
    }

    private static uint ExpectedAppId(SteamAppRole role) =>
        role == SteamAppRole.Creator
            ? WorkshopTarget.CreatorAppId : WorkshopTarget.ConsumerAppId;

    internal static string HintDirectory(SteamAppRole role) =>
        role == SteamAppRole.Creator
            ? AppContext.BaseDirectory
            : Path.Combine(AppContext.BaseDirectory, "consumer");

    private static void ValidateHint(SteamAppRole role)
    {
        var path = Path.Combine(HintDirectory(role), "steam_appid.txt");
        var expected = ExpectedAppId(role).ToString(CultureInfo.InvariantCulture);
        if (!File.Exists(path)
            || !string.Equals(File.ReadAllText(path).Trim(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Steam {role} App ID hint must be {expected}: {path}");
        }
    }
}
