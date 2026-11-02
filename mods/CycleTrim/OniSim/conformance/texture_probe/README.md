# Graphical property texture probe

This is a separate, opt-in test mod for the pinned Linux 744825 installation.
It is excluded from CycleTrim's production compilation and ZIP. It contains no
game assemblies or assets. Compile against the locally installed game:

```sh
python3 build.py --managed-path /path/to/OxygenNotIncluded_Data/Managed
```

The build writes a receipt under `../artifacts/texture-probe-build/`, binding the
exact source files, builder, installed managed reference and output DLL hashes.
`verify_receipt` must pass before staging the DLL. Failed builds remove the old
receipt and output DLL; a previous binary is not an acceptable substitute.

With ONI stopped, stage that verified DLL and the two YAML files as a separate
Local test mod. Record the original mod settings and any controller files before
enabling it. Launch through OniHarness/Steam with explicit `--graphics normal`
and `--game-arg=--cycletrim-texture-probe`. The probe requires the accompanying
`--oniharness-control` directory, the pinned managed hash, a graphics device and
async GPU readback support. Without the explicit probe flag it installs no hooks.

After loading or creating a colony, verify pause and write the following request
to `texture-probe-request.json` inside that run's control directory:

```json
{"request": 1}
```

Use an increasing integer for later requests. After the original
`PropertyTextures.LateUpdate` completes, the probe compares all bytes of the five
native CPU buffers with Unity's retained texture data and with async GPU
readback. The fields are Flow (8 bytes/cell), Liquid, LiquidData and MaterialData
(4 each), and ExposedToSunlight (1). No upload or simulation method is replaced.

Wait for the matching `texture-probe.json` request with `complete=true`,
`passed=true`, five texture records, and both `cpuEqual` and `gpuEqual` true for
each record. Missing records, readback errors, wrong dimensions and deadlines
fail acceptance. Preserve failed receipts. An armed probe or a visible window
alone is not a successful texture comparison. Copy each completed receipt before
issuing the next request.

`validate.validate_observation(data, expected_request)` independently checks
the request, device, dimensions, all five formats and full byte counts, completion
flags and equal SHA-256 values. Use it before accepting or publishing a receipt.
It checks consistency of recorded observations; it cannot replace capturing the
actual probe output from the verified process.

The probe also records the first managed backwall input whose element index is
outside the registered table. This observes the `0xffff` null sentinel used by
the game; it never rewrites the input. Diagnostic file failures are logged and
do not interrupt the original setter.

The readback and copies add overhead, so do not use this run for FPS claims.
Restore only the recorded test files/settings after stopping the owned process,
including its original `mod_load_in_progress` flag if startup was interrupted.
Never clear an unknown prior loader flag or disable the game's safe-mode policy.
