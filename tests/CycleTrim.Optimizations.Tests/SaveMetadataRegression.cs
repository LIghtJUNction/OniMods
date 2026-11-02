using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim.Optimizations.Tests
{
    internal static class SaveMetadataRegression
    {
        private static int assertions;

        [SkipSaveFileSerialization]
        internal class Skipped : SyntheticSaveComponent { }
        internal sealed class Inherited : Skipped { }
        internal sealed class Generic<T> : SyntheticSaveComponent { }
        private sealed class PatchedOnly : SyntheticSaveComponent { }
        private sealed class Failing : SyntheticSaveComponent
        {
            public override void Write(BinaryWriter writer)
            {
                throw new InvalidOperationException("synthetic payload failure");
            }
        }

        internal static void Run()
        {
            AttributeArgumentsStayDistinct();
            CustomReflectionAndExceptionsAreNotCached();
            SameNamesDoNotAliasTypes();
            ConcurrentAndWarmReads();
            CollectibleTypesAreNotRetained();
            TranspilerIsAtomicAndPreservesMetadata();
            PatchedSerializationMatchesOriginal();
            Console.WriteLine("PASS save metadata: " + assertions
                + " assertions; real Harmony, byte comparison, weak lifetime and concurrency");
        }

        private static void AttributeArgumentsStayDistinct()
        {
            var types = new[] { typeof(SyntheticSaveComponent), typeof(Skipped), typeof(Inherited),
                typeof(Generic<int>), typeof(Generic<string>), typeof(Dictionary<string, int>),
                typeof(int[]), typeof(int).MakePointerType(), typeof(int).MakeByRefType() };
            var attributes = new[] { typeof(SkipSaveFileSerialization), typeof(SerializableAttribute),
                typeof(AttributeUsageAttribute) };
            foreach (var type in types)
            {
                Equal(type.ToString(), SaveTypeMetadataCache.TypeName(type), "exact Type.ToString");
                foreach (var attribute in attributes)
                    foreach (var inherit in new[] { false, true })
                        Equal(type.IsDefined(attribute, inherit),
                            SaveTypeMetadataCache.IsDefined(type, attribute, inherit), "attribute query");
            }
            Check(!SaveTypeMetadataCache.IsDefined(typeof(Inherited), typeof(SkipSaveFileSerialization), false),
                "direct declaration must not inherit skip-save");
            Check(SaveTypeMetadataCache.IsDefined(typeof(Inherited), typeof(SkipSaveFileSerialization), true),
                "inherited query must keep its original semantics");
        }

        private sealed class MutableType : TypeDelegator
        {
            internal int AttributeCalls;
            internal int NameCalls;
            internal bool Answer;
            internal bool Throw;
            internal MutableType() : base(typeof(SyntheticSaveComponent)) { }
            public override bool IsDefined(Type attributeType, bool inherit)
            {
                AttributeCalls++;
                if (Throw) throw new NotSupportedException("mutable metadata");
                return Answer;
            }
            public override string ToString() { return "mutable-" + ++NameCalls; }
        }

        private sealed class MutableName
        {
            private int calls;
            public override string ToString() { return (++calls).ToString(); }
        }

        private static void CustomReflectionAndExceptionsAreNotCached()
        {
            var type = new MutableType();
            Check(!SaveTypeMetadataCache.IsDefined(type, typeof(SkipSaveFileSerialization), false), "custom false");
            type.Answer = true;
            Check(SaveTypeMetadataCache.IsDefined(type, typeof(SkipSaveFileSerialization), false), "custom changed");
            Equal(2, type.AttributeCalls, "custom metadata queried every time");
            Equal("mutable-1", SaveTypeMetadataCache.TypeName(type), "custom name 1");
            Equal("mutable-2", SaveTypeMetadataCache.TypeName(type), "custom name 2");
            type.Throw = true;
            Equal(typeof(NotSupportedException), Error(() => SaveTypeMetadataCache.IsDefined(
                type, typeof(SkipSaveFileSerialization), false)), "custom exception");
            type.Throw = false;
            Check(SaveTypeMetadataCache.IsDefined(type, typeof(SkipSaveFileSerialization), false),
                "an exception must not poison a later successful query");
            Equal(4, type.AttributeCalls, "exception not cached");
            var name = new MutableName();
            Equal("1", SaveTypeMetadataCache.TypeName(name), "non-Type virtual dispatch 1");
            Equal("2", SaveTypeMetadataCache.TypeName(name), "non-Type virtual dispatch 2");
            Equal(typeof(NullReferenceException), Error(() => SaveTypeMetadataCache.TypeName(null)), "null name");
            Equal(typeof(NullReferenceException), Error(() => SaveTypeMetadataCache.IsDefined(
                null, typeof(SkipSaveFileSerialization), false)), "null member");
            Equal(Error(() => typeof(Skipped).IsDefined(null, false)),
                Error(() => SaveTypeMetadataCache.IsDefined(typeof(Skipped), null, false)), "null attribute");
            Equal(Error(() => typeof(Skipped).IsDefined(typeof(string), false)),
                Error(() => SaveTypeMetadataCache.IsDefined(typeof(Skipped), typeof(string), false)), "non-attribute type");
            var wrappedAttribute = new TypeDelegator(typeof(SkipSaveFileSerialization));
            bool expectedWrapped = false;
            bool actualWrapped = false;
            Equal(Error(() => expectedWrapped = typeof(Skipped).IsDefined(wrappedAttribute, false)),
                Error(() => actualWrapped = SaveTypeMetadataCache.IsDefined(typeof(Skipped), wrappedAttribute, false)),
                "wrapped attribute error");
            Equal(expectedWrapped, actualWrapped, "wrapped attribute result");
            var method = typeof(MutableName).GetMethod("ToString");
            Equal(method.IsDefined(typeof(ObsoleteAttribute), true),
                SaveTypeMetadataCache.IsDefined(method, typeof(ObsoleteAttribute), true), "non-Type member");
        }

        private static Type NewType(string assemblyName, bool skip)
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName),
                AssemblyBuilderAccess.RunAndCollect);
            var type = assembly.DefineDynamicModule(assemblyName).DefineType("Same.Name", TypeAttributes.Public);
            if (skip)
                type.SetCustomAttribute(new CustomAttributeBuilder(
                    typeof(SkipSaveFileSerialization).GetConstructor(Type.EmptyTypes), new object[0]));
            return type.CreateType();
        }

        private static void SameNamesDoNotAliasTypes()
        {
            var first = NewType("CycleTrim.Metadata.First", true);
            var second = NewType("CycleTrim.Metadata.Second", false);
            Equal(first.ToString(), second.ToString(), "same displayed type name fixture");
            Check(SaveTypeMetadataCache.IsDefined(first, typeof(SkipSaveFileSerialization), false), "first type skipped");
            Check(!SaveTypeMetadataCache.IsDefined(second, typeof(SkipSaveFileSerialization), false), "second type saved");
        }

        private static void ConcurrentAndWarmReads()
        {
            var type = typeof(Generic<Tuple<int, string>>);
            var expected = type.ToString();
            Parallel.For(0, 8, worker =>
            {
                for (int index = 0; index < 10000; index++)
                    if (SaveTypeMetadataCache.TypeName(type) != expected
                        || SaveTypeMetadataCache.IsDefined(type, typeof(SkipSaveFileSerialization), false))
                        throw new InvalidOperationException("concurrent metadata differs");
            });
            // Warm this complete measuring method, including its counter call
            // sites and tiered PGO/OSR transitions, before asserting hot allocations.
            for (int warmup = 0; warmup < 3; warmup++)
                MeasureWarmReads(type);
            Check(MeasureWarmReads(type) == 0, "warm cache allocations");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MeasureWarmReads(Type type)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100000; index++)
            {
                SaveTypeMetadataCache.IsDefined(type, typeof(SkipSaveFileSerialization), false);
                SaveTypeMetadataCache.TypeName(type);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference WarmCollectibleType()
        {
            var type = NewType("CycleTrim.Metadata.Collectible", true);
            SaveTypeMetadataCache.IsDefined(type, typeof(SkipSaveFileSerialization), false);
            SaveTypeMetadataCache.TypeName(type);
            return new WeakReference(type);
        }

        private static void CollectibleTypesAreNotRetained()
        {
            var weak = WarmCollectibleType();
            for (int index = 0; index < 12 && weak.IsAlive; index++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Check(!weak.IsAlive, "metadata cache must not keep a collectible type alive");
        }

        private static List<CodeInstruction> Calls()
        {
            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(MemberInfo), "IsDefined",
                    new[] { typeof(Type), typeof(bool) })),
                new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(MemberInfo), "IsDefined",
                    new[] { typeof(Type), typeof(bool) })),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(object), "GetType")),
                new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(object), "ToString"))
            };
        }

        private static void TranspilerIsAtomicAndPreservesMetadata()
        {
            var input = Calls();
            var label = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
            input[0].labels.Add(label);
            input[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            var output = new List<CodeInstruction>(SaveTypeMetadataPatch.Transpiler(input));
            Equal(OpCodes.Callvirt, input[0].opcode, "upstream instruction must not be mutated");
            Equal(OpCodes.Call, output[0].opcode, "cached query uses static call");
            Equal(1, output[0].labels.Count, "branch label preserved");
            Equal(label, output[0].labels[0], "same branch destination");
            Equal(ExceptionBlockType.BeginExceptionBlock, output[0].blocks[0].blockType, "exception boundary preserved");
            foreach (var mutation in new[] { 0, 1, 2, 3 })
            {
                var changed = Calls();
                if (mutation == 0) changed.RemoveAt(0);
                if (mutation == 1) changed.Add(changed[0]);
                if (mutation == 2) changed[2] = new CodeInstruction(OpCodes.Nop);
                if (mutation == 3) changed.Insert(0, new CodeInstruction(OpCodes.Constrained, typeof(int)));
                var unchanged = new List<CodeInstruction>(SaveTypeMetadataPatch.Transpiler(changed));
                Equal(changed.Count, unchanged.Count, "unknown shape instruction count");
                for (int index = 0; index < changed.Count; index++)
                    Check(ReferenceEquals(changed[index], unchanged[index]), "unknown shape left wholly unchanged");
            }
        }

        private static byte[] Serialize(SaveLoadRoot root)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    root.SaveWithoutTransform(writer);
                return stream.ToArray();
            }
        }

        private static void PatchedSerializationMatchesOriginal()
        {
            var fixtures = new List<SaveLoadRoot>();
            fixtures.Add(new SaveLoadRoot { Components = new SyntheticSaveComponent[] { new PatchedOnly() } });
            var random = new Random(7029);
            for (int trial = 0; trial < 256; trial++)
            {
                var components = new SyntheticSaveComponent[random.Next(0, 100)];
                for (int index = 0; index < components.Length; index++)
                {
                    switch (random.Next(5))
                    {
                        case 0: components[index] = new SyntheticSaveComponent(); break;
                        case 1: components[index] = new Skipped(); break;
                        case 2: components[index] = new Inherited(); break;
                        case 3: components[index] = new Generic<int>(); break;
                        default: continue;
                    }
                    components[index].Value = random.Next();
                }
                fixtures.Add(new SaveLoadRoot { Components = components });
            }
            var expected = fixtures.ConvertAll(Serialize);
            Check(!HasCachedType(typeof(PatchedOnly)), "unpatched fixture does not fill production cache");
            var failure = new SaveLoadRoot { Components = new SyntheticSaveComponent[] { new Failing() } };
            var expectedError = Error(() => Serialize(failure));
            var harmony = new Harmony("CycleTrim.Metadata.HostTest");
            try
            {
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false, saveMetadata: false);
                harmony.CreateClassProcessor(typeof(SaveTypeMetadataPatch)).Patch();
                var target = AccessTools.Method(typeof(SaveLoadRoot), "SaveWithoutTransform");
                Check(Harmony.GetPatchInfo(target) == null, "disabled option must not install a patch");
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false);
                harmony.CreateClassProcessor(typeof(SaveTypeMetadataPatch)).Patch();
                Check(Harmony.GetPatchInfo(target).Owners.Contains(harmony.Id), "real Harmony patch installed");
                for (int index = 0; index < fixtures.Count; index++)
                    Equal(Convert.ToBase64String(expected[index]), Convert.ToBase64String(Serialize(fixtures[index])),
                        "patched serialized bytes trial " + index);
                Check(HasCachedType(typeof(PatchedOnly)), "executed Harmony replacement reaches production cache");
                Equal(expectedError, Error(() => Serialize(failure)), "payload exception unchanged");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false);
            }
        }

        private static bool HasCachedType(Type type)
        {
            var entries = typeof(SaveTypeMetadataCache).GetField("Entries",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            return (bool)entries.GetType().GetMethod("TryGetValue").Invoke(entries, new object[] { type, null });
        }

        private static Type Error(Action action)
        {
            try { action(); }
            catch (Exception error) { return error.GetType(); }
            return null;
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(message + ": expected " + expected + ", actual " + actual);
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
