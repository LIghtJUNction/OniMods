using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using OniSim.CycleTrim;

internal static class PortableProfileTests
{
    internal static void Run()
    {
        var first = Fixture(typeof(int), PackingSize.Size4, "SimDLL");
        var rebuilt = Fixture(typeof(int), PackingSize.Size4, "SimDLL.dll");
        if (first.ManifestModule.ModuleVersionId == rebuilt.ManifestModule.ModuleVersionId ||
            PortableProfile.Fingerprint(first) != PortableProfile.Fingerprint(rebuilt))
            throw new Exception("Equivalent ABI profiles changed with assembly identity or native library suffix");
        if (PortableProfile.Fingerprint(first) == PortableProfile.Fingerprint(Fixture(typeof(long), PackingSize.Size4, "SimDLL")))
            throw new Exception("A native message field width change was accepted");
        if (PortableProfile.Fingerprint(first) == PortableProfile.Fingerprint(Fixture(typeof(int), PackingSize.Size8, "SimDLL")))
            throw new Exception("A native message packing change was accepted");

        var assembly = typeof(PortableProfileTests).Assembly;
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Append(assembly.Location);
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths), typeof(object).Assembly.GetName().Name);
        var metadata = context.LoadFromAssemblyPath(assembly.Location);
        if (PortableProfile.Fingerprint(assembly) != PortableProfile.Fingerprint(metadata))
            throw new Exception("Static ABI inventory differs from runtime reflection");
        Console.WriteLine("Portable ABI inventory ignores MVID/suffix and detects field/packing changes; metadata/runtime fingerprints match");
    }

    private static Assembly Fixture(Type field, PackingSize packing, string library)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("OniSim.AbiFixture"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Fixture");
        var sim = module.DefineType("Sim", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var packet = module.DefineType("Packet", TypeAttributes.Public | TypeAttributes.Sealed |
            TypeAttributes.SequentialLayout, typeof(ValueType), packing);
        packet.DefineField("value", field, FieldAttributes.Public);
        var packetType = packet.CreateType();
        sim.DefinePInvokeMethod("Handle", library, "SIM_HandleMessage",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
            CallingConventions.Standard, typeof(void), [packetType.MakeByRefType()], CallingConvention.Cdecl, CharSet.Ansi);
        sim.CreateType();
        return assembly;
    }
}

// This declaration is never invoked. It lets both reflection paths inventory a
// real compiled marshaling contract without loading game assemblies in CI.
internal static class CompiledAbiFixture
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct Packet
    {
        internal int count;
        [MarshalAs(UnmanagedType.I1)] internal bool enabled;
    }

    [DllImport("SimDLL", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Fixture(ref Packet packet);
}
