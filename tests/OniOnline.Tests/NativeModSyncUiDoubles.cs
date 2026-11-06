using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OniOnline.Core;

// Minimal controls for executing the production dialog's callbacks in the host.
// They do not model Unity rendering, native pause ownership or input handling.
namespace UnityEngine
{
    internal class Object
    {
        internal static void Destroy(GameObject value) { value.Destroyed = true; }
    }

    internal sealed class GameObject : Object
    {
        private readonly object component;
        internal string name;
        internal bool Active = true, Destroyed;
        internal GameObject(object component = null) { this.component = component; }
        internal void SetActive(bool value) { Active = value; }
        internal T GetComponent<T>() where T : class { return component as T; }
    }
}

namespace HarmonyLib
{
    internal static class AccessTools
    {
        internal static FieldInfo Field(Type type, string name)
        { return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
    }
}

namespace OniOnline.Runtime
{
    internal static class OnlineText
    {
        internal static string Get(string key)
        { return Catalog.Get(key, "en-US"); }

        private static readonly TextCatalog Catalog = new TextCatalog(File.ReadLines(
            Path.Combine(Directory.GetCurrentDirectory(), "mods/OniOnline/localization/menu-draft.csv")));
    }
}

internal sealed class KButton
{
    internal System.Action OnClick;
    internal void ClearOnClick() { OnClick = null; }
}

internal sealed class LocText
{
    internal bool richText = true;
    internal string Text;
    internal void SetText(string value) { Text = value; }
}

internal sealed class ConfirmDialogScreen
{
    private readonly UnityEngine.GameObject confirmButton = new UnityEngine.GameObject(new KButton());
    private readonly UnityEngine.GameObject cancelButton = new UnityEngine.GameObject(new KButton());
    private readonly UnityEngine.GameObject configurableButton = new UnityEngine.GameObject(new KButton());
    private readonly LocText popupMessage = new LocText();
    internal readonly UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    private bool active = true;
    internal bool deactivateOnConfirmAction, deactivateOnCancelAction, deactivateOnConfigurableAction;
    internal string ConfirmText;
    internal int RenderCount;
    internal KButton Confirm { get { return confirmButton.GetComponent<KButton>(); } }
    internal KButton Configure { get { return configurableButton.GetComponent<KButton>(); } }
    internal KButton Cancel { get { return cancelButton.GetComponent<KButton>(); } }
    internal LocText Body { get { return popupMessage; } }
    internal bool IsActive() { return active; }
    internal bool IsScreenActive() { return active && gameObject.Active; }
    internal void Deactivate() { active = false; gameObject.Destroyed = true; }

    internal void PopupConfirmDialog(string text, System.Action confirm, System.Action cancel,
        string configureText, System.Action configure, string title, string confirmText, string cancelText)
    {
        RenderCount++;
        popupMessage.SetText(text);
        ConfirmText = confirmText;
        Confirm.OnClick += confirm;
        Configure.OnClick += configure;
        Cancel.OnClick += cancel;
    }
}

internal sealed class ScreenPrefabs
{
    internal static ScreenPrefabs Instance = new ScreenPrefabs();
    internal readonly ConfirmDialogScreen ConfirmDialogScreen = new ConfirmDialogScreen();
}

internal sealed class KScreenManager
{
    internal static KScreenManager Instance = new KScreenManager();
    internal readonly List<ConfirmDialogScreen> Created = new List<ConfirmDialogScreen>();
    internal ConfirmDialogScreen StartScreen(UnityEngine.GameObject prefab, UnityEngine.GameObject root)
    {
        var screen = new ConfirmDialogScreen();
        Created.Add(screen);
        return screen;
    }
}

internal sealed class Global
{
    internal static Global Instance = new Global();
    internal readonly UnityEngine.GameObject globalCanvas = new UnityEngine.GameObject();
}
