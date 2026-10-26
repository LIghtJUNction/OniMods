using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal static class PackageVerifier
{
    private sealed class AssemblyInfo
    {
        internal string Name;
        internal string[] References;
        internal bool EmbeddedPUtil, BootstrapGate;
    }

    internal static void Check(string zipPath, string consumer)
    {
        if (string.IsNullOrEmpty(consumer) || consumer.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-')) ||
            consumer == "." || consumer == "..") throw new ArgumentException("Invalid consumer assembly name.");
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assemblies = new Dictionary<string, AssemblyInfo>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName;
                if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains('\\') || name.Split('/').Any(p => p == ".." || p == ".") ||
                    !names.Add(name)) throw new InvalidDataException("Package contains an ambiguous entry path.");
                string file = name.Split('/').Last();
                if (ForbiddenName(Path.GetFileNameWithoutExtension(file)))
                    throw new InvalidDataException("Package includes a shared dependency or game library: " + file);
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                using (var input = entry.Open())
                using (var bytes = new MemoryStream())
                {
                    input.CopyTo(bytes); bytes.Position = 0;
                    var info = ReadAssembly(bytes);
                    if (info == null) continue; // Native OniSim libraries have no managed metadata.
                    if (ForbiddenName(info.Name)) throw new InvalidDataException("Package includes a renamed shared dependency or game library.");
                    if (info.EmbeddedPUtil) throw new InvalidDataException("Package embeds PUtil: " + name);
                    if (info.References.Any(PLib)) throw new InvalidDataException("Package still references PLib: " + name);
                    if (info.Name == "OniOnline.Framework" && info.References.Any(r => r == "Jlib" || r.StartsWith("Unity", StringComparison.Ordinal) || r.StartsWith("Assembly-CSharp", StringComparison.Ordinal)))
                        throw new InvalidDataException("OniOnline.Framework must remain independent of Jlib and the game.");
                    assemblies.Add(name, info);
                }
            }
            if (!assemblies.TryGetValue(consumer + ".dll", out var bootstrap) || bootstrap.Name != consumer + ".Bootstrap")
                throw new InvalidDataException("Root DLL is not the expected consumer bootstrap.");
            if (bootstrap.References.Any(r => r.Equals("Jlib", StringComparison.OrdinalIgnoreCase)) || !bootstrap.BootstrapGate)
                throw new InvalidDataException("Root bootstrap has a direct Jlib dependency or lacks its dependency gate.");
            if (!assemblies.TryGetValue("implementation/" + consumer + ".dll", out var implementation) || implementation.Name != consumer)
                throw new InvalidDataException("Package is missing the implementation with its original assembly identity.");
            if (consumer != "OniModTemplate" && !implementation.References.Contains("Jlib", StringComparer.Ordinal))
                throw new InvalidDataException("Consumer implementation does not reference external Jlib.");
        }
    }

    private static bool PLib(string name)
    { return name.StartsWith("PLib", StringComparison.OrdinalIgnoreCase) || name.StartsWith("PeterHan.PLib", StringComparison.OrdinalIgnoreCase); }

    private static bool ForbiddenName(string name)
    {
        return name.Equals("Jlib", StringComparison.OrdinalIgnoreCase) || PLib(name) ||
            name.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) || name.Equals("Assembly-CSharp-firstpass", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) || name.Equals("Unity.TextMeshPro", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("0Harmony", StringComparison.OrdinalIgnoreCase) || name.Equals("com.rlabrecque.steamworks.net", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase) || name.Equals("SimDLL", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("libSimDLL", StringComparison.OrdinalIgnoreCase) || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) || name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("System.", StringComparison.OrdinalIgnoreCase);
    }

    private static AssemblyInfo ReadAssembly(Stream stream)
    {
        using (var pe = new PEReader(stream))
        {
            if (!pe.HasMetadata) return null;
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) throw new InvalidDataException("Managed DLL is not an assembly.");
            return new AssemblyInfo {
                Name = reader.GetString(reader.GetAssemblyDefinition().Name),
                References = reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)).ToArray(),
                EmbeddedPUtil = reader.TypeDefinitions.Any(h => reader.GetString(reader.GetTypeDefinition(h).Name) == "PUtil"),
                BootstrapGate = HasBootstrapGate(pe, reader)
            };
        }
    }

    private static bool HasBootstrapGate(PEReader pe, MetadataReader reader)
    {
        var bootstrap = reader.TypeDefinitions.FirstOrDefault(h => TypeName(reader, h) == "OniMods.JlibBootstrap.BootstrapMod");
        if (bootstrap.IsNil) return false;
        var definition = reader.GetTypeDefinition(bootstrap);
        var method = definition.GetMethods().FirstOrDefault(h => reader.GetString(reader.GetMethodDefinition(h).Name) == "OnLoad");
        if (method.IsNil) return false;
        var onLoad = reader.GetMethodDefinition(method);
        if (onLoad.RelativeVirtualAddress == 0) return false;
        var instructions = Instructions(pe.GetMethodBody(onLoad.RelativeVirtualAddress).GetILBytes()).ToArray();
        int gate = Array.FindIndex(instructions, i => Call(reader, i) == "OniMods.JlibBootstrap.DependencyLoader.TryLoad");
        int load = Array.FindIndex(instructions, i => Call(reader, i) == "System.Reflection.Assembly.LoadFrom");
        if (gate < 0 || load <= gate || gate + 1 >= instructions.Length) return false;
        var branch = instructions[gate + 1];
        if (branch.OpCode != OpCodes.Brtrue && branch.OpCode != OpCodes.Brtrue_S) return false;
        // The false result must return before the successful branch can load any payload.
        if (branch.Target <= branch.Offset || branch.Target > instructions[load].Offset ||
            !instructions.Skip(gate + 2).TakeWhile(i => i.Offset < branch.Target).Any(i => i.OpCode == OpCodes.Ret)) return false;
        return instructions.Take(gate).All(i => Call(reader, i) != "System.Reflection.Assembly.GetTypes") &&
            reader.TypeDefinitions.Any(h => TypeName(reader, h) == "OniMods.JlibBootstrap.DependencyLoader") &&
            reader.TypeDefinitions.Any(h => TypeName(reader, h) == "OniMods.JlibBootstrap.DependencyNotice");
    }

    private readonly struct Instruction
    {
        internal readonly int Offset, Token, Target;
        internal readonly OpCode OpCode;
        internal Instruction(int offset, OpCode opcode, int token, int target)
        { Offset = offset; OpCode = opcode; Token = token; Target = target; }
    }

    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => unchecked((ushort)o.Value));

    private static IEnumerable<Instruction> Instructions(byte[] il)
    {
        for (int at = 0; at < il.Length;)
        {
            int offset = at, token = 0, target = -1;
            ushort value = il[at++];
            if (value == 0xfe) value = (ushort)(0xfe00 | il[at++]);
            if (!OpCodesByValue.TryGetValue(value, out var opcode)) throw new InvalidDataException("Invalid IL opcode.");
            int size;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone: size = 0; break;
                case OperandType.ShortInlineBrTarget: size = 1; target = at + 1 + (sbyte)il[at]; break;
                case OperandType.InlineBrTarget: size = 4; target = at + 4 + BitConverter.ToInt32(il, at); break;
                case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch: size = checked(4 + 4 * BitConverter.ToInt32(il, at)); break;
                default: size = 4; break;
            }
            if (size < 0 || at > il.Length - size) throw new InvalidDataException("Truncated IL operand.");
            if (opcode.OperandType == OperandType.InlineMethod) token = BitConverter.ToInt32(il, at);
            at += size;
            yield return new Instruction(offset, opcode, token, target);
        }
    }

    private static string Call(MetadataReader reader, Instruction instruction)
    {
        if (instruction.Token == 0) return "";
        var handle = MetadataTokens.EntityHandle(instruction.Token);
        if (handle.Kind == HandleKind.MethodSpecification) handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            return TypeName(reader, method.GetDeclaringType()) + "." + reader.GetString(method.Name);
        }
        if (handle.Kind == HandleKind.MemberReference)
        {
            var member = reader.GetMemberReference((MemberReferenceHandle)handle);
            return TypeName(reader, member.Parent) + "." + reader.GetString(member.Name);
        }
        return "";
    }

    private static string TypeName(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }
        return "";
    }
}
