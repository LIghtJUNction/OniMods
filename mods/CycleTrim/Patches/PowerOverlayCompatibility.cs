using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;

namespace CycleTrim.Patches
{
    // Interface implementations can change state even without patching Power or
    // CircuitManager. Reuse is supported only with the game's implementations.
    internal static class PowerOverlayCompatibility
    {
        private static readonly object ScanLock = new object();
        private static readonly Assembly GameAssembly = typeof(CircuitManager).Assembly;
        // The CLR's anonymous DynamicMethod host cannot acquire ordinary types.
        // Other dynamic assemblies can grow after AssemblyLoad and are unsupported.
        private static readonly Assembly DynamicMethodHost = new DynamicMethod(
            "CycleTrimPowerOverlayHost", typeof(void), Type.EmptyTypes).Module.Assembly;
        private static int assemblyVersion;
        private static int scannedVersion = -1;
        private static bool supported;
        private static MethodBase[] callbacks = new MethodBase[0];

        static PowerOverlayCompatibility()
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        internal static int CurrentVersion { get { return Volatile.Read(ref assemblyVersion); } }

        internal static bool IsCurrent(int version)
        {
            return version == Volatile.Read(ref assemblyVersion);
        }

        internal static bool CanReuse(out int version)
        {
            version = Volatile.Read(ref assemblyVersion);
            if (Volatile.Read(ref scannedVersion) != version)
            {
                lock (ScanLock)
                {
                    if (scannedVersion != version)
                    {
                        MethodBase[] methods;
                        supported = Scan(out methods);
                        callbacks = methods;
                        Volatile.Write(ref scannedVersion, version);
                    }
                }
            }

            if (!supported || !IsCurrent(version))
                return false;
            foreach (var method in callbacks)
            {
                var patches = Harmony.GetPatchInfo(method);
                if (patches != null && (patches.Prefixes.Count != 0
                    || patches.Postfixes.Count != 0 || patches.Transpilers.Count != 0
                    || patches.Finalizers.Count != 0))
                    return false;
            }
            return IsCurrent(version);
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            Interlocked.Increment(ref assemblyVersion);
        }

        private static bool Scan(out MethodBase[] methods)
        {
            methods = new MethodBase[0];
            var guarded = new HashSet<MethodBase>();
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var dependencies = new Dictionary<Assembly, AssemblyName[]>();
                var relevantNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    GameAssembly.GetName().Name
                };
                foreach (var assembly in assemblies)
                {
                    if (ReferenceEquals(assembly, DynamicMethodHost))
                        continue;
                    if (assembly.IsDynamic)
                        return false;
                    dependencies.Add(assembly, assembly.GetReferencedAssemblies());
                }

                // Follow references backwards from the game, including indirect
                // subclasses in another mod. GetTypes on unrelated framework
                // assemblies would itself load many irrelevant dependencies.
                // A still-unloaded base assembly must load before an instance
                // can be created; AssemblyLoad then invalidates the lease.
                bool expanded;
                do
                {
                    expanded = false;
                    foreach (var entry in dependencies)
                    {
                        var name = entry.Key.GetName().Name;
                        if (relevantNames.Contains(name))
                            continue;
                        foreach (var reference in entry.Value)
                        {
                            if (relevantNames.Contains(reference.Name))
                            {
                                expanded |= relevantNames.Add(name);
                                break;
                            }
                        }
                    }
                } while (expanded);

                foreach (var assembly in assemblies)
                {
                    if (!relevantNames.Contains(assembly.GetName().Name))
                        continue;
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface)
                            continue;
                        if (!InspectInterface(type, typeof(IEnergyConsumer), guarded)
                            || !InspectInterface(type, typeof(IBridgedNetworkItem), guarded)
                            || !InspectInterface(type, typeof(ICircuitConnected), guarded))
                            return false;
                    }
                }

                // A transformer resolves its circuit through these methods as
                // well as reading its rating. A patch anywhere along that path
                // can make two otherwise identical demand queries differ.
                var networks = typeof(UtilityNetworkManager<ElectricalUtilityNetwork, Wire>);
                if (!AddGetter(guarded, typeof(Generator), "WattageRating", typeof(float))
                    || !AddGetter(guarded, typeof(Generator), "Efficiency", typeof(float))
                    || !AddGetter(guarded, typeof(Generator), "CircuitID", typeof(ushort))
                    || !AddGetter(guarded, typeof(Game), "Instance", typeof(Game), true)
                    || !AddMethod(guarded, typeof(CircuitManager), "GetCircuitID", typeof(ushort),
                        false, typeof(ICircuitConnected))
                    || !AddMethod(guarded, typeof(CircuitManager), "GetCircuitID", typeof(ushort),
                        false, typeof(int))
                    || !AddMethod(guarded, typeof(CircuitManager), "GetVirtualCircuitID", typeof(ushort),
                        false, typeof(object))
                    || !AddMethod(guarded, networks, "GetNetworkForCell", typeof(UtilityNetwork),
                        false, typeof(int))
                    || !AddMethod(guarded, networks, "GetNetworkForVirtualKey", typeof(UtilityNetwork),
                        false, typeof(object))
                    || !AddMethod(guarded, typeof(Grid), "IsValidCell", typeof(bool), true, typeof(int))
                    || !AddMethod(guarded, typeof(Klei.AI.AttributeInstance), "GetTotalValue", typeof(float),
                        false))
                    return false;
                methods = new MethodBase[guarded.Count];
                guarded.CopyTo(methods);
                return true;
            }
            catch (Exception error) when (error is ReflectionTypeLoadException
                || error is NotSupportedException || error is TypeLoadException
                || error is System.IO.FileNotFoundException || error is System.IO.FileLoadException
                || error is BadImageFormatException || error is System.Security.SecurityException)
            {
                // An incomplete type inventory is not evidence of compatibility.
                return false;
            }
        }

        private static bool InspectInterface(Type type, Type contract, HashSet<MethodBase> guarded)
        {
            if (!contract.IsAssignableFrom(type))
                return true;
            if (!ReferenceEquals(type.Assembly, GameAssembly))
                return false;
            var map = type.GetInterfaceMap(contract);
            for (var index = 0; index < map.InterfaceMethods.Length; index++)
            {
                var name = map.InterfaceMethods[index].Name;
                if (name == "get_WattsNeededWhenActive" || name == "GetNetworkCell"
                    || name == "IsConnectedToNetworks" || name == "get_IsVirtual"
                    || name == "get_PowerCell" || name == "get_VirtualCircuitKey")
                    guarded.Add(map.TargetMethods[index]);
            }
            return true;
        }

        private static bool AddGetter(HashSet<MethodBase> guarded, Type type, string name,
            Type result, bool isStatic = false)
        {
            var getter = AccessTools.PropertyGetter(type, name);
            if (getter == null || getter.ReturnType != result || getter.IsStatic != isStatic
                || getter.GetParameters().Length != 0)
                return false;
            guarded.Add(getter);
            return true;
        }

        private static bool AddMethod(HashSet<MethodBase> guarded, Type type, string name,
            Type result, bool isStatic, params Type[] parameters)
        {
            var method = AccessTools.DeclaredMethod(type, name, parameters);
            if (method == null || method.ReturnType != result || method.IsStatic != isStatic
                || method.ContainsGenericParameters)
                return false;
            guarded.Add(method);
            return true;
        }
    }
}
