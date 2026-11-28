using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityEngine
{
    internal sealed partial class GameObject
    {
        internal bool activeInHierarchy { get; set; } = true;
    }

    internal class Component
    {
        internal GameObject gameObject { get; } = new GameObject();
    }

    internal class Behaviour : Component
    {
        internal bool enabled { get; set; } = true;
    }

    internal enum FindObjectsSortMode { None }

    internal static partial class Object
    {
        internal static MinionSelectScreen StarterScreen { get; set; }
        internal static T[] FindObjectsByType<T>(FindObjectsSortMode _)
            => StarterScreen is T screen ? new[] { screen } : Array.Empty<T>();
        internal static T FindFirstObjectByType<T>() where T : class => null;
    }
}

namespace HarmonyLib
{
    internal static class AccessTools
    {
        internal static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field;
            }
            return null;
        }
    }
}

namespace Klei.CustomSettings
{
    internal sealed class CustomGameSettings
    {
        internal enum CustomGameMode { Survival }
        internal static CustomGameSettings Instance { get; } = new CustomGameSettings();
        internal CustomGameMode customGameMode => CustomGameMode.Survival;
        internal (string id, int unused)? GetCurrentQualitySetting(string option) => ("Disabled", 0);
    }

    internal static class CustomGameSettingConfigs
    {
        internal const string SandboxMode = "SandboxMode";
        internal const string ClusterLayout = "ClusterLayout";
    }
}

namespace OniMcp.Tools
{
    internal static class NewGamePauseWatcher
    {
        internal static int Starts { get; set; }
        internal static string Status => "idle";
        internal static bool Begin(Game _) { Starts++; return true; }
    }
}

internal sealed partial class Game
{
    internal bool baseAlreadyCreated { get; set; }
}

internal class KScreen : Behaviour
{
    internal bool IsActive() => gameObject.activeInHierarchy;
}
internal sealed class MainMenu : KScreen { internal static MainMenu Instance => null; }
internal sealed class ModeSelectScreen : KScreen { }
internal sealed class ClusterCategorySelectionScreen : KScreen
{
    internal sealed class Style { internal MultiToggle button => null; }
    internal Style classicStyle => null;
    internal Style spacedOutStyle => null;
    internal Style vanillaStyle => null;
}
internal sealed class ColonyDestinationSelectScreen : KScreen { }
internal sealed class WorldGenScreen : KScreen { }
internal sealed class NewGameFlow { }
internal sealed class NewGameFlowScreen : KScreen { }
internal sealed class MinionSelectScreen : KScreen
{
    private readonly List<CharacterContainer> containers = new List<CharacterContainer>();
    private readonly KButton proceedButton = new KButton();
    internal bool IsStarterMinion => true;
    internal List<CharacterContainer> Candidates => containers;
    internal KButton EmbarkButton => proceedButton;
}
internal enum KKeyCode { Mouse0 }
internal sealed class KButton : Behaviour
{
    internal Action Click { get; set; }
    internal bool isInteractable { get; set; } = true;
    internal void SignalClick(KKeyCode _) => Click?.Invoke();
}
internal sealed class MultiToggle : Behaviour { internal Action onClick => null; }
