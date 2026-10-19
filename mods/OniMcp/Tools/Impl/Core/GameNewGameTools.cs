using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Klei.CustomSettings;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class GameNewGameTools
    {
        private sealed class Choice
        {
            internal string Id;
            internal bool Enabled;
            internal System.Action Click;
        }

        internal static CallToolResult Handle(JObject args)
        {
            string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
            KScreen screen = CurrentScreen();
            string stage = Stage(screen);
            var choices = Choices(screen);
            if (action == "new_game_status")
                return Status(screen, stage, choices);

            if (action == "new_game")
            {
                if (stage != "main_menu")
                    return CallToolResult.Error("New game requires the main menu. Use save/quit target=menu first; an existing colony is never discarded here.");
                var menu = (MainMenu)screen;
                var button = Field<KButton>(menu, "Button_NewGame");
                if (!Usable(button) || !button.isInteractable)
                    return CallToolResult.Error("Native New Game button is not available");
                if (ToolUtil.GetBool(args, "dryRun", false))
                    return Preview(stage, "new_game");
                if (!ToolUtil.GetBool(args, "confirm", false))
                    return CallToolResult.Error("confirm=true is required");
                button.SignalClick(KKeyCode.Mouse0);
                return Scheduled(stage, "new_game");
            }

            if (action != "new_game_select")
                return CallToolResult.Error("Use new_game, new_game_status, or new_game_select");
            string id = args["name"]?.ToString();
            Choice choice = choices.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (choice == null || !choice.Enabled)
                return CallToolResult.Error("Choice is not available on the current screen. Read new_game_status and use an advertised choice id as name.");
            if ((stage == "destination" || stage == "characters") && !SurvivalSettings())
                return CallToolResult.Error("World generation requires native Survival mode with SandboxMode=Disabled");
            if (stage == "characters" && !StarterCrewReady((MinionSelectScreen)screen))
                return CallToolResult.Error("Wait for exactly three native starting candidates and an enabled Embark button");
            if (ToolUtil.GetBool(args, "dryRun", false))
                return Preview(stage, choice.Id);
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required");

            if (stage == "characters" && !NewGamePauseWatcher.Begin(Game.Instance))
                return CallToolResult.Error("New-game pause guard is unavailable or already active; no Embark was submitted");
            choice.Click();
            return Scheduled(stage, choice.Id);
        }

        private static KScreen CurrentScreen()
        {
            // Native starter screens can be shown without KScreen.Activate().
            // Select the visible starter instance, rather than a prefab or a hidden screen.
            var crew = UnityEngine.Object.FindObjectsByType<MinionSelectScreen>(FindObjectsSortMode.None)
                .FirstOrDefault(item => Usable(item) && item.IsStarterMinion
                    && Usable(Field<KButton>(item, "proceedButton")));
            if (crew != null && Game.Instance != null && !Game.Instance.baseAlreadyCreated)
                return crew;
            if (Game.Instance != null)
                return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<NewGameFlow>();
            var current = flow == null ? null : Field<NewGameFlowScreen>(flow, "currentScreen");
            if (Usable(current) && current.IsActive())
                return current;
            var menu = MainMenu.Instance;
            return Usable(menu) && menu.IsActive() ? menu : null;
        }

        private static string Stage(KScreen screen)
        {
            if (screen is MainMenu) return "main_menu";
            if (screen is ModeSelectScreen) return "mode";
            if (screen is ClusterCategorySelectionScreen) return "category";
            if (screen is ColonyDestinationSelectScreen) return "destination";
            if (screen is WorldGenScreen) return "world_generation";
            if (screen is MinionSelectScreen) return "characters";
            return Game.Instance == null ? "unavailable"
                : Game.Instance.baseAlreadyCreated ? "colony" : "starting_colony";
        }

        private static List<Choice> Choices(KScreen screen)
        {
            var result = new List<Choice>();
            if (screen is ModeSelectScreen)
            {
                AddToggle(result, "survival", Field<MultiToggle>(screen, "survivalButton"));
            }
            else if (screen is ClusterCategorySelectionScreen category)
            {
                AddToggle(result, "classic", category.classicStyle?.button);
                AddToggle(result, "spaced_out", category.spacedOutStyle?.button);
                AddToggle(result, "vanilla", category.vanillaStyle?.button);
            }
            else if (screen is ColonyDestinationSelectScreen)
                AddButton(result, "generate_world", Field<KButton>(screen, "launchButton"));
            else if (screen is MinionSelectScreen crew && crew.IsStarterMinion
                && Game.Instance != null && !Game.Instance.baseAlreadyCreated)
                AddButton(result, "embark", Field<KButton>(crew, "proceedButton"));
            return result;
        }

        private static void AddButton(List<Choice> choices, string id, KButton button)
        {
            if (!Usable(button)) return;
            choices.Add(new Choice
            {
                Id = id, Enabled = button.isInteractable,
                Click = () => button.SignalClick(KKeyCode.Mouse0)
            });
        }

        private static void AddToggle(List<Choice> choices, string id, MultiToggle button)
        {
            if (!Usable(button)) return;
            choices.Add(new Choice
            {
                Id = id, Enabled = button.onClick != null,
                Click = () => button.onClick?.Invoke()
            });
        }

        private static bool Usable(Component component)
        {
            return component != null && component.gameObject.activeInHierarchy
                && (!(component is Behaviour behaviour) || behaviour.enabled);
        }

        private static T Field<T>(object instance, string name) where T : class
        {
            return instance == null ? null : AccessTools.Field(instance.GetType(), name)?.GetValue(instance) as T;
        }

        private static List<CharacterContainer> Crew(MinionSelectScreen screen)
        {
            var containers = Field<IList>(screen, "containers");
            return containers == null ? new List<CharacterContainer>()
                : containers.OfType<CharacterContainer>().ToList();
        }

        private static bool StarterCrewReady(MinionSelectScreen screen)
        {
            var crew = Crew(screen);
            return screen.IsStarterMinion && Game.Instance != null && !Game.Instance.baseAlreadyCreated
                && crew.Count == 3 && crew.All(item => item.Stats != null);
        }

        private static bool SurvivalSettings()
        {
            var settings = CustomGameSettings.Instance;
            return settings != null && settings.customGameMode == CustomGameSettings.CustomGameMode.Survival
                && settings.GetCurrentQualitySetting(CustomGameSettingConfigs.SandboxMode)?.id == "Disabled";
        }

        private static CallToolResult Status(KScreen screen, string stage, List<Choice> choices)
        {
            var result = new JObject
            {
                ["stage"] = stage,
                ["screen"] = screen?.GetType().Name,
                ["colonyLoaded"] = Game.Instance != null,
                ["baseAlreadyCreated"] = Game.Instance != null && Game.Instance.baseAlreadyCreated,
                ["liveDuplicants"] = Game.Instance == null ? 0 : Components.LiveMinionIdentities.Count,
                ["isPaused"] = SpeedControlScreen.Instance == null ? (JToken)JValue.CreateNull() : new JValue(SpeedControlScreen.Instance.IsPaused),
                ["pauseAfterStart"] = NewGamePauseWatcher.Status,
                ["choices"] = new JArray(choices.Select(item => new JObject
                {
                    ["id"] = item.Id, ["enabled"] = item.Enabled
                })),
                ["next"] = stage == "main_menu"
                    ? "launch/new_game dryRun=true, then confirm=true"
                    : stage == "colony" ? "Verify baseAlreadyCreated, liveDuplicants=3 and isPaused; then read /active/colony/status.oni"
                    : stage == "world_generation" || stage == "starting_colony" || stage == "unavailable"
                    ? "Wait for the native transition, then read launch/new_game_status"
                    : "launch/new_game_select name=<advertised id> dryRun=true, then confirm=true; inspect status after each transition"
            };
            if (screen is ColonyDestinationSelectScreen && CustomGameSettings.Instance != null)
            {
                result["cluster"] = CustomGameSettings.Instance.GetCurrentQualitySetting(CustomGameSettingConfigs.ClusterLayout)?.id;
                result["survivalWithoutSandbox"] = SurvivalSettings();
            }
            if (screen is MinionSelectScreen crew)
            {
                result["startingCandidates"] = Crew(crew).Count;
                result["crewReady"] = StarterCrewReady(crew);
            }
            return CallToolResult.Text(result.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static CallToolResult Preview(string stage, string choice)
        {
            return CallToolResult.Text(new JObject
            {
                ["dryRun"] = true, ["stage"] = stage, ["choice"] = choice, ["wouldApply"] = true
            }.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static CallToolResult Scheduled(string stage, string choice)
        {
            return CallToolResult.Text(new JObject
            {
                ["scheduled"] = true, ["fromStage"] = stage, ["choice"] = choice,
                ["next"] = "Read launch/new_game_status; menu and world-generation transitions are asynchronous."
            }.ToString(Newtonsoft.Json.Formatting.None));
        }
    }
}
