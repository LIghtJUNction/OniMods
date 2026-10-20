# OniSim status (2026-10-20)

Experimental Rust simulator snapshot, **not usable for gameplay**. Its native
engine currently has 36 previously existing integration/compiler errors.
The isolated disease-emitter implementation under `native/src/` has a
formally checked geometric optimization and standalone regression/benchmark
evidence under `lean-lab/`. Those component results do **not** establish that
the full simulator builds, runs, or increases ONI FPS. No installed game,
save, or official SimDLL is changed by this checkout.

Read [README](README.md) and [lean-lab report](lean-lab/README.md).
