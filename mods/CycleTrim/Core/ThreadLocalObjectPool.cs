using PeterHan.PLib.Core;

namespace CycleTrim.Core
{
    /// <summary>
    /// Compatibility shim forwarding to Jlib's PeterHan.PLib.Core.PThreadLocalPool.
    /// </summary>
    internal static class ThreadLocalObjectPool<T>
        where T : class, new()
    {
        internal static T Rent() => PThreadLocalPool<T>.Rent();

        internal static void Return(T value) => PThreadLocalPool<T>.Return(value);
    }
}
