using OniSim.Compatibility;

if (args.Length != 2 || args[0] != "describe")
{
    Console.Error.WriteLine("Usage: OniSim.Host describe /absolute/path/to/liboni_sim_core.so");
    Console.Error.WriteLine("The former grid-demo and batch-demo fixtures were retired; they do not satisfy current table and frame contracts.");
    return 2;
}

try
{
    using var session = OniSimSession.Open(Path.GetFullPath(args[1]));
    Console.WriteLine(session.Describe());
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
    return 1;
}
