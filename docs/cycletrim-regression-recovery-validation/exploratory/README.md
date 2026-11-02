# Exploratory evidence

These sessions preceded the final seven-pair run. They are retained, not pooled
with final measurements. Checkpoint 1 used the packed-vector union and the
experimental two-dictionary power cache. Checkpoint 2 used scalar unions and the
same power trial. Both are superseded by checkpoint 3: local-parameter bit casts,
validated conveyor entry and unchanged baseline power cache.

`whole-default-checkpoint-1-power-candidate.log` is incomplete (4 of 14 samples)
despite an observed process exit of 0; its cause was not established. It is
excluded. The separately named retry contains all 14 samples and is the input
to that exploratory session's summary. The first reference build failed during
restricted NuGet metadata retrieval; the same build passed with network access.

The retained patches describe production changes. The conveyor guard split is
for source review: later direct-entry regression tests also reference the new
entry, so reversing production code alone is not a full earlier test checkout.
The two animation patches can be reversed in order (local bitcast to scalar,
then scalar to packed) to restore the earlier helper. Original source and
assembly hashes identify each measured checkpoint. No binaries are included.
