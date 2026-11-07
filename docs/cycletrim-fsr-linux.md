# Linux FSR launch profile

This experimental profile leaves ONI's renderer on OpenGL and places Gamescope's
Vulkan FSR1 compositor outside it. It is **not DLSS or temporal FSR**, is not an
OniSim simulation backend, and is not enabled in the Workshop package. It uses
an existing Gamescope installation; it does not install drivers or change Steam
settings automatically.

## Current evidence

- The installed ONI build uses Unity 6000.3.5f2. Its serialized BuildSettings
  graphics API list is `[17]` (OpenGLCore, not Vulkan's `21`). All 77 inspected
  Shader objects in global/shared asset files advertise compiler platform `15`
  (OpenGLCore), not Vulkan's `18`. This audit does not inspect every possible
  AssetBundle. Merely finding Vulkan code in UnityPlayer.so is insufficient.
- Gamescope 3.16.31 on this machine advertises the `fsr` filter as AMD FSR1.
  Its compositor is separate from the game's graphics API. NVIDIA and AMD are
  supported by the upstream approach; AMD hardware has not been tested here.
- Five launch-helper unit tests pass: full Steam wrapper/argument preservation,
  rejection of a bare executable or wrong game, input/output sizing, and Vulkan
  vendor/device selection without shell interpolation.
- A bounded headless OpenGL client ran at about 60 FPS on NVIDIA with Gamescope
  configured for FSR. Forced process-group shutdown triggered a teardown crash;
  this is not accepted as a clean successful run or as ONI visual acceptance.
  The first failed attempt also lacked a valid XDG runtime directory. Those
  failed observations are retained rather than hidden.
- A child-only timeout reproducer also segfaulted without terminating the parent
  process group. Reported upstream as
  [Gamescope #2516](https://github.com/ValveSoftware/gamescope/issues/2516).
  A symbolized faulting-library diagnosis is not established; the report does
  not attribute the crash to NVIDIA or FSR. The launcher remains opt-in.

No ONI launch, screenshot/pixel comparison or FPS improvement is established by
these synthetic tests. The next gate is a clean compositor lifecycle, then a
Steam-launched graphical test with disposable save/profile settings, followed by
native-resolution versus FSR visual and performance comparisons.

## Steam launch options

Use the repository's `scripts/cycletrim_fsr.py` as a prefix around the **existing
complete Steam launch chain**. For example, if the original launch options are
`prime-run gamemoderun python3 /path/to/steam_instance_entry.py %command%`, the
profile is:

```text
python3 /path/to/OniMods/scripts/cycletrim_fsr.py --input-size 1280x720 --output-size 1920x1080 -- prime-run gamemoderun python3 /path/to/steam_instance_entry.py %command%
```

Use actual paths and retain the original wrappers and their order. Do not replace
`%command%` with the game executable. The script requires Steam App 457140 and a
Wayland desktop, refuses a missing Steam wrapper, and does not start Steam itself.
It does not add `-force-vulkan` to ONI. `--print-only` prints the resulting command
without executing it. `--device VENDOR:DEVICE` optionally selects the compositor's
Vulkan device; it does not change the game's own GPU-selection wrappers.

Choose input/output sizes with matching aspect ratios. The input must be lower
in both dimensions. `--sharpness` ranges from 0 (strongest) to 20 (weakest), with
5 as this profile's starting value. The default requested refresh is 60 Hz.

Gamescope scales the **whole game image, including UI/text**. It cannot preserve
native-resolution ONI UI through this external path. Readability, liquid/light
effects, overlays, mouse coordinates, save/load and window/fullscreen changes
need actual game checks. Sharpening and higher FPS cannot be assumed to improve
readability or simulation tick speed.

Rollback removes only this added Python prefix and restores the original Steam
launch options. It requires no mod removal or save changes.

References: [Gamescope upstream](https://github.com/ValveSoftware/gamescope),
[FSR1's spatial input model](https://gpuopen.com/fidelityfx-superresolution/),
[NVIDIA DLSS integration guide](https://raw.githubusercontent.com/NVIDIA/DLSS/main/doc/DLSS_Programming_Guide_Release.pdf).
