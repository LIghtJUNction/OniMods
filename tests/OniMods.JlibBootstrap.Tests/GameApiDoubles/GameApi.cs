using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public static class Object
    {
        public static int Destroyed;
        public static void Destroy(object value) { Destroyed++; }
    }
}

namespace HarmonyLib
{
    public sealed class Harmony
    {
        public string Id { get; }
        public readonly List<Assembly> Patched = new List<Assembly>();
        public Harmony(string id) { Id = id; }
        public void PatchAll(Assembly assembly) { Patched.Add(assembly); }
    }
}

namespace KMod
{
    public class UserMod2
    {
        public Assembly assembly { get; set; }
        public string path { get; set; }
        public Mod mod { get; set; }
        public virtual void OnLoad(HarmonyLib.Harmony harmony) { harmony.PatchAll(assembly); }
        public virtual void OnAllModsLoaded(HarmonyLib.Harmony harmony, IReadOnlyList<Mod> mods) { }
    }
    public sealed class Mod
    {
        public enum Status { NotInstalled, Installed }
        public string staticID;
        public Status status = Status.Installed;
        public bool Enabled = true;
        public string ContentPath;
        public LoadedModData loaded_mod_data = new LoadedModData();
        public bool IsEnabledForActiveDlc() { return Enabled; }
    }
    public sealed class LoadedModData
    {
        public ICollection<Assembly> dlls = new HashSet<Assembly>();
        public Dictionary<Assembly, UserMod2> userMod2Instances = new Dictionary<Assembly, UserMod2>();
    }
    public sealed class Manager { public List<Mod> mods = new List<Mod>(); }
}

public sealed class GameObject
{
    public T GetComponent<T>() where T : new() { return new T(); }
}
public sealed class Global
{
    public static Global Instance;
    public KMod.Manager modManager = new KMod.Manager();
    public GameObject globalCanvas = new GameObject();
    private readonly List<IEnumerator> routines = new List<IEnumerator>();
    public int StartedCoroutines;
    public void StartCoroutine(IEnumerator routine) { StartedCoroutines++; routines.Add(routine); }
    public void Frame()
    {
        foreach (var routine in routines.ToArray())
            if (!routine.MoveNext()) routines.Remove(routine);
    }
}
public sealed class MainMenu
{
    public static MainMenu Instance;
    public bool Active = true;
    public bool IsActive() { return Active; }
}
public sealed class ScreenPrefabs
{
    public static ScreenPrefabs Instance = new ScreenPrefabs();
    public ConfirmDialogScreen ConfirmDialogScreen = new ConfirmDialogScreen();
}
public sealed class ConfirmDialogScreen
{
    public GameObject gameObject = new GameObject();
    public static readonly List<string> Messages = new List<string>();
    public static bool FailNextPopup;
    public void PopupConfirmDialog(string text, Action on_confirm, Action on_cancel, string configurable_text = null,
        Action on_configurable_clicked = null, string title_text = null, string confirm_text = null,
        string cancel_text = null, object image_sprite = null)
    {
        if (FailNextPopup) { FailNextPopup = false; throw new InvalidOperationException("private native UI fixture details"); }
        Messages.Add(text);
    }
    public void Activate() { }
}
public static class KScreenManager
{
    public static GameObject AddChild(GameObject parent, GameObject prefab) { return new GameObject(); }
}
