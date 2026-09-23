#!/usr/bin/env python3
"""Exercise the publisher's offline ZIP preflight without starting Steam."""

import os
import subprocess
import tempfile
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PUBLISHER = (
    ROOT
    / "tools/OniMods.SteamPublisher/bin/Release/net10.0/OniMods.SteamPublisher.dll"
)
TARGETS = (
    ("OniMcp", "3731864673"),
    ("CycleTrim", "3766318556"),
)


def run_preflight(
    name: str, item_id: str, *, include_dll: bool, hazard: str | None = None
) -> subprocess.CompletedProcess[str]:
    with tempfile.TemporaryDirectory(prefix="onim-legacy-package-") as temporary:
        root = Path(temporary)
        content = root / name
        content.mkdir()
        (content / "mod.yaml").write_text("title: Test\n", encoding="utf-8")
        (content / "mod_info.yaml").write_text("version: 1\n", encoding="utf-8")
        if include_dll:
            (content / f"{name}.dll").write_bytes(b"test assembly")
        (content / "docs").mkdir()
        (content / "docs" / "steam-description-en.md").write_text(
            "English description", encoding="utf-8"
        )
        if hazard == "file-link":
            private = root / "private.txt"
            private.write_text("dummy private content", encoding="utf-8")
            (content / "readme-linked.txt").symlink_to(private)
        elif hazard == "directory-link":
            private = root / "private"
            private.mkdir()
            (private / "dummy.txt").write_text("private", encoding="utf-8")
            (content / "linked-directory").symlink_to(private, target_is_directory=True)
        elif hazard == "root-link":
            alias = root / f"{name}-alias"
            alias.symlink_to(content, target_is_directory=True)
            content = alias
        elif hazard is not None:
            (content / "docs" / hazard).write_text("dummy private content", encoding="utf-8")
        preview = root / "preview.png"
        preview.write_bytes(b"test preview")
        vdf = root / "workshop.vdf"
        vdf.write_text(
            '"workshopitem"\n{\n'
            '"appid" "457140"\n'
            f'"publishedfileid" "{item_id}"\n'
            f'"contentfolder" "{content}"\n'
            f'"previewfile" "{preview}"\n'
            f'"title" "{name}"\n'
            '"description" "Combined description"\n'
            '"changenote" "Test"\n'
            '}\n',
            encoding="utf-8",
        )
        env = os.environ.copy()
        env.update(
            ONIM_PUBLISH_APP_ID="457140",
            ONIM_PUBLISH_WORKSHOP_ID=item_id,
            ONIM_PUBLISH_NAME=name,
        )
        result = subprocess.run(
            ["dotnet", str(PUBLISHER), "--validate-vdf", "--vdf", str(vdf)],
            capture_output=True,
            text=True,
            check=False,
            env=env,
        )
        archive = root / f"{name}.workshop-legacy.zip"
        if include_dll and hazard is None:
            assert result.returncode == 0, result.stderr
            assert archive.is_file(), "preflight did not stage a single ZIP file"
            assert f"legacyZip={archive}" in result.stdout
            with zipfile.ZipFile(archive) as zip_file:
                assert set(zip_file.namelist()) >= {
                    "mod.yaml",
                    "mod_info.yaml",
                    f"{name}.dll",
                    "docs/steam-description-en.md",
                }
                assert zip_file.testzip() is None
        elif not include_dll:
            assert result.returncode != 0, "missing DLL passed package validation"
            assert f"missing root entry {name}.dll" in result.stderr
        else:
            assert result.returncode != 0, f"hazard {hazard} passed package validation"
            assert not archive.exists(), f"hazard {hazard} left a ZIP for upload"
            if "link" in hazard:
                assert "symbolic link" in result.stderr
            else:
                assert "sensitive file name" in result.stderr
        return result


for target_name, workshop_id in TARGETS:
    run_preflight(target_name, workshop_id, include_dll=True)
    run_preflight(target_name, workshop_id, include_dll=False)
    for sensitive in ("OniMcpConfig.json", ".env", ".env.local", "private.key", "private.pem"):
        run_preflight(target_name, workshop_id, include_dll=True, hazard=sensitive)
    if os.name != "nt":
        for linked in ("file-link", "directory-link", "root-link"):
            run_preflight(target_name, workshop_id, include_dll=True, hazard=linked)

blocked = subprocess.run(
    [str(ROOT / "scripts/publish_cycletrim_steam.sh"), "--dry-run", "--steamcmd"],
    capture_output=True,
    text=True,
    check=False,
)
assert blocked.returncode == 2, blocked.stderr
assert "cannot produce an ONI-compatible legacy item" in blocked.stderr
print("legacy Workshop package and directory-transport guard passed")
