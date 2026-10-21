using System;
using System.IO;
using System.Linq;

namespace OniOnline.Multiplayer
{
    internal sealed class NativeDupeRemapPayload
    {
        internal NativeDupeRemapPlan Plan;
        internal byte[][] Roots;
        internal string[] RootHashes;
        internal bool ImportReady => false;
    }

    internal static partial class NativeDuplicantRemapPlanner
    {
        // Work on owned snapshots. The original archive stays usable for verification
        // and recovery even if a later native admission or source release fails.
        internal static NativeDupeRemapPayload PreparePayload(byte[] sourceDirectory, byte[][] sourceRoots,
            NativeDupeRemapContext context, byte[] destinationDirectory)
        {
            byte[] schema = CopyBounded(sourceDirectory, 4 * 1024 * 1024);
            byte[] destinationSchema = CopyBounded(destinationDirectory, 4 * 1024 * 1024);
            if (sourceRoots == null || sourceRoots.Length != 2)
                throw Invalid("bounded_two_roots_and_schema_required");
            var roots = sourceRoots.Select(bytes => CopyBounded(bytes, 2 * 1024 * 1024)).ToArray();
            NativeDupeRemapPlan plan = Plan(schema, roots, CopyContext(context));
            RequireDestinationSchemaCompatibility(plan, destinationSchema);

            // These offsets came from the complete structural parse, never from a
            // search for integers that happen to equal a source instance ID.
            foreach (NativeDupeByteEdit edit in plan.Edits)
            {
                if (edit.Root < 0 || edit.Root >= roots.Length || edit.Offset < 0 ||
                    edit.Expected == null || edit.Replacement == null ||
                    edit.Expected.Length != 4 || edit.Replacement.Length != 4 ||
                    edit.Offset > roots[edit.Root].Length - 4)
                    throw Invalid("materialized_edit_out_of_bounds");
                for (int i = 0; i < 4; i++)
                    if (roots[edit.Root][edit.Offset + i] != edit.Expected[i])
                        throw Invalid("materialized_edit_source_value_changed");
            }
            foreach (NativeDupeByteEdit edit in plan.Edits)
                Buffer.BlockCopy(edit.Replacement, 0, roots[edit.Root], edit.Offset, 4);
            return new NativeDupeRemapPayload { Plan = plan, Roots = roots, RootHashes = roots.Select(Hash).ToArray() };
        }

        private static byte[] CopyBounded(byte[] bytes, int maximum)
        {
            if (bytes == null || bytes.Length > maximum) throw Invalid("bounded_native_input_required");
            return (byte[])bytes.Clone();
        }

        private static NativeDupeRemapContext CopyContext(NativeDupeRemapContext context)
        {
            if (context == null) return null;
            int[] ids = context.ExistingDestinationIds;
            if (ids == null || ids.Length > 200000) throw Invalid("destination_id_collision_or_unbounded_inventory");
            return new NativeDupeRemapContext {
                ArchiveVersion = context.ArchiveVersion,
                Build = context.Build, GameAssemblyHash = context.GameAssemblyHash,
                FrameworkAssemblyHash = context.FrameworkAssemblyHash,
                SourceMinionId = context.SourceMinionId, SourceProxyId = context.SourceProxyId,
                DestinationMinionId = context.DestinationMinionId, DestinationProxyId = context.DestinationProxyId,
                ExistingDestinationIds = (int[])ids.Clone(),
                SourceSeconds = context.SourceSeconds, SourceWholeCycle = context.SourceWholeCycle,
                SourceFractionalCycles = context.SourceFractionalCycles,
                DestinationSeconds = context.DestinationSeconds, DestinationWholeCycle = context.DestinationWholeCycle,
                DestinationFractionalCycles = context.DestinationFractionalCycles,
                DestinationX = context.DestinationX, DestinationY = context.DestinationY,
                DestinationWorldId = context.DestinationWorldId, DestinationWorldX = context.DestinationWorldX,
                DestinationWorldY = context.DestinationWorldY, DestinationWorldWidth = context.DestinationWorldWidth,
                DestinationWorldHeight = context.DestinationWorldHeight
            };
        }
    }
}
