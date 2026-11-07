import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location(
    "cycletrim_fsr", Path(__file__).resolve().parents[1] / "scripts/cycletrim_fsr.py")
fsr = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fsr)


class SteamFsrLaunchTests(unittest.TestCase):
    def setUp(self):
        self.steam = ["/path with spaces/steam-launch-wrapper", "--", "/steam/reaper",
                      "SteamLaunch", "AppId=457140", "--", "/steam/runtime/_v2-entry-point",
                      "--verb=waitforexitandrun", "--", "/game/OxygenNotIncluded",
                      "-logFile", "/log path/player.log"]

    def test_retains_complete_steam_chain_and_argument_boundaries(self):
        command = fsr.build_command(self.steam, (1280, 720), (1920, 1080))
        self.assertEqual(command[-len(self.steam):], self.steam)
        self.assertEqual(command[-len(self.steam)-1], "--")
        self.assertEqual(command[command.index("-F")+1], "fsr")
        self.assertNotIn("-force-vulkan", command)

    def test_rejects_bare_executable(self):
        with self.assertRaises(ValueError):
            fsr.build_command(["/game/OxygenNotIncluded"], (1280, 720), (1920, 1080))

    def test_rejects_wrong_game(self):
        with self.assertRaises(ValueError):
            fsr.build_command(["steam-launch-wrapper", "OtherGame"], (1280, 720), (1920, 1080))

    def test_rejects_no_upscale_and_stretched_aspect(self):
        for source, target in [((1920,1080), (1920,1080)), ((1280,720), (1920,1200))]:
            with self.subTest(source=source, target=target), self.assertRaises(ValueError):
                fsr.build_command(self.steam, source, target)

    def test_allows_radeon_and_nvidia_device_ids_without_shell_interpolation(self):
        for device in ["1002:73bf", "10de:28a0"]:
            command = fsr.build_command(self.steam, (1280,720), (1920,1080), device=device)
            self.assertEqual(command[command.index("--prefer-vk-device")+1], device)
        with self.assertRaises(ValueError):
            fsr.build_command(self.steam, (1280,720), (1920,1080), device="$(command)")


if __name__ == "__main__":
    unittest.main()
