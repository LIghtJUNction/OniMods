using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace OniSim.CycleTrim
{
    // Compare public ABI metadata, never method bodies or private Mono layouts.
    // MVIDs, file paths and platform-specific PE headers are deliberately absent.
    internal static class PortableProfile
    {
        internal const int GameBuild = 744825;
        internal const string AbiSha256 = "1ea01b47d321af150f856c087fe66cb51f50ca7d232fd965fbc086b6bef44879";

        internal static void Validate(Assembly assembly)
        {
            Type version = assembly.GetType("KleiVersion", true);
            FieldInfo build = version.GetField("ChangeList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (build == null || !build.IsLiteral || Convert.ToInt32(build.GetRawConstantValue(), CultureInfo.InvariantCulture) != GameBuild)
                throw new InvalidOperationException("The game's build does not match the inspected OniSim ABI profile.");
            if (!Equals(version.GetField("BuildBranch").GetRawConstantValue(), "release") ||
                !Equals(version.GetField("isDebugBuild").GetRawConstantValue(), false) ||
                !Equals(version.GetField("Patch").GetRawConstantValue(), ""))
                throw new InvalidOperationException("The game is not the inspected release build.");
            if (!string.Equals(Fingerprint(assembly), AbiSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The game's SimDLL imports or message layouts do not match the inspected OniSim ABI profile.");
        }

        internal static string Fingerprint(Assembly assembly)
        {
            var text = new StringBuilder();
            var pending = new Queue<Type>();
            var definitions = new SortedDictionary<string, string>(StringComparer.Ordinal);
            int imports = 0;
            foreach (Type type in assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    CustomAttributeData import = method.GetCustomAttributesData().FirstOrDefault(a =>
                        a.AttributeType.FullName == "System.Runtime.InteropServices.DllImportAttribute");
                    if (import == null) continue;
                    string library = Convert.ToString(import.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
                    if (library != "SimDLL" && library != "SimDLL.dll") continue;
                    text.Append(type.FullName).Append("::").Append(method.Name).Append('|');
                    AppendAttributes(text, new[] { import });
                    AppendMethod(text, method, pending);
                    imports++;
                }
                if (type.DeclaringType != null && (type.DeclaringType.FullName == "Sim" || type.DeclaringType.FullName == "SimMessages"))
                    pending.Enqueue(type);
            }
            text.Append("imports=").Append(imports).Append('\n');
            while (pending.Count != 0)
            {
                Type type = pending.Dequeue();
                string name = type.FullName ?? type.Name;
                if (definitions.ContainsKey(name) || type.IsPrimitive || type.IsPointer || type.IsByRef || type.IsArray ||
                    name == "System.Void" || name == "System.String")
                {
                    if (type.HasElementType) pending.Enqueue(type.GetElementType());
                    continue;
                }
                definitions.Add(name, "");
                var definition = new StringBuilder(name).Append('|');
                if (type.IsEnum)
                {
                    definition.Append(type.GetEnumUnderlyingType().FullName).Append('|');
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(f => f.Name, StringComparer.Ordinal))
                        definition.Append(field.Name).Append('=').Append(Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture)).Append(';');
                }
                else if (type.BaseType != null && type.BaseType.FullName == "System.MulticastDelegate")
                {
                    AppendAttributes(definition, type.GetCustomAttributesData());
                    AppendMethod(definition, type.GetMethod("Invoke"), pending);
                }
                else if (type.IsValueType)
                {
                    var layout = type.StructLayoutAttribute;
                    definition.Append(layout.Value).Append(':').Append(layout.Pack).Append(':').Append(layout.Size).Append(':').Append(layout.CharSet).Append('|');
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(f => f.MetadataToken))
                    {
                        definition.Append(field.Name).Append(':').Append(field.FieldType.FullName).Append('|');
                        AppendAttributes(definition, field.GetCustomAttributesData());
                        pending.Enqueue(field.FieldType);
                    }
                }
                definitions[name] = definition.ToString();
            }
            foreach (string definition in definitions.Values) text.Append(definition).Append('\n');
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        private static void AppendMethod(StringBuilder text, MethodInfo method, Queue<Type> pending)
        {
            AppendParameter(text, method.ReturnParameter, pending);
            foreach (ParameterInfo parameter in method.GetParameters()) AppendParameter(text, parameter, pending);
            text.Append('\n');
        }

        private static void AppendParameter(StringBuilder text, ParameterInfo parameter, Queue<Type> pending)
        {
            text.Append(parameter.ParameterType.FullName).Append(':').Append(parameter.Attributes).Append('|');
            AppendAttributes(text, parameter.GetCustomAttributesData());
            pending.Enqueue(parameter.ParameterType);
        }

        private static void AppendAttributes(StringBuilder text, IEnumerable<CustomAttributeData> attributes)
        {
            foreach (CustomAttributeData attribute in attributes.Where(a => a.AttributeType.Namespace == "System.Runtime.InteropServices")
                .OrderBy(a => a.AttributeType.FullName, StringComparer.Ordinal))
            {
                text.Append(attribute.AttributeType.FullName).Append('(');
                foreach (CustomAttributeTypedArgument argument in attribute.ConstructorArguments)
                {
                    object value = argument.Value;
                    if (attribute.AttributeType.FullName == "System.Runtime.InteropServices.DllImportAttribute" &&
                        (Equals(value, "SimDLL") || Equals(value, "SimDLL.dll"))) value = "SimDLL";
                    text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(',');
                }
                foreach (CustomAttributeNamedArgument argument in attribute.NamedArguments.OrderBy(a => a.MemberName, StringComparer.Ordinal))
                    text.Append(argument.MemberName).Append('=').Append(Convert.ToString(argument.TypedValue.Value, CultureInfo.InvariantCulture)).Append(',');
                text.Append(")|");
            }
        }
    }
}
