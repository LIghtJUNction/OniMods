using System.Runtime.InteropServices;
using OniSim.CycleTrim;

if (args.Length == 2 && (args[0] == "--profile" || args[0] == "--check-profile"))
{
    string assemblyPath = Path.GetFullPath(args[1]);
    var resolver = new System.Reflection.PathAssemblyResolver(Directory.GetFiles(Path.GetDirectoryName(assemblyPath), "*.dll"));
    using var context = new System.Reflection.MetadataLoadContext(resolver, "mscorlib");
    var assembly = context.LoadFromAssemblyPath(assemblyPath);
    Console.WriteLine("portableAbiSha256=" + PortableProfile.Fingerprint(assembly));
    if (args[0] == "--check-profile") PortableProfile.Validate(assembly);
    return;
}

PortableProfileTests.Run();
NativePlatform.RequireSupported();
var directory = Path.Combine(Path.GetTempPath(), "onisim-loader-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var library = NativePlatform.BundledLibrary(directory);
    if (Path.GetFileName(library) != NativePlatform.LibraryName ||
        Path.GetFileName(Path.GetDirectoryName(library)) != NativePlatform.Id)
        throw new Exception("The bundle selected the wrong platform");
    var result = OniSimLoader.Configure(typeof(Program).Assembly, false);
    if (result.State != LoaderState.Disabled) throw new Exception("Disabled settings opened a native library");
    Environment.SetEnvironmentVariable(OniSimLoader.EnableVariable, "off");
    if (OniSimLoader.Configure(typeof(Program).Assembly, true).State != LoaderState.Disabled)
        throw new Exception("The startup disable override was ignored");
    Environment.SetEnvironmentVariable(OniSimLoader.EnableVariable, "invalid");
    if (OniSimLoader.Configure(typeof(Program).Assembly, true).State != LoaderState.Refused)
        throw new Exception("An invalid override was accepted");
    Environment.SetEnvironmentVariable(OniSimLoader.EnableVariable, null);
    try
    {
        OfficialProfile.ValidateAssembly(typeof(Program).Assembly);
        throw new Exception("An unknown game assembly was accepted");
    }
    catch (InvalidOperationException) { }
    Console.WriteLine("Platform selection, startup opt-out and unknown-profile refusal passed");
    if (args.Length == 0)
    {
        Console.WriteLine("Native library smoke test not run: pass the built bundle root");
        return;
    }
    var path = Path.Combine(Path.GetFullPath(args[0]), NativePlatform.Id, NativePlatform.LibraryName);
    var module = NativeModule.OpenTarget(path);
    OfficialProfile.ValidateTarget(module);
    var resident = NativeModule.OpenAlreadyLoaded(path);
    if (resident.RequireSymbol("ONISIM_GetAbiVersion") != module.RequireSymbol("ONISIM_GetAbiVersion"))
        throw new Exception("Already-loaded lookup returned a different native module");
    var initialize = module.Bind<Initialize>("ONISIM_TryInitialize");
    var status = module.Bind<Status>("ONISIM_GetLastStatus");
    var shutdown = module.Bind<Shutdown>("SIM_Shutdown");
    initialize(IntPtr.Zero);
    if (status() != 0) throw new Exception("Native initialization failed");
    shutdown();
    if (status() != 0) throw new Exception("Native shutdown failed");
    try
    {
        module.RequireSymbol("ONISIM_MissingSymbol");
        throw new Exception("A missing export was accepted");
    }
    catch (EntryPointNotFoundException) { }
    Console.WriteLine(NativePlatform.Id + " native exports, initialization, shutdown and resident module passed");
}
finally
{
    Environment.SetEnvironmentVariable(OniSimLoader.EnableVariable, null);
    Directory.Delete(directory, true);
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Initialize(IntPtr callback);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Status();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Shutdown();
