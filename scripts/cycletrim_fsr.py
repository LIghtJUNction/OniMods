#!/usr/bin/env python3
"""Steam launch wrapper: OpenGL ONI -> Gamescope Vulkan FSR1 compositor.

This does not enable DLSS, temporal FSR, or OniSim GPU simulation. Supply Steam's
complete %command% after --; its wrappers and argument boundaries are retained.
"""
import argparse
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess


def size(value):
    if not re.fullmatch(r"[1-9][0-9]*x[1-9][0-9]*", value):
        raise argparse.ArgumentTypeError("expected WIDTHxHEIGHT")
    width, height = map(int, value.split("x"))
    if not (320 <= width <= 16384 and 180 <= height <= 16384):
        raise argparse.ArgumentTypeError("size is outside 320x180..16384x16384")
    return width, height


def build_command(game, input_size, output_size, sharpness=5, refresh=60, device=None):
    if not game:
        raise ValueError("supply Steam's complete %command% after --")
    if not any(Path(arg).name == "steam-launch-wrapper" or arg == "SteamLaunch" for arg in game):
        raise ValueError("refusing a bare game executable; preserve the full Steam launch chain")
    if not any("OxygenNotIncluded" in Path(arg).name for arg in game):
        raise ValueError("the Steam command does not identify Oxygen Not Included")
    iw, ih = input_size
    ow, oh = output_size
    if not (iw < ow and ih < oh):
        raise ValueError("FSR requires a lower input resolution in both dimensions")
    if abs(iw * oh - ih * ow) > max(ow, oh):
        raise ValueError("input/output aspect ratios must match to within one input pixel")
    if not 0 <= sharpness <= 20 or not 1 <= refresh <= 240:
        raise ValueError("sharpness must be 0..20 and refresh must be 1..240")
    if device and not re.fullmatch(r"[0-9a-fA-F]{4}:[0-9a-fA-F]{4}", device):
        raise ValueError("Vulkan device must be VENDOR:DEVICE hexadecimal IDs")
    command = ["gamescope", "--backend", "wayland", "-w", str(iw), "-h", str(ih),
               "-W", str(ow), "-H", str(oh), "-r", str(refresh), "-F", "fsr",
               "--sharpness", str(sharpness), "--force-windows-fullscreen"]
    if device:
        command += ["--prefer-vk-device", device]
    return command + ["--"] + list(game)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-size", type=size, default=(1280, 720))
    parser.add_argument("--output-size", type=size, default=(1920, 1080))
    parser.add_argument("--sharpness", type=int, default=5)
    parser.add_argument("--refresh", type=int, default=60)
    parser.add_argument("--device")
    parser.add_argument("--print-only", action="store_true")
    parser.add_argument("game", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    game = args.game[1:] if args.game[:1] == ["--"] else args.game
    try:
        command = build_command(game, args.input_size, args.output_size,
                                args.sharpness, args.refresh, args.device)
        if args.print_only:
            print(shlex.join(command))
            return
        if os.environ.get("SteamAppId") != "457140":
            raise ValueError("launch through Steam App 457140; this script does not start Steam itself")
        if not os.environ.get("WAYLAND_DISPLAY"):
            raise ValueError("this launch profile requires a Wayland desktop session")
        if not shutil.which("gamescope"):
            raise ValueError("gamescope is not installed")
        help_result = subprocess.run(["gamescope", "--help"], capture_output=True,
                                     text=True, check=True)
        help_text = help_result.stdout + help_result.stderr
        if "fsr => AMD FidelityFX" not in help_text:
            raise ValueError("installed Gamescope does not advertise its FSR filter")
        os.execvp(command[0], command)
    except (ValueError, OSError, subprocess.SubprocessError) as error:
        parser.exit(2, f"cycletrim-fsr: {error}\n")


if __name__ == "__main__":
    main()
