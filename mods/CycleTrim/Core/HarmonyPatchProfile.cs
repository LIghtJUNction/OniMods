using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace CycleTrim.Core
{
    // Pinned to Harmony 2.4.2's publication lock and shared registry version 102.
    // Other providers can share registry storage while using a different lock.
    internal static class HarmonyPatchProfile
    {
        internal static object ReadPublicationLock()
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

        internal static Dictionary<MethodBase, byte[]> ReadRegistry(object publicationLock)
        {
            try
            {
                var type = typeof(Harmony).Assembly.GetType("HarmonyLib.HarmonySharedState", false);
                if (publicationLock == null || type == null)
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

        internal static bool CanSharePublicationLock(Assembly assembly, Assembly dynamicMethodHost)
        {
            if (ReferenceEquals(assembly, typeof(Harmony).Assembly))
                return true;
            try
            {
                // A growable assembly can add a provider after AssemblyLoad.
                // Only the CLR DynamicMethod host cannot acquire ordinary types.
                if (assembly.IsDynamic && !ReferenceEquals(assembly, dynamicMethodHost))
                    return false;
                var provider = assembly.GetType("HarmonyLib.PatchProcessor", false);
                return provider == null || provider == typeof(PatchProcessor);
            }
            catch (Exception)
            {
                // Metadata inspection must not fail another module's load;
                // an unreadable provider cannot establish a shared lock.
                return false;
            }
        }
    }
}
