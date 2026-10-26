using System;
using System.Collections.Generic;
using HarmonyLib;
using KMod;

public sealed class PayloadMod : UserMod2, Jlib.Runtime.IFixtureJlib
{
    static PayloadMod()
    { AppDomain.CurrentDomain.SetData("fixture.ctor.saw.registration", AppDomain.CurrentDomain.GetData("fixture.registered.assembly") != null); }
    public override void OnLoad(Harmony harmony)
    {
        AppDomain.CurrentDomain.SetData("fixture.onload", Count("fixture.onload") + 1);
        AppDomain.CurrentDomain.SetData("fixture.assembly", assembly);
        AppDomain.CurrentDomain.SetData("fixture.path", path);
        AppDomain.CurrentDomain.SetData("fixture.mod", mod);
        base.OnLoad(harmony);
    }
    public override void OnAllModsLoaded(Harmony harmony, IReadOnlyList<Mod> mods)
    {
        AppDomain.CurrentDomain.SetData("fixture.postload", Count("fixture.postload") + 1);
        AppDomain.CurrentDomain.SetData("fixture.mods", mods);
    }
    private static int Count(string key) { return (int)(AppDomain.CurrentDomain.GetData(key) ?? 0); }
}
