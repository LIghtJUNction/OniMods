using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Sdk;

internal static class ModSyncDeclarationTests
{
    // Run in its own host: declaration freezing intentionally lasts for the process lifetime.
    internal static void Run(System.Action<bool, string> check)
    {
        ModSyncDeclarations.Register("test-only-overlay", ModSyncPolicy.ClientOnly);
        ModSyncDeclarations.Register("test-only-overlay", ModSyncPolicy.ClientOnly);
        ModSyncDeclarations.RegisterContentExtensions("test-only-overlay", new[] { ".custom", ".assetx", ".custom" });
        check(Throws(() => ModSyncDeclarations.Register("test-only-overlay", ModSyncPolicy.Optional)), "conflicting local declaration is rejected");
        check(Throws(() => ModSyncDeclarations.Register(ModSyncDeclarations.FrameworkModId, ModSyncPolicy.ClientOnly)), "framework policy cannot be downgraded");
        var snapshot = ModSyncDeclarations.Snapshot();
        check(snapshot["test-only-overlay"] == ModSyncPolicy.ClientOnly && snapshot[ModSyncDeclarations.FrameworkModId] == ModSyncPolicy.Required,
            "trusted local declarations are captured with required framework policy");
        var mutable = snapshot as IDictionary<string, ModSyncPolicy>;
        check(mutable != null && Throws(() => mutable.Add("injected", ModSyncPolicy.Optional)), "snapshot cannot mutate captured policy");
        var content = ModSyncDeclarations.ContentSnapshot();
        check(content.ContainsKey("test-only-overlay") && content["test-only-overlay"].SequenceEqual(new[] { ".assetx", ".custom" }),
            "custom content extensions are normalized, deduplicated and sorted");
        check(Throws(() => ModSyncDeclarations.RegisterContentExtensions("late-content", new[] { ".custom" })),
            "content declarations freeze with the policy snapshot");
        check(Throws(() => ModSyncDeclarations.Register("registered-after-capture", ModSyncPolicy.Optional)), "declarations freeze before connection or confirmation");
        check(Throws(() => ModSyncDeclarations.Register("", ModSyncPolicy.Required)), "invalid declaration identity is rejected");
        check(Throws(() => ModSyncDeclarations.RegisterContentExtensions("bad-extension", new[] { "png" })),
            "content declarations require extension identities");
        check(Throws(() => ModSyncDeclarations.RegisterContentExtensions("bad-extension-dot", new[] { ".foo.bar" })),
            "content declarations reject multi-suffix identities");
        check(Throws(() => ModSyncDeclarations.RegisterContentExtensions("bad-extension-path", new[] { ".foo/bar" })),
            "content declarations reject path separators");
    }
    private static bool Throws(System.Action action)
    { try { action(); return false; } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; } catch (NotSupportedException) { return true; } }
}
