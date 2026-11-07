# PreparedStep model proof

`PreparedStep.lean` is byte-identical to the previously checked source in
`prior-verification.json` (3 theorems). The original final
checker output is retained in `prior-proof.log`. This copy reuses that exact
source verification; it is not a new Lean invocation in this directory.

Use the pinned Lean 4.19.0 installation to check it again:

```sh
rtk proxy /path/to/lean-4.19.0/bin/lean -DwarningAsError=true PreparedStep.lean
```

The model covers abstract state/effects or pure-check elimination. It does not
prove compiled Rust, libm, FFI, game compatibility or whole-game performance.
The Rust implementation and its runtime guards require their separate tests.
