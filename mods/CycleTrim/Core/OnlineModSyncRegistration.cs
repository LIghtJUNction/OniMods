using System;
using System.Collections.Generic;
using System.Reflection;

namespace CycleTrim.Core
{
    // Optional integration: use the framework already loaded by OniOnline.
    // CycleTrim must neither require nor ship a second framework assembly.
    internal static class OnlineModSyncRegistration
    {
        internal const string ModId = "LIghtJUNction.CycleTrim";
        private const string FrameworkAssemblyName = "OniOnline.Framework";

        internal static void RegisterIfAvailable()
        {
            if (TryRegister(AppDomain.CurrentDomain.GetAssemblies(), out var error))
                Console.WriteLine("[CycleTrim] OniOnline content declared: Required, public package .json metadata.");
            else if (error != null)
                Console.WriteLine("[CycleTrim] OniOnline content declaration failed: " + error +
                    ". Strict mod synchronization remains required.");
        }

        internal static bool TryRegister(IEnumerable<Assembly> loadedAssemblies, out string error)
        {
            error = null;
            try
            {
                Assembly framework = null;
                foreach (var assembly in loadedAssemblies)
                {
                    if (assembly == null || assembly.GetName().Name != FrameworkAssemblyName) continue;
                    if (framework != null)
                    {
                        error = "mod_sync_framework_ambiguous";
                        return false;
                    }
                    framework = assembly;
                }
                if (framework == null) return false;

                var declarations = framework.GetType("OniOnline.Sdk.ModSyncDeclarations", false);
                var policy = framework.GetType("OniOnline.Sdk.ModSyncPolicy", false);
                if (declarations == null || policy == null || !policy.IsEnum || !Enum.IsDefined(policy, "Required"))
                {
                    error = "mod_sync_api_unavailable";
                    return false;
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
                var register = declarations.GetMethod("Register", flags, null, new[] { typeof(string), policy }, null);
                var registerContent = declarations.GetMethod("RegisterContentExtensions", flags, null,
                    new[] { typeof(string), typeof(IEnumerable<string>) }, null);
                if (register == null || registerContent == null ||
                    register.ReturnType != typeof(void) || registerContent.ReturnType != typeof(void) ||
                    register.ContainsGenericParameters || registerContent.ContainsGenericParameters)
                {
                    error = "mod_sync_api_unavailable";
                    return false;
                }

                register.Invoke(null, new[] { (object)ModId, Enum.Parse(policy, "Required", false) });
                // OniSim/<platform>/build.json and the bundled public conformance metadata
                // participate in the same strict digest as the managed/native code.
                // The framework still excludes private settings and save paths.
                registerContent.Invoke(null, new object[] { ModId, new[] { ".json" } });
                return true;
            }
            catch (Exception exception)
            {
                var cause = exception is TargetInvocationException && exception.InnerException != null
                    ? exception.InnerException : exception;
                error = cause.GetType().Name;
                if (cause is ArgumentException || cause is InvalidOperationException)
                    error += ": " + cause.Message;
                return false;
            }
        }
    }
}
