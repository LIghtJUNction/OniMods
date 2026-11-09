using System;
using System.Reflection;
using KMod;

namespace Jlib.Runtime
{
    public static class JlibApi
    {
#if BAD_ABI
        public const int AbiVersion = 2;
#else
        public const int AbiVersion = 1;
#endif
#if BAD_STATIC_ID
        public const string StaticId = "another.provider";
#else
        public const string StaticId = "LIghtJUNction.Jlib";
#endif
#if OLD_VERSION
        public const string Version = "0.1.9";
#else
        public const string Version = "0.2.0";
#endif
        public const string PLibVersion = "4.25.0.0";
#if !NO_REGISTRAR
        public static void RegisterConsumer(Assembly implementation, Mod owner)
        {
            AppDomain.CurrentDomain.SetData("fixture.registered.assembly", implementation);
            AppDomain.CurrentDomain.SetData("fixture.registered.owner", owner);
        }
#endif
    }
    public interface IFixtureJlib { }
}
