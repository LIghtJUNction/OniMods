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
        foreach (PackingSize pack in new[] { PackingSize.Size1, PackingSize.Size2, PackingSize.Size8, PackingSize.Size16 })
            Different(first, Fixture(typeof(int), pack, "SimDLL"), "message packing " + pack);
        Equal(Fixture(typeof(long), PackingSize.Unspecified, "SimDLL"),
            Fixture(typeof(long), PackingSize.Size8, "SimDLL"), "default and explicit eight-byte packing");
        Equal(first, Fixture(typeof(int), PackingSize.Size4, "SimDLL", returnFlags: ParameterAttributes.Retval),
            "synthesized return-slot Retval");
        Different(first, Fixture(typeof(int), PackingSize.Size4, "SimDLL", returnFlags: ParameterAttributes.Out),
            "return direction");
        foreach (ParameterAttributes flags in new[] { ParameterAttributes.In, ParameterAttributes.Out, ParameterAttributes.Retval })
            Different(first, Fixture(typeof(int), PackingSize.Size4, "SimDLL", argumentFlags: flags),
                "ordinary parameter " + flags);
        Different(first, Fixture(typeof(int), PackingSize.Size4, "SimDLL", size: 32), "explicit struct size");
        Different(Fixture(typeof(int), PackingSize.Size4, "SimDLL", offset: 0),
            Fixture(typeof(int), PackingSize.Size4, "SimDLL", offset: 4), "explicit field offset");
        Different(Fixture(typeof(int), PackingSize.Size4, "SimDLL", returnMarshal: UnmanagedType.I1),
            Fixture(typeof(int), PackingSize.Size4, "SimDLL", returnMarshal: UnmanagedType.Bool), "return marshaling");
        Different(first, Fixture(typeof(int), PackingSize.Size4, "SimDLL", convention: CallingConvention.StdCall),
            "calling convention");
        Different(first, Fixture(typeof(int), PackingSize.Size4, "SimDLL", charset: CharSet.Unicode),
            "character set");
        Different(first, Fixture(typeof(int), PackingSize.Size4, "SimDLL", entryPoint: "Changed"),
            "entry point");

        var assembly = typeof(PortableProfileTests).Assembly;
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Append(assembly.Location);
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths), typeof(object).Assembly.GetName().Name);
        var metadata = context.LoadFromAssemblyPath(assembly.Location);
        if (PortableProfile.Fingerprint(assembly) != PortableProfile.Fingerprint(metadata))
            throw new Exception("Static ABI inventory differs from runtime reflection");
        Console.WriteLine("Portable ABI inventory normalizes default pack/return Retval; field/packing/size/offset/marshaling/parameter/import changes differ; metadata/runtime fingerprints match");
    }

    private static void Equal(Assembly first, Assembly second, string description)
    {
        if (PortableProfile.Fingerprint(first) != PortableProfile.Fingerprint(second))
            throw new Exception("Equivalent ABI profiles differ: " + description);
    }

    private static void Different(Assembly first, Assembly second, string description)
    {
        if (PortableProfile.Fingerprint(first) == PortableProfile.Fingerprint(second))
            throw new Exception("A native ABI change was accepted: " + description);
    }

    private static Assembly Fixture(Type field, PackingSize packing, string library,
        ParameterAttributes returnFlags = ParameterAttributes.None,
        ParameterAttributes argumentFlags = ParameterAttributes.None, int size = 0, int? offset = null,
        UnmanagedType? returnMarshal = null, CallingConvention convention = CallingConvention.Cdecl,
        CharSet charset = CharSet.Ansi, string entryPoint = "SIM_HandleMessage")
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("OniSim.AbiFixture"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Fixture");
        var sim = module.DefineType("Sim", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var packet = module.DefineType("Packet", TypeAttributes.Public | TypeAttributes.Sealed |
            (offset.HasValue ? TypeAttributes.ExplicitLayout : TypeAttributes.SequentialLayout), typeof(ValueType), packing, size);
        var value = packet.DefineField("value", field, FieldAttributes.Public);
        if (offset.HasValue) value.SetOffset(offset.Value);
        var packetType = packet.CreateType();
        var method = sim.DefinePInvokeMethod("Handle", library, entryPoint,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
            CallingConventions.Standard, returnMarshal.HasValue ? typeof(bool) : typeof(void),
            [packetType.MakeByRefType()], convention, charset);
        var returned = method.DefineParameter(0, returnFlags, null);
        method.DefineParameter(1, argumentFlags, "packet");
        if (returnMarshal.HasValue)
            returned.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(MarshalAsAttribute).GetConstructor([typeof(UnmanagedType)]), [returnMarshal.Value]));
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
