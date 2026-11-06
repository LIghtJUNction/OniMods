# Mod regression checks

Run the same checks as the `Mod quality` GitHub Actions workflow:

```sh
python3 scripts/check_mods.py
```

Requirements: Python 3 and .NET SDK 10. The first C# run restores packages from
NuGet. On Linux, the conformance fixture tests also require a C compiler named
`cc` and the pinned OniHarness submodule:

```sh
git submodule update --init tools/OniHarness
```

The checks do not require a Steam installation, game assemblies, or local
`Directory.Build.props`. The Linux fixture tests compile an authored clock
fixture and initialize the controller with inert files; they do not launch ONI.
Use `--dotnet /path/to/dotnet` for an SDK outside `PATH`, or `--static-only` to run
the Python source contracts and fixture suites without .NET.

The runner discovers `scripts/verify_*.py`, game-independent `scripts/test_*.py`,
Python `test_*.py` suites immediately under `tests/*/`, and every console regression
project under `tests/`. On Linux it also runs the OniSim conformance and
memory-probe fixture suites. Each Python test directory is discovered in its own
process so similarly named modules in different suites remain isolated.

Every check exits with a nonzero code on failure. The C# projects link production
sources and use small stubs where game APIs would otherwise be required. They
exercise the linked logic; they do not validate Unity behavior or Harmony patch
compatibility. Supported and retired MCP transport scenarios run together in
`OniMcp.Server.Tests`; their [coverage and historical provenance](mcp-protocol-retirement.md)
are documented separately.

The Rust CLI has its own full regression gate:

```sh
cargo +1.99.1 test --locked
```

## Game-dependent checks

The standalone runner lists these as **NOT RUN**. Before shipping a Mod release,
configure the game path using `Directory.Build.props.example`, build both Mods,
and run:

```sh
dotnet build mods/OniMcp/OniMcp.csproj -c Debug -warnaserror
dotnet build mods/CycleTrim/CycleTrim.csproj -c Release -warnaserror
python3 scripts/verify_restart_packaging.py
python3 scripts/verify_cycletrim_release_binary.py
python3 scripts/verify_cycletrim_target_contract.py /path/to/Assembly-CSharp.dll
```

The runner also reports `test_legacy_workshop_package.py` as **NOT RUN** because
it requires a built Steam publisher and installed Steamworks files. Its
game-independent packaging and journal logic remains covered by
`OniMods.SteamPublisher.Tests`. Run the complete publisher preflight separately
after building that tool.

The CycleTrim assembly checks require `ilspycmd`. Also check the Mods inside ONI:
connect an MCP client, read resources, execute a batch containing a failing call,
restart the server with an open event stream, and load a colony with idle and
busy duplicants and moving critters. Verify that navigation, priority updates,
and immediate fallback behavior remain correct. Timing results from the policy
benchmark do not establish an in-game FPS improvement.
