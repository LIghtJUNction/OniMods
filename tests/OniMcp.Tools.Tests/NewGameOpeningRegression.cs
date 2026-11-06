using System;
using OniMcp.Tools;

internal static class NewGameOpeningRegression
{
    internal static void Run()
    {
        const string title = "Native localized mod differences title";
        Require(NewGameOpeningPolicy.IsInformationalModDifference("ConfirmDialogScreen", title, title, false, true),
            "the exact native informational mod-difference dialog may be acknowledged");
        foreach (string type in new[] { "OtherDialog", "DerivedConfirmDialogScreen", "StoryMessageScreen", null })
            Require(!NewGameOpeningPolicy.IsInformationalModDifference(type, title, title, false, true),
                "other screen types must never be automatically confirmed");
        foreach (string otherTitle in new[] { "Delete save", "Quit", "Restart", "Overwrite", "", null })
            Require(!NewGameOpeningPolicy.IsInformationalModDifference("ConfirmDialogScreen", otherTitle, title, false, true),
                "unrelated or missing dialog titles must never be automatically confirmed");
        Require(!NewGameOpeningPolicy.IsInformationalModDifference("ConfirmDialogScreen", title, title, true, true),
            "dialogs with any confirm, cancel or third-button callback must remain untouched");
        Require(!NewGameOpeningPolicy.IsInformationalModDifference("ConfirmDialogScreen", "", "", false, true),
            "missing native title metadata must fail closed");
        Require(!NewGameOpeningPolicy.IsInformationalModDifference("ConfirmDialogScreen", title, title, false, false),
            "confirmation must actually dismiss the informational dialog");

        var opening = new NewGameOpeningPolicy();
        Require(!opening.ObserveReady(0f, true, 3, false, false, false),
            "baseCreated and three duplicants must not complete before the native opening flow");
        Require(!opening.ObserveReady(20f, true, 3, false, true, false),
            "birth animations and the welcome screen must finish before MCP pauses");
        Require(!opening.ObserveReady(21f, true, 3, true, false, true),
            "a blocking confirmation must prevent readiness even after GameStarted");
        Require(!opening.ObserveReady(22f, true, 3, true, false, false),
            "HUD restoration must settle after native startup");
        Require(opening.ObserveReady(22.75f, true, 3, true, false, false),
            "closed opening screens and completed native startup must become ready after HUD restoration");
        Require(!opening.ObserveReady(23f, true, 3, true, true, false),
            "a reappearing opening screen must reset readiness");
        Require(!opening.ObserveReady(24f, true, 3, true, false, false),
            "readiness must settle again after a screen reappears");
        Require(!opening.ObserveReady(25f, true, 2, true, false, false),
            "an incomplete starter crew must prevent readiness");
        Require(!opening.ObserveReady(26f, false, 3, true, false, false),
            "base creation is still required");
        Console.WriteLine("PASS: bounded native new-game opening confirmation and readiness policy");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
