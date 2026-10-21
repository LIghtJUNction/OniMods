# Read-only memory probe

This separate test mod is excluded from CycleTrim's production compilation and
ZIP. Build against the installed pinned game with:

```sh
rtk proxy python3 build.py --managed-path /installed/OxygenNotIncluded_Data/Managed
```

Pass the resulting receipt to `../prepare_instances.py --memory-probe-receipt
/private/build/receipt.json`. The helper verifies and stages the DLL/YAML files
and adds `--cycletrim-memory-probe` to the isolated test members. Without that
explicit game argument, the probe installs no hooks. Preparation launches no
games. Capacity forecasts now default to the observed workload's rounded-up
5 GiB per complete game plus the controller's 1 GiB host reserve.

After starting a real Steam headless instance, loading the disposable save and
verifying pause, request one snapshot:

```sh
rtk proxy python3 capture.py --harness /repo/tools/OniHarness \
  --group-root /tmp/test-group --name guest1 \
  --build-receipt /private/build/receipt.json \
  --preparation /private/rust-stage/preparation.json --output /private/first-load.json
```

Repeat with fresh output names after reloading the exact same owned save. The
collector binds the actual loaded probe DLL, process incarnation, run nonce,
save, paused native clock and request number. Failed requests remain failures;
a new invocation uses a fresh number. It never advances, reloads or pauses the
game itself. The caller must complete each existing controller operation first.

Preparation must explicitly use `--simulation-core rust` or
`--simulation-core official`. Capture checks the prepared group hash, actually
loaded CycleTrim DLL and actual native mapping before and after the request.
For the official control, also pass
`--official-library /installed/OxygenNotIncluded_Data/Plugins/libSimDLL.so`.
That control requires the pinned reference hash, matching mapping device/inode,
and no mapped Rust library, including deleted mappings. A disabled option alone
does not establish an official baseline. Use a new process for each core.

Run the game-free capture and mapping tests with:

```sh
rtk proxy python3 -m unittest discover -s . -p 'test_*.py'
```

The mapping tests use temporary ordinary files and synthetic `/proc` entries;
they establish rejection behavior, not an observed game core or Unity counter.

The probe reads Unity allocated/reserved/unused-reserved bytes, Mono used/heap
bytes, `GC.GetTotalMemory(false)`, GC collection counts and Linux process
RSS/PSS/swap counters. It performs no `GC.Collect`, asset unloading, allocator
trimming or KSM/system configuration changes. Counters overlap and are sampled
near each other; do not add them or claim exact allocator attribution.

Compilation and protocol tests do not prove these counters work in Unity/Mono.
Actual first-load/repeated-load observations and an official-library control
remain necessary before calling the previously observed growth a leak or
attributing it to Rust. Do not use an armed probe run for CPU/FPS acceptance.
