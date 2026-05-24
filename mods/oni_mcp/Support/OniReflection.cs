using System;
using System.Reflection;

namespace OniMcp.Support
{
    public static class OniReflection
    {
        public static FieldInfo GetFieldSafe(Type type, string name, bool isStatic)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            return type.GetField(name, flags);
        }

        public static MethodInfo GetMethodSafe(Type type, string name, bool isStatic, Type[] parameters)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            if (parameters == null)
                return type.GetMethod(name, flags);
            return type.GetMethod(name, flags, null, parameters, null);
        }
    }
}
