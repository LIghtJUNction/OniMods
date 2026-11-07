internal static class WorkshopDependencyTests
{
    private const ulong ChildId = 3808000001;
    private static readonly WorkshopDependencyTarget Target = WorkshopDependencyTarget.Create("CycleTrim", ChildId, "Jlib");

    internal static void Run()
    {
        var api = new FakeApi();
        int intents = 0;
        var events = new List<WorkshopDependencyEvent>();
        Check(WorkshopDependency.Run(Target, api, true, () => intents++, events.Add), "new relationship is written");
        Check(api.Adds == 1 && intents == 1 && api.Parent.Children.Contains(ChildId)
            && api.Parent.Children.Contains(77UL) && events[^1].Phase == "verified-added", "add once and preserve unrelated children");
        Check(!WorkshopDependency.Run(Target, api, true, () => intents++, events.Add)
            && api.Adds == 1 && intents == 1, "existing relationship is idempotent without another intent or write");
        Check(!WorkshopDependency.Run(Target, api, false, () => throw new Exception("unexpected write"), events.Add), "verify is read-only");
        api = new FakeApi();
        Fails(() => WorkshopDependency.Run(Target, api, false, () => intents++, events.Add));
        Check(api.Adds == 0, "missing verification never adds");

        foreach (var invalid in new Func<WorkshopDependencyItem, WorkshopDependencyItem>[]
        {
            item => item with { Id = item.Id + 1 }, item => item with { CreatorAppId = 1 },
            item => item with { ConsumerAppId = 1 }, item => item with { Owner = 1 },
            item => item with { Banned = true }, item => item with { Public = false },
            item => item with { Community = false }, item => item with { Title = "another mod" },
            item => item with { FileHandle = 0 }, item => item with { FileHandle = ulong.MaxValue },
            item => item with { FileBytes = 0 }, item => item with { Children = [0] },
            item => item with { Children = [77, 77] }, item => item with { Children = [item.Id] },
            item => item with { Children = Enumerable.Range(1, 4097).Select(value => (ulong)value).ToArray() },
        })
        {
            foreach (bool child in new[] { false, true })
            {
                api = new FakeApi();
                if (child) api.Child = invalid(api.Child); else api.Parent = invalid(api.Parent);
                Fails(() => WorkshopDependency.Run(Target, api, true, () => intents++, events.Add));
                Check(api.Adds == 0, "invalid parent/child is refused before writing");
            }
        }
        api = new FakeApi(); api.Child = api.Child with { Children = [Target.ParentId] };
        Fails(() => WorkshopDependency.Run(Target, api, true, () => intents++, events.Add));
        Check(api.Adds == 0, "reverse dependency is refused before adding a cycle");

        foreach (var callback in new[]
        {
            new WorkshopDependencyCallback(false, Target.ParentId, ChildId, "k_EResultFail"),
            new WorkshopDependencyCallback(true, Target.ParentId + 1, ChildId, "k_EResultOK"),
            new WorkshopDependencyCallback(true, Target.ParentId, ChildId + 1, "k_EResultOK"),
        })
        {
            api = new FakeApi { Callback = callback };
            events.Clear();
            Fails(() => WorkshopDependency.Run(Target, api, true, () => intents++, events.Add));
            Check(api.Adds == 1 && events.Any(e => e.Phase == "uncertain-readback")
                && !events.Any(e => e.Phase == "verified-added"), "failed or wrong callback is not retried or treated as success");
        }
        api = new FakeApi { AddError = new TimeoutException("callback timeout") };
        events.Clear();
        Fails(() => WorkshopDependency.Run(Target, api, true, () => intents++, events.Add));
        Check(api.Adds == 1 && events.Any(e => e.Phase == "uncertain-readback"), "callback timeout gets readback without retry");
        api = new FakeApi { QueryError = new TimeoutException("query timeout") };
        Fails(() => WorkshopDependency.Run(Target, api, true, () => intents++, events.Add));
        Check(api.Adds == 0, "failed initial query cannot be treated as an empty dependency list");
        api = new FakeApi { AfterAdd = fake => fake.QueryError = new IOException("readback failed") };
        events.Clear();
        Fails(() => WorkshopDependency.Run(Target, api, true, () => intents++, events.Add));
        Check(api.Adds == 1 && events.Any(e => e.Phase == "uncertain-readback-failed")
            && !events.Any(e => e.Phase == "verified-added"), "failed readback remains failure without retrying the write");

        foreach (var mutation in new Action<FakeApi>[]
        {
            fake => fake.Parent = fake.Parent with { Children = [77] },
            fake => fake.Parent = fake.Parent with { Children = [ChildId] },
            fake => fake.Parent = fake.Parent with { FileHandle = 999 },
            fake => fake.Parent = fake.Parent with { FileBytes = 999 },
            fake => fake.Child = fake.Child with { FileHandle = 999 },
            fake => fake.Child = fake.Child with { FileBytes = 999 },
            fake => fake.Child = fake.Child with { Public = false },
            fake => fake.Child = fake.Child with { Children = [Target.ParentId] },
        })
        {
            api = new FakeApi { AfterAdd = mutation };
            events.Clear();
            Fails(() => WorkshopDependency.Run(Target, api, true, () => intents++, events.Add));
            Check(api.Adds == 1 && !events.Any(e => e.Phase == "verified-added"), "readback drift cannot report success");
        }
        api = new FakeApi();
        Fails(() => WorkshopDependency.Run(Target, api, true, () => throw new IOException("prior intent"), events.Add));
        Check(api.Adds == 0, "retained write intent prevents a second attempt");
        Fails(() => WorkshopDependencyTarget.Create("Unknown", ChildId, "Jlib"));
        Fails(() => WorkshopDependencyTarget.Create("OniOnline", ChildId, "Jlib"));
        Fails(() => WorkshopDependencyTarget.Create("CycleTrim", ChildId, "Jlib", 3766318556));
        Fails(() => WorkshopDependencyTarget.Create("CycleTrim", Target.ParentId, "Jlib"));
        foreach (ulong invalidId in new[] { 0UL, ulong.MaxValue })
            Fails(() => WorkshopDependencyTarget.Create("CycleTrim", invalidId, "Jlib"));
        Check(WorkshopDependencyTarget.Create("OniOnline", ChildId, "Jlib", 3808000002).ParentId == 3808000002,
            "future OniOnline requires an explicit parent");
        Check(WorkshopDependencyTarget.Create("OniMcp", ChildId, "Jlib").ParentId == 3807166922,
            "OniMcp uses the replacement Legacy item");

        var args = new[] { "--add-jlib-dependency", "--parent", "CycleTrim", "--jlib-plan", "plan.json",
            "--expected-plan-sha256", new string('A', 64) };
        Check(WorkshopDependencyOptions.Parse(args).Write, "dependency CLI parses its exclusive write mode");
        Check(WorkshopDependencyOptions.IsMode(["--parent", "CycleTrim"]), "dependency-only options cannot leak into content upload");
        Fails(() => WorkshopDependencyOptions.Parse(["--parent", "CycleTrim"]));
        foreach (var extra in new[] { new[] { "--vdf", "other.vdf" }, new[] { "--prepare-first-item" },
            new[] { "--verify-jlib-dependency" }, new[] { "--parent", "OniMcp" }, new[] { "--parent-workshop-id" } })
            Fails(() => WorkshopDependencyOptions.Parse(args.Concat(extra).ToArray()));
        var verify = (string[])args.Clone(); verify[0] = "--verify-jlib-dependency";
        Check(!WorkshopDependencyOptions.Parse(verify).Write, "verification CLI cannot request a write");

        string directory = Path.Combine(Path.GetTempPath(), "onim-dependency-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = DependencyWriteJournal.Begin(directory, Target, new string('A', 64));
            byte[] before = File.ReadAllBytes(journal.Path);
            Fails(() => DependencyWriteJournal.Begin(directory, Target, new string('A', 64)));
            Check(before.SequenceEqual(File.ReadAllBytes(journal.Path)), "repeated intent preserves recovery bytes");
            using var intent = System.Text.Json.JsonDocument.Parse(File.ReadAllLines(journal.Path)[0]);
            Check(intent.RootElement.GetProperty("ParentId").GetUInt64() == Target.ParentId
                && intent.RootElement.GetProperty("ChildId").GetUInt64() == ChildId, "durable intent binds the exact parent-child pair");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        Console.WriteLine("Workshop Jlib dependency identity, one-shot writes, idempotence, callbacks and readback passed");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Fails(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or TimeoutException)
        { return; }
        throw new InvalidOperationException("An invalid dependency action was accepted");
    }

    private sealed class FakeApi : IWorkshopDependencyApi
    {
        internal WorkshopDependencyItem Parent = Item(Target.ParentId, "CycleTrim") with { Children = [77] };
        internal WorkshopDependencyItem Child = Item(ChildId, "Jlib");
        internal int Adds;
        internal WorkshopDependencyCallback? Callback;
        internal Exception? AddError;
        internal Exception? QueryError;
        internal Action<FakeApi>? AfterAdd;
        public WorkshopDependencyItem Query(ulong id)
        {
            if (QueryError != null) throw QueryError;
            return id == Target.ParentId ? Parent : Child;
        }
        public WorkshopDependencyCallback Add(ulong parent, ulong child)
        {
            Adds++;
            if (AddError != null) throw AddError;
            Parent = Parent with { Children = Parent.Children.Append(child).ToArray() };
            AfterAdd?.Invoke(this);
            return Callback ?? new(true, parent, child, "k_EResultOK");
        }
        private static WorkshopDependencyItem Item(ulong id, string title) => new(id,
            WorkshopDependencyTarget.CreatorAppId, WorkshopDependencyTarget.ConsumerAppId,
            WorkshopDependencyTarget.Owner, title, false, true, true, id + 10, 100, []);
    }
}
