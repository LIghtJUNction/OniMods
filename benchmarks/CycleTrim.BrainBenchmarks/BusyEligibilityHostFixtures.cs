// Minimal host-only API fixtures for the linked production busy-chore patch.
// These do not emulate Unity, install Harmony patches, or prove ONI compatibility.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

internal class ComponentFixture
{
    private readonly Dictionary<Type, object> components = new Dictionary<Type, object>();
    internal int IdentityLookups { get; private set; }

    internal void AddComponent<T>(T component) where T : class
    {
        components[typeof(T)] = component;
    }

    public T GetComponent<T>() where T : class
    {
        if (typeof(T) == typeof(MinionIdentity))
        {
            IdentityLookups++;
        }
        return components.TryGetValue(typeof(T), out var component) ? (T)component : null;
    }
}

internal sealed class PickupableSensor : ComponentFixture { }
internal sealed class MinionIdentity { }

internal sealed class KPrefabID
{
    internal bool HasTag(object tag) { return false; }
}

internal static class GameTags
{
    internal static readonly object PreventChoreInterruption = new object();
}
internal sealed class NavGrid { }
internal sealed class ScheduleBlock { }
internal sealed class BrainScheduler { }

internal sealed partial class Navigator
{
    internal NavGrid NavGrid { get; set; }
    internal int CurrentNavType { get; set; }
    internal int flags { get; set; }
    internal int cachedCell { get; set; }
}

internal sealed class ChoreConsumer : ComponentFixture
{
    internal ChoreDriver choreDriver { get; set; }
    internal ChoreConsumerState consumerState { get; set; }
}

internal sealed class ChoreConsumerState
{
    internal Navigator navigator { get; set; }
    internal Schedulable schedulable { get; set; }
}

internal sealed class ChoreDriver
{
    internal Chore CurrentChore { get; set; }
    internal Chore GetCurrentChore() { return CurrentChore; }
}

internal class Chore
{
    internal static class Precondition
    {
        internal struct Context
        {
            internal int Marker;
        }
    }
}

internal sealed class IdleChore : Chore { }

internal sealed class Schedulable
{
    internal Schedule GetSchedule() { return null; }
}

internal sealed class Schedule
{
    internal ScheduleBlock GetCurrentScheduleBlock() { return null; }
}

internal sealed class Brain : ComponentFixture
{
    internal ChoreConsumer choreConsumer = null;
}

namespace CycleTrim.Patches
{
    internal static class NavigationInvalidationVersions
    {
        internal static int Get(NavGrid grid) { return 0; }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute
    {
        internal HarmonyPatch() { }
        internal HarmonyPatch(Type type, string methodName) { }
    }

    internal static class AccessTools
    {
        internal delegate ref TField FieldRef<TInstance, TField>(TInstance instance);

        internal static FieldRef<TInstance, TField> FieldRefAccess<TInstance, TField>(string name)
        {
            var field = typeof(TInstance).GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(TField))
                throw new InvalidOperationException("Unexpected host field reference: " + name);
            var getter = new DynamicMethod("HostField_" + name, typeof(TField).MakeByRefType(),
                new[] { typeof(TInstance) }, typeof(AccessTools).Module, true);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, field);
            il.Emit(OpCodes.Ret);
            return (FieldRef<TInstance, TField>)getter.CreateDelegate(typeof(FieldRef<TInstance, TField>));
        }

        internal static Type TypeByName(string name) { return null; }

        internal static MethodInfo Method(Type type, string name, Type[] parameters = null)
        {
            return parameters == null
                ? type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                : type.GetMethod(name, parameters);
        }
    }
}
