# Rejected ideas and cost limits

The initial always-on thermal cache regressed random material/insulation maps. A fixed
128-lookup sample and 32-hit threshold now disable caching for low reuse within that pass.
Separately compiled bypass paths reduced the extra cost but did not eliminate every regression.
Do not tune the threshold repeatedly until this synthetic matrix happens to turn green.

Zero gas flow is not a whole-function identity: temperature rounding, germ normalization,
vacuum replacement and events can still occur. Only the mathematically bounded zero-germ
ratio is skipped. No active-tile skipping, changed traversal order, altered diffusion constants,
float reassociation or speculative multicore execution was adopted.

A small mass request stopping at the first cell does not benefit from preventing duplicate
neighbor enqueues. The measured low-nanosecond overhead is retained in the complete table;
this candidate should not yet be promoted as a universal win.

The algorithms use established stable deduplication, memoization and zero specialization.
This is a project-specific implementation with checked obligations and measurements, not a
claim of a new physical law or a previously unknown general mathematical algorithm.
