using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class StarterCrewRegression
{
    internal static void Run()
    {
        var screen = new MinionSelectScreen();
        foreach (string name in new[] { "Ada", "Mae", "Nikola" })
            screen.Candidates.Add(new CharacterContainer { Stats = new MinionStartingStats { Name = name } });
        screen.Candidates[0].Stats.personality = new TestPrintingResource("Ada");
        var slot = screen.Candidates[1];
        slot.RerollButton.Click = () => slot.Stats = new MinionStartingStats { Name = "Meep" };
        screen.Candidates[2].RerollButton.isInteractable = false;
        string[] embarkedCrew = null;
        screen.EmbarkButton.Click = () => embarkedCrew = screen.Candidates.Select(candidate => candidate.Stats.Name).ToArray();
        var previousGame = Game.Instance;
        Game.Instance = new Game();
        UnityEngine.Object.StarterScreen = screen;
        NewGamePauseWatcher.Starts = 0;
        try
        {
            var status = Status();
            var candidates = (JArray)status["candidates"];
            Require((int)status["startingCandidates"] == 3 && (bool)status["crewReady"] && candidates.Count == 3,
                "status retains crew readiness and describes every generated slot");
            Require((string)candidates[0]["model"] == "Standard" && candidates[2]["rerollChoiceId"] == null,
                "status describes the model and only advertises available rerolls on candidate rows");

            string reroll = (string)candidates[1]["rerollChoiceId"];
            var choice = new JObject { ["action"] = "new_game_select", ["name"] = reroll };
            Require(GameNewGameTools.Handle(choice).IsError, "reroll requires confirmation");
            choice["dryRun"] = true;
            choice["confirm"] = true;
            Require(!GameNewGameTools.Handle(choice).IsError && Names(Status()).SequenceEqual(new[] { "Ada", "Mae", "Nikola" })
                && NewGamePauseWatcher.Starts == 0, "preview leaves all slots and the opening watcher alone");
            choice.Remove("dryRun");
            Require(!GameNewGameTools.Handle(choice).IsError && Names(Status()).SequenceEqual(new[] { "Ada", "Meep", "Nikola" })
                && NewGamePauseWatcher.Starts == 0, "execution clicks only the advertised native reroll");

            choice["name"] = "embark";
            Require(!GameNewGameTools.Handle(choice).IsError && NewGamePauseWatcher.Starts == 1
                && embarkedCrew.SequenceEqual(new[] { "Ada", "Meep", "Nikola" }),
                "embark uses the resulting crew and starts the existing pause guard");
            choice["name"] = reroll;
            Game.Instance.baseAlreadyCreated = true;
            Require(GameNewGameTools.Handle(choice).IsError, "reroll requests outside the starter stage are rejected");
        }
        finally
        {
            UnityEngine.Object.StarterScreen = null;
            Game.Instance = previousGame;
            NewGamePauseWatcher.Starts = 0;
        }
        Console.WriteLine("PASS: starter crew inspection, native reroll routing, and embark guard");
    }

    private static JObject Status()
        => JObject.Parse(GameNewGameTools.Handle(new JObject { ["action"] = "new_game_status" }).Content[0].Text);

    private static string[] Names(JObject status)
        => status["candidates"].Select(candidate => (string)candidate["name"]).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
