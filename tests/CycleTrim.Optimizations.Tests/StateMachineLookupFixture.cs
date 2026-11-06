using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CycleTrim.Core;
using HarmonyLib;

// Synthetic API fixtures only. GetSMI reproduces the ordered first-assignable
// search contract, without loading or executing any game assembly.
public class StateMachine
{
    public class Instance
    {
        internal int Id;
    }
}

public class StateMachineController
{
    private List<StateMachine.Instance> stateMachines;

    internal StateMachineController(List<StateMachine.Instance> machines)
    {
        stateMachines = machines;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public StateMachine.Instance GetSMI(Type type)
    {
        for (int index = 0; index < stateMachines.Count; index++)
        {
            var instance = stateMachines[index];
            if (type.IsAssignableFrom(instance.GetType())) return instance;
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public T GetSMI<T>() where T : class
    {
        return GetSMI(typeof(T)) as T;
    }

    internal List<StateMachine.Instance> Machines
    {
        get { return stateMachines; }
        set { stateMachines = value; }
    }
}

namespace CycleTrim.Optimizations.Tests
{
    internal static class StateMachineLookupFixture
    {
        internal interface ITagged { }
        internal class Parent : StateMachine.Instance { }
        internal sealed class Leaf : Parent, ITagged { }
        internal sealed class Filler : StateMachine.Instance { }
        internal sealed class Missing : StateMachine.Instance { }

        internal static readonly MethodInfo Target = AccessTools.DeclaredMethod(
            typeof(StateMachineController), "GetSMI", new[] { typeof(Type) });
        private static bool originalInstalled;

        internal static void InstallOriginal()
        {
            if (originalInstalled) return;
            new Harmony("cycletrim.tests.smi.original").CreateReversePatcher(Target,
                new HarmonyMethod(typeof(StateMachineLookupFixture), "Original"))
                .Patch(HarmonyReversePatchType.Original);
            originalInstalled = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static StateMachine.Instance Original(StateMachineController controller, Type type)
        {
            throw new InvalidOperationException("Original state-machine lookup stand-in not installed");
        }

        internal static StateMachineController Controller(int count = 64, int match = 63)
        {
            var list = new List<StateMachine.Instance>(count);
            for (int index = 0; index < count; index++)
                list.Add(index == match ? (StateMachine.Instance)new Leaf { Id = index + 1 }
                    : new Filler { Id = index + 1 });
            return new StateMachineController(list);
        }

        internal sealed class CountingType : TypeDelegator
        {
            internal int Calls;
            internal Action Visiting;
            internal CountingType(Type type) : base(type) { }
            public override bool IsAssignableFrom(Type candidate)
            {
                Calls++;
                Visiting?.Invoke();
                return base.IsAssignableFrom(candidate);
            }
        }

        internal sealed class WorkProbe : IDisposable
        {
            private const string Owner = "cycletrim.tests.smi.work-probe";
            internal static long Requests, Hits, NormalReturns;

            internal WorkProbe()
            {
                var harmony = new Harmony(Owner);
                harmony.Patch(AccessTools.Method(typeof(StateMachineLookupCache), "TryGet"),
                    postfix: new HarmonyMethod(typeof(WorkProbe), "AfterLookup"));
                harmony.Patch(AccessTools.Method(typeof(StateMachineLookupCache), "Remember"),
                    postfix: new HarmonyMethod(typeof(WorkProbe), "AfterReturn"));
                Reset();
            }

            internal static void Reset() { Requests = Hits = NormalReturns = 0; }
            private static void AfterLookup(bool __result) { Requests++; if (__result) Hits++; }
            private static void AfterReturn() { NormalReturns++; }
            public void Dispose() { new Harmony(Owner).UnpatchAll(Owner); }
        }
    }
}
