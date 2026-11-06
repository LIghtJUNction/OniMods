# Exact reciprocal quotient experiment

This optional experiment is **not adopted**. The production source and the
runtime-tested c301cc3e candidate are unchanged.

Lean 4.19.0 with Std checks nine theorems. For `0 <= n < base` and `d > 0`,
`q = floor(n * floor(base / d) / base)` is at most one below `floor(n / d)`.
One remainder comparison corrects it exactly. With `base = 2^64` and `d >= 2`,
the cached reciprocal fits u64 and the intermediate product fits u128.
No incomplete proofs or added axioms are used. The dependencies are the standard
`propext`, `Classical.choice`, and `Quot.sound`.

```sh
rtk proxy /path/to/lean-4.19.0/bin/lean -DwarningAsError=true \
  mods/CycleTrim/OniSim/lean-lab/reciprocal-proof/ReciprocalIndex.lean
```

This is an arithmetic-model proof. It does not verify Rust code generation,
signed index conversion, cache lifetime, FFI integration, or game execution.
The private Rust prototype passed two million unsigned-division cases,
600,000 generated signed-index cases and exhaustive padded-grid fixtures,
also with overflow checks. Integration tested the actual game_index helper;
34 Rust tests and 227 cases / 9103 publications with both one/four workers pass.

The [source proposal](../../conformance/proposals/reciprocal-index.patch) caches
one 16-byte divisor field per world and adds no heap allocation. It remains a
proposal: eight alternating pairs in six native scenes failed the predeclared
performance gates. The best median CPU reduction was about 2.7%, below 5%,
and the thermal median increased about 3.5%, above the 3% regression limit.
See [the exact result](../../conformance/results/linux-744825-reciprocal-index.json).
These are native fixture measurements, not game FPS or whole-game memory gains.
