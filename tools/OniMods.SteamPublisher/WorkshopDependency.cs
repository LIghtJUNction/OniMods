internal sealed record WorkshopDependencyTarget(string ParentName, ulong ParentId, ulong ChildId, string ChildTitle)
{
    internal const uint CreatorAppId = 636750;
    internal const uint ConsumerAppId = 457140;
    internal const ulong Owner = 76561199137573787;

    internal static WorkshopDependencyTarget Create(string name, ulong childId, string childTitle, ulong? explicitParent = null)
    {
        ulong parent = name switch
        {
            "CycleTrim" => 3807166689,
            "OniMcp" => 3807166922,
            "OniOnline" when explicitParent.HasValue => explicitParent.Value,
            _ => throw new ArgumentException("Select CycleTrim, OniMcp, or OniOnline with an explicit published parent ID"),
        };
        if (parent is 0 or ulong.MaxValue || childId is 0 or ulong.MaxValue || parent == childId
            || explicitParent.HasValue && explicitParent.Value != parent || !childTitle.StartsWith("Jlib", StringComparison.Ordinal))
            throw new ArgumentException("Invalid or conflicting Workshop dependency identity");
        return new WorkshopDependencyTarget(name, parent, childId, childTitle);
    }

    internal void Validate(WorkshopDependencyItem item, bool child)
    {
        ulong expectedId = child ? ChildId : ParentId;
        bool titleMatches = !string.IsNullOrWhiteSpace(item.Title) && (child ? item.Title == ChildTitle
            : ParentName == "OniMcp" ? item.Title.Contains("OniMcp", StringComparison.OrdinalIgnoreCase)
                || item.Title.Contains("ONI MCP Server", StringComparison.OrdinalIgnoreCase)
            : item.Title.Contains(ParentName, StringComparison.OrdinalIgnoreCase));
        if (item.Id != expectedId || item.CreatorAppId != CreatorAppId || item.ConsumerAppId != ConsumerAppId
            || item.Owner != Owner || item.Banned || !item.Public || !item.Community || !titleMatches
            || item.FileHandle is 0 or ulong.MaxValue || item.FileBytes <= 0
            || item.Children.Count > 4096 || item.Children.Contains(item.Id)
            || item.Children.Any(id => id is 0 or ulong.MaxValue) || item.Children.Distinct().Count() != item.Children.Count)
            throw new InvalidOperationException($"Workshop dependency {(child ? "child" : "parent")} identity check failed for {expectedId}");
    }
}

internal sealed record WorkshopDependencyItem(ulong Id, uint CreatorAppId, uint ConsumerAppId,
    ulong Owner, string Title, bool Banned, bool Public, bool Community, ulong FileHandle, long FileBytes,
    IReadOnlyList<ulong> Children);
internal sealed record WorkshopDependencyCallback(bool Success, ulong ParentId, ulong ChildId, string Result);
internal sealed record WorkshopDependencyEvent(string Phase, ulong ParentId, ulong ChildId,
    bool? Present = null, string? Result = null, IReadOnlyList<ulong>? Children = null);

// This boundary deliberately has no content-upload operation.
internal interface IWorkshopDependencyApi
{
    WorkshopDependencyItem Query(ulong id);
    WorkshopDependencyCallback Add(ulong parent, ulong child);
}

internal static class WorkshopDependency
{
    internal static bool Run(WorkshopDependencyTarget target, IWorkshopDependencyApi api, bool write,
        Action beforeWrite, Action<WorkshopDependencyEvent> report)
    {
        var parent = api.Query(target.ParentId);
        var child = api.Query(target.ChildId);
        target.Validate(parent, child: false);
        target.Validate(child, child: true);
        if (child.Children.Contains(target.ParentId))
            throw new InvalidOperationException("Jlib already depends on this parent; refusing a dependency cycle");
        bool present = parent.Children.Contains(target.ChildId);
        report(new("baseline", target.ParentId, target.ChildId, present, Children: parent.Children));
        if (present)
        {
            report(new("verified-existing", target.ParentId, target.ChildId, true));
            return false;
        }
        if (!write) throw new InvalidOperationException("Jlib dependency is absent; verification made no changes");

        // Persist intent before the only write. An uncertain prior attempt must
        // be inspected with verify, rather than silently submitted again.
        beforeWrite();
        report(new("add-intent", target.ParentId, target.ChildId, false));
        try
        {
            var callback = api.Add(target.ParentId, target.ChildId);
            report(new("add-callback", callback.ParentId, callback.ChildId, Result: callback.Result));
            if (!callback.Success || callback.ParentId != target.ParentId || callback.ChildId != target.ChildId)
                throw new InvalidOperationException("AddDependency callback failed or returned different parent/child IDs");
            var after = api.Query(target.ParentId);
            var childAfter = api.Query(target.ChildId);
            target.Validate(after, child: false);
            target.Validate(childAfter, child: true);
            if (childAfter.Children.Contains(target.ParentId))
                throw new InvalidOperationException("Jlib dependency readback contains a dependency cycle");
            present = after.Children.Contains(target.ChildId);
            report(new("readback", target.ParentId, target.ChildId, present, Children: after.Children));
            if (!present || parent.Children.Except(after.Children).Any())
                throw new InvalidOperationException("Workshop dependency readback is missing the child or a prior dependency");
            if (parent.FileHandle != after.FileHandle || parent.FileBytes != after.FileBytes
                || child.FileHandle != childAfter.FileHandle || child.FileBytes != childAfter.FileBytes)
                throw new InvalidOperationException("Workshop file identity changed during dependency update");
            report(new("verified-added", target.ParentId, target.ChildId, true));
            return true;
        }
        catch
        {
            // A missing/failed callback is ambiguous. Gather one fresh read if
            // possible, but preserve failure and never repeat AddDependency.
            try
            {
                var observed = api.Query(target.ParentId);
                target.Validate(observed, child: false);
                report(new("uncertain-readback", target.ParentId, target.ChildId,
                    observed.Children.Contains(target.ChildId), Children: observed.Children));
            }
            catch (Exception error)
            { report(new("uncertain-readback-failed", target.ParentId, target.ChildId, Result: error.GetType().Name)); }
            throw;
        }
    }
}

internal sealed record WorkshopDependencyOptions(bool Write, string Parent, ulong? ParentId, string PlanPath, string PlanSha256)
{
    internal static readonly string[] Modes = ["--add-jlib-dependency", "--verify-jlib-dependency"];
    internal static bool IsMode(string[] args) => args.Any(value => Modes.Contains(value, StringComparer.Ordinal)
        || value is "--parent" or "--parent-workshop-id" or "--jlib-plan");

    internal static WorkshopDependencyOptions Parse(string[] args)
    {
        if (args.Count(value => Modes.Contains(value, StringComparer.Ordinal)) != 1)
            throw new ArgumentException("Select exactly one Jlib dependency mode");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new[] { "--parent", "--parent-workshop-id", "--jlib-plan", "--expected-plan-sha256" };
        for (int index = 0; index < args.Length; index++)
        {
            if (Modes.Contains(args[index], StringComparer.Ordinal)) continue;
            string name = args[index];
            if (!names.Contains(name, StringComparer.Ordinal) || ++index >= args.Length
                || args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(name, args[index]))
                throw new ArgumentException("Dependency mode has an unknown, repeated or missing option");
        }
        string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Dependency mode requires " + key);
        string parent = Required("--parent");
        if (parent is not ("CycleTrim" or "OniMcp" or "OniOnline"))
            throw new ArgumentException("Unsupported dependency parent");
        ulong? parentId = values.TryGetValue("--parent-workshop-id", out var text)
            ? ulong.Parse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture) : null;
        if (parent == "OniOnline" && !parentId.HasValue)
            throw new ArgumentException("OniOnline requires its explicit published Workshop ID");
        string hash = Required("--expected-plan-sha256");
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("Dependency mode requires a reviewed Jlib plan SHA256");
        return new WorkshopDependencyOptions(args.Contains(Modes[0], StringComparer.Ordinal), parent,
            parentId, Path.GetFullPath(Required("--jlib-plan")), hash);
    }
}
