# Mod regression checks

Run the same checks as the `Mod quality` GitHub Actions workflow:

```sh
python3 scripts/check_mods.py
```

Requirements: Python 3 and .NET SDK 10. The first C# run restores Newtonsoft.Json
from NuGet. The checks do not require a Steam installation, game assemblies, or
local `Directory.Build.props`. Use `--dotnet /path/to/dotnet` for an SDK outside
`PATH`, or `--static-only` to run just the Python source contracts.

The runner executes source contracts, every console regression project under
`tests/`, and the existing CycleTrim policy assertions. Each console exits with a
nonzero code on failure. These projects link production sources and use small
stubs where game APIs would otherwise be required. They exercise the linked
logic; they do not validate Unity behavior or Harmony patch compatibility.

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

The CycleTrim assembly checks require `ilspycmd`. Also check the Mods inside ONI:
connect an MCP client, read resources, execute a batch containing a failing call,
restart the server with an open event stream, and load a colony with idle and
busy duplicants and moving critters. Verify that navigation, priority updates,
and immediate fallback behavior remain correct. Timing results from the policy
benchmark do not establish an in-game FPS improvement.
