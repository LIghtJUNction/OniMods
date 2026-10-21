#!/usr/bin/env python3
"""Prepare a separate CycleTrim group without launching or changing any game."""
import argparse
import json
from pathlib import Path
import shutil
import secrets
import stat
import struct
import subprocess
import sys
import zipfile

from verify_instances import digest, require, load_backend
from texture_probe.build import verify_receipt as verify_texture_probe
from memory_probe.build import verify_receipt as verify_memory_probe

PAYLOAD = ("CycleTrim.dll", "mod_info.yaml", "mod.yaml", "OniSim/liboni_sim_core.so",
           "LICENSE", "LICENSE.MIT", "LICENSING.md", "THIRD_PARTY_NOTICES.txt")
REQUIRED = set(PAYLOAD[:4])
COMPANION_FILES = ("OniHarness.Runtime.dll", "mod.yaml", "mod_info.yaml")
MCP_PAYLOAD = ("OniMcp.dll", "mod_info.yaml", "mod.yaml", "LICENSE", "LICENSE.MIT",
               "LICENSING.md", "THIRD_PARTY_NOTICES.txt", "assets/edit_mark_tool.png",
               "assets/restart_oni_steam_relay.sh")


def check_native(path):
    with path.open("rb") as stream:
        header = stream.read(64)
    require(len(header) == 64 and header[:6] == b"\x7fELF\x02\x01"
            and struct.unpack_from("<HH", header, 16) == (3, 62),
            "Require a Linux x64 shared library; this header check does not validate the ABI")


def stage_files(package, destination, payload, required, description):
    require(not destination.exists(), "Use a new staged mod directory")
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        require(required <= set(names), "Package lacks " + description)
        selected = [name for name in payload if name in names]
        require(all(names.count(name) == 1 for name in selected), "Ambiguous duplicate package entry")
        infos = [archive.getinfo(name) for name in selected]
        require(sum(item.file_size for item in infos) <= 32 * 1024 * 1024,
                "Runtime payload exceeds the bounded staging budget")
        require(all(not stat.S_ISLNK(item.external_attr >> 16) for item in infos), "Symlink payload is not supported")
        destination.mkdir(parents=True, mode=0o700)
        for name in selected:
            target = destination / name  # Names come only from the fixed allowlist.
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(name) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output, length=1024 * 1024)


def stage_package(package, destination, native=None):
    if native is not None:
        check_native(native)
    stage_files(package, destination, PAYLOAD, REQUIRED, "the real managed/native CycleTrim payload")
    target = destination / "OniSim/liboni_sim_core.so"
    if native is not None:
        expected_native = digest(native)
        shutil.copyfile(native, target)
        require(digest(target) == expected_native == digest(native), "Native override changed while copying")
    check_native(target)
    return dict(managed_sha256=digest(destination / "CycleTrim.dll"), native_sha256=digest(target))


def stage_simulation_options(destination, core):
    require(core in ("rust", "official"), "Unknown simulation core selection")
    path = destination / "CycleTrimOptions.json"
    # PLib reads this mod-local file. Do not change shared user settings or
    # infer success from the option: the actual process mapping still needs audit.
    with path.open("x") as output:
        json.dump(dict(EnableOniSim=core == "rust"), output, indent=2)
        output.write("\n")
    return digest(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--harness", required=True, type=Path)
    parser.add_argument("--group-root", required=True, type=Path)
    parser.add_argument("--stage", required=True, type=Path)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--native", type=Path, help="Explicit experimental native override; never implies acceptance")
    parser.add_argument("--mcp-package", type=Path,
                        help="Optional existing OniMcp ZIP for save/step acceptance; isolated ports/configs, not a release dependency")
    parser.add_argument("--texture-probe-receipt", type=Path,
                        help="Optional verified texture-probe build; arm only graphical members")
    parser.add_argument("--memory-probe-receipt", type=Path,
                        help="Optional verified read-only memory probe; snapshots require explicit requests")
    parser.add_argument("--expected-instance-gib", type=int, default=5,
                        help="Capacity forecast per complete game; default rounds up the observed 4.13 GiB footprint")
    parser.add_argument("--simulation-core", choices=("rust", "official"),
                        help="Explicit mod-local PLib setting for controlled comparisons; verify actual native mapping after launch")
    parser.add_argument("--game-path", required=True, type=Path)
    parser.add_argument("--companion", required=True, type=Path)
    parser.add_argument("--save", required=True, type=Path)
    parser.add_argument("--count", type=int, choices=range(4, 17), default=4)
    parser.add_argument("--threads", type=int, choices=range(1, 9), default=2)
    parser.add_argument("--base-port", type=int, default=29740)
    args = parser.parse_args()
    root, stage = args.group_root.resolve(), args.stage.resolve()
    require(not root.exists() and not stage.exists(), "Use new group and staging directories; existing tests are never replaced")
    require(not root.is_relative_to(stage) and not stage.is_relative_to(root), "Keep controller root and staged source separate")
    require(len(str(root / "controller.sock").encode()) <= 100, "Use a short controller root for its Unix socket")
    require(1024 <= args.base_port and args.base_port + args.count - 1 <= 65535, "Invalid instance port range")
    require(args.expected_instance_gib > 0, "Capacity forecast must be positive")
    package_hash, source_save_hash = digest(args.package), digest(args.save)
    mcp_package_hash = digest(args.mcp_package) if args.mcp_package else None
    texture = verify_texture_probe(args.texture_probe_receipt) if args.texture_probe_receipt else None
    memory = verify_memory_probe(args.memory_probe_receipt) if args.memory_probe_receipt else None
    controller = args.harness.resolve() / "scripts/instance_controller.py"
    controller_hash = digest(controller)
    companion_hashes = {name: digest(args.companion / name) for name in COMPANION_FILES}
    if args.native is not None:
        check_native(args.native)
    stage.mkdir(parents=True, mode=0o700)
    payload = stage_package(args.package, stage / "mod", args.native)
    options_hash = stage_simulation_options(stage / "mod", args.simulation_core) if args.simulation_core else None
    texture_hashes = {}
    if texture:
        probe = stage / "texture-probe"
        probe.mkdir()
        project = Path(__file__).resolve().parent / "texture_probe"
        files = {"CycleTrim.TextureProbe.dll": Path(texture["output_dll"]),
                 "mod.yaml": project / "mod.yaml", "mod_info.yaml": project / "mod_info.yaml"}
        for name, source in files.items():
            expected = digest(source)
            shutil.copyfile(source, probe / name)
            require(digest(probe / name) == expected == digest(source), "Texture probe changed during staging")
            texture_hashes[name] = expected
        require(texture_hashes["CycleTrim.TextureProbe.dll"] == texture["probe_sha256"],
                "Texture probe DLL no longer matches its build")
        verify_texture_probe(args.texture_probe_receipt)
    memory_hashes = {}
    if memory:
        probe = stage / "memory-probe"
        probe.mkdir()
        project = Path(__file__).resolve().parent / "memory_probe"
        files = {"CycleTrim.MemoryProbe.dll": Path(memory["output_dll"]),
                 "mod.yaml": project / "mod.yaml", "mod_info.yaml": project / "mod_info.yaml"}
        for name, source in files.items():
            expected = digest(source)
            shutil.copyfile(source, probe / name)
            require(digest(probe / name) == expected == digest(source), "Memory probe changed during staging")
            memory_hashes[name] = expected
        require(memory_hashes["CycleTrim.MemoryProbe.dll"] == memory["probe_sha256"],
                "Memory probe DLL no longer matches its build")
        verify_memory_probe(args.memory_probe_receipt)
    mcp_hashes = {}
    if args.mcp_package:
        stage_files(args.mcp_package, stage / "mcp", MCP_PAYLOAD, set(MCP_PAYLOAD[:3]), "the OniMcp runtime payload")
        mcp_hashes = {str(p.relative_to(stage / "mcp")): digest(p)
                      for p in (stage / "mcp").rglob("*") if p.is_file()}
    companion = stage / "companion"
    companion.mkdir()
    for name in COMPANION_FILES:
        shutil.copyfile(args.companion / name, companion / name)
    require(companion_hashes == {name: digest(companion / name) for name in COMPANION_FILES}
            == {name: digest(args.companion / name) for name in COMPANION_FILES},
            "Runtime companion changed while copying")
    copied_save = stage / "Initial.sav"
    shutil.copyfile(args.save, copied_save)
    require(digest(copied_save) == source_save_hash == digest(args.save), "Source save changed while copying")
    # WorldGen.CanLoad opens saves with FileMode.Open's default read/write
    # access. copy2 in the controller preserves this mode in each game profile.
    # The caller's source is only read; this private test input must be loadable.
    copied_save.chmod(0o600)
    command = ["rtk", "proxy", sys.executable, str(controller), "--root", str(root), "init",
               "--game-path", str(args.game_path.resolve()), "--companion", str(companion),
               "--save", str(copied_save), "--count", str(args.count), "--base-port", str(args.base_port),
               "--mod", json.dumps(dict(id="CycleTrim", staticID="LIghtJUNction.CycleTrim", source=str(stage / "mod")))]
    if args.mcp_package:
        command.extend(["--mod", json.dumps(dict(id="OniMcp", staticID="LIghtJUNction.OniMcp",
                                                source=str(stage / "mcp")))])
    if texture:
        command.extend(["--mod", json.dumps(dict(id="CycleTrim.TextureProbe",
                       staticID="LIghtJUNction.CycleTrim.TextureProbe", source=str(stage / "texture-probe")))])
    if memory:
        command.extend(["--mod", json.dumps(dict(id="CycleTrim.MemoryProbe",
                       staticID="LIghtJUNction.CycleTrim.MemoryProbe", source=str(stage / "memory-probe")))])
    result = subprocess.run(command, check=False, capture_output=True, text=True, timeout=30)
    (stage / "initialize.log").write_text(result.stdout + result.stderr)
    require(result.returncode == 0, "Group initialization failed; inspect the staged initialize.log")
    require(digest(controller) == controller_hash and digest(args.package) == package_hash,
            "Controller or package changed during preparation")
    config_path = root / "group.json"
    config = json.loads(config_path.read_text())
    config["expectedInstanceBytes"] = args.expected_instance_gib * 1024 ** 3
    for instance in config["instances"]:
        instance["threads"] = args.threads
        if texture and instance["graphics"] == "normal":
            instance["gameArgs"] = ["--cycletrim-texture-probe"]
        if memory:
            instance.setdefault("gameArgs", []).append("--cycletrim-memory-probe")
    config_path.write_text(json.dumps(config, indent=2) + "\n")
    mcp_configs = {}
    if args.mcp_package:
        backend, _, _ = load_backend(args.harness.resolve())
        controller_instance = backend.Controller(root)
        for instance in config["instances"]:
            path = controller_instance.paths(instance["name"])["user"] / "OniMcpConfig.json"
            path.parent.mkdir(parents=True, exist_ok=True)
            # Tokens stay only in each private profile; never put them in receipts.
            options = dict(SecurityMigrationVersion=1, Host="127.0.0.1", Port=instance["port"],
                           AuthEnabled=True, AuthToken=secrets.token_urlsafe(32), ToolTextFormat="json",
                           GlobalAutoDisinfectDisabled=False, AutoDismissTutorialNotifications=False,
                           ScreenshotCleanupEnabled=False)
            with path.open("x") as output:
                path.chmod(0o600)
                json.dump(options, output, indent=2)
                output.write("\n")
            mcp_configs[instance["name"]] = dict(port=instance["port"], sha256=digest(path))
        require(digest(args.mcp_package) == mcp_package_hash,
                "OniMcp package changed while preparing the group")
    require(digest(controller) == controller_hash, "Controller changed during preparation")
    report = dict(schema=1, prepared=True, game_instances_started=0, runtime_validated=False,
                  concurrency_validated=False, **payload, package_sha256=package_hash,
                  source_save_sha256=source_save_hash, controller_sha256=controller_hash,
                  staged_save_mode="0600", source_save_modified=False,
                  companion_sha256=companion_hashes,
                  mcp_package_sha256=mcp_package_hash, mcp_payload_sha256=mcp_hashes,
                  mcp_profile_configs=mcp_configs,
                  texture_probe_payload_sha256=texture_hashes,
                  texture_probe_build_receipt_sha256=digest(args.texture_probe_receipt) if texture else None,
                  memory_probe_payload_sha256=memory_hashes,
                  memory_probe_build_receipt_sha256=digest(args.memory_probe_receipt) if memory else None,
                  expected_instance_bytes=config["expectedInstanceBytes"],
                  reserve_bytes=config["reserveBytes"],
                  requested_simulation_core=args.simulation_core,
                  simulation_options_sha256=options_hash,
                  group_sha256=digest(config_path), instance_count=args.count, threads_per_instance=args.threads,
                  scope="Isolated prepared inputs only. Serve/start/step and actual library audit remain required.")
    (stage / "preparation.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report))


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, ValueError, zipfile.BadZipFile, subprocess.TimeoutExpired) as error:
        print("FAILED: " + str(error), file=sys.stderr)
        raise SystemExit(1)
