using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;

namespace CycleTrim.Core
{
    internal enum HarmonyPatchSnapshotResult
    {
        UnsupportedLayout,
        Compatible,
        Reject
    }

    // Harmony 2.4.2 replaces its serialized patch bytes on each publication.
    // Contract: pardeike/Harmony@a264a1bf, PatchProcessor/HarmonySharedState.
    // Only byte identity is cached; callers still check on every reuse scope.
    internal sealed class HarmonyPatchSnapshot
    {
        private static readonly object PublicationLock = ReadPublicationLock();
        private static readonly Dictionary<MethodBase, byte[]> Registry = ReadRegistry();
        private static readonly Assembly DynamicMethodHost = new DynamicMethod(
            "CycleTrimPatchSnapshotHost", typeof(void), Type.EmptyTypes).Module.Assembly;
        private static volatile bool uniqueProvider = true;
        private readonly MethodBase[] methods;
        private readonly Func<MethodBase, bool> validate;
        private byte[][] observed;
        private bool compatible;

        internal HarmonyPatchSnapshot(MethodBase[] methods, Func<MethodBase, bool> validate)
        {
            if (methods == null)
                throw new ArgumentNullException(nameof(methods));
            if (validate == null)
                throw new ArgumentNullException(nameof(validate));
            this.methods = (MethodBase[])methods.Clone();
            this.validate = validate;
        }

        static HarmonyPatchSnapshot()
        {
            // Different providers may share the registry but have different
            // publication locks. Subscribe before inventory to cover late loads.
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                ObserveProvider(assembly);
        }

        internal HarmonyPatchSnapshotResult Check()
        {
            // Ambiguous providers and in-flight publication are unsafe, not
            // merely an unknown layout eligible for the caller's public scan.
            if (!uniqueProvider)
                return HarmonyPatchSnapshotResult.Reject;
            if (PublicationLock == null || Registry == null)
                return HarmonyPatchSnapshotResult.UnsupportedLayout;
            if (Monitor.IsEntered(PublicationLock))
                return HarmonyPatchSnapshotResult.Reject;

            byte[][] snapshot;
            lock (PublicationLock)
            lock (Registry)
            {
                if (!uniqueProvider)
                    return HarmonyPatchSnapshotResult.Reject;
                if (observed != null && Matches(observed))
                    return compatible ? HarmonyPatchSnapshotResult.Compatible
                        : HarmonyPatchSnapshotResult.Reject;
                snapshot = new byte[methods.Length][];
                for (var index = 0; index < methods.Length; index++)
                    Registry.TryGetValue(methods[index], out snapshot[index]);
            }

            // Public validation can resolve methods and load assemblies. Never
            // hold the publication/registry locks while running caller code.
            var result = true;
            foreach (var method in methods)
                if (!validate(method))
                {
                    result = false;
                    break;
                }
            lock (PublicationLock)
            lock (Registry)
            {
                if (!uniqueProvider || !Matches(snapshot))
                    return HarmonyPatchSnapshotResult.Reject;
                observed = snapshot;
                compatible = result;
                return result ? HarmonyPatchSnapshotResult.Compatible
                    : HarmonyPatchSnapshotResult.Reject;
            }
        }

        private bool Matches(byte[][] snapshot)
        {
            for (var index = 0; index < methods.Length; index++)
            {
                byte[] current;
                Registry.TryGetValue(methods[index], out current);
                if (!ReferenceEquals(current, snapshot[index]))
                    return false;
            }
            return true;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            ObserveProvider(args.LoadedAssembly);
        }

        private static void ObserveProvider(Assembly assembly)
        {
            if (ReferenceEquals(assembly, typeof(Harmony).Assembly))
                return;
            try
            {
                // A growable assembly can define a provider after its load
                // event. Only the exact CLR DynamicMethod host cannot do so.
                if (assembly.IsDynamic && !ReferenceEquals(assembly, DynamicMethodHost))
                {
                    uniqueProvider = false;
                    return;
                }
                var provider = assembly.GetType("HarmonyLib.PatchProcessor", false);
                if (provider != null && provider != typeof(PatchProcessor))
                    uniqueProvider = false;
            }
            catch (Exception)
            {
                // Provider discovery must not fail another module's load.
                uniqueProvider = false;
            }
        }

        private static object ReadPublicationLock()
        {
            try
            {
                if (typeof(Harmony).Assembly.GetName().Version != new Version(2, 4, 2, 0))
                    return null;
                var field = typeof(PatchProcessor).GetField("locker", BindingFlags.Static | BindingFlags.NonPublic);
                return field != null && field.IsInitOnly && field.FieldType == typeof(object)
                    ? field.GetValue(null) : null;
            }
            catch (MemberAccessException) { return null; }
            catch (TypeInitializationException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        private static Dictionary<MethodBase, byte[]> ReadRegistry()
        {
            try
            {
                var type = typeof(Harmony).Assembly.GetType("HarmonyLib.HarmonySharedState", false);
                if (PublicationLock == null || type == null)
                    return null;
                const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                var state = type.GetField("state", flags);
                var declared = type.GetField("internalVersion", flags);
                var actual = type.GetField("actualVersion", flags);
                if (state == null || !state.IsInitOnly || state.FieldType != typeof(Dictionary<MethodBase, byte[]>)
                    || declared == null || !declared.IsLiteral || !Equals(declared.GetRawConstantValue(), 102)
                    || actual == null || !actual.IsInitOnly || actual.FieldType != typeof(int)
                    || !Equals(actual.GetValue(null), 102))
                    return null;
                return (Dictionary<MethodBase, byte[]>)state.GetValue(null);
            }
            catch (MemberAccessException) { return null; }
            catch (TypeInitializationException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }
    }
}
