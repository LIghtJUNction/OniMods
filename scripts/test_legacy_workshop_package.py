#!/usr/bin/env python3
"""Exercise the publisher's offline ZIP preflight without starting Steam."""

import os
import hashlib
import subprocess
import tempfile
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PUBLISHER = (
    ROOT
    / "tools/OniMods.SteamPublisher/bin/Release/net10.0/OniMods.SteamPublisher.dll"
)
assert (PUBLISHER.parent / "steam_appid.txt").read_text().strip() == "636750"
assert (PUBLISHER.parent / "consumer/steam_appid.txt").read_text().strip() == "457140"
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
        title = "ONI MCP Server (Test)" if name == "OniMcp" else name
        vdf = root / "workshop.vdf"
        vdf.write_text(
            '"workshopitem"\n{\n'
            '"appid" "457140"\n'
            f'"publishedfileid" "{item_id}"\n'
            f'"contentfolder" "{content}"\n'
            f'"previewfile" "{preview}"\n'
            f'"title" "{title}"\n'
            '"description" "Combined description"\n'
            '"changenote" "Test"\n'
            '}\n',
            encoding="utf-8",
        )
        env = os.environ.copy()
        env.update(
            ONIM_PUBLISH_CREATOR_APP_ID="636750",
            ONIM_PUBLISH_CONSUMER_APP_ID="457140",
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
            assert "creatorApp=636750" in result.stdout
            assert "consumerApp=457140" in result.stdout
            with zipfile.ZipFile(archive) as zip_file:
                assert set(zip_file.namelist()) >= {
                    "mod.yaml",
                    "mod_info.yaml",
                    f"{name}.dll",
                    "docs/steam-description-en.md",
                }
                assert zip_file.testzip() is None
            changed = subprocess.run(
                [
                    "dotnet", str(PUBLISHER), "--verify-installed", "--zip", str(archive),
                    "--previous-updated", "0", "--expected-sha256", "0" * 64,
                ],
                capture_output=True,
                text=True,
                check=False,
                env=env,
            )
            assert changed.returncode != 0
            assert "ZIP changed after upload" in changed.stderr
            prepared = subprocess.run(
                ["dotnet", str(PUBLISHER), "--prepare-private-candidate", "--vdf", str(vdf)],
                capture_output=True,
                text=True,
                check=False,
                env=env,
            )
            assert prepared.returncode == 0, prepared.stderr
            expected_title = (
                "ONI MCP Server [Private Legacy Test Candidate for 3731864673]"
                if name == "OniMcp"
                else "CycleTrim [Private Legacy Test Candidate for 3766318556]"
            )
            journal_prefix = "onimcp" if name == "OniMcp" else "cycletrim"
            assert f"candidateTitle={expected_title}" in prepared.stdout
            assert "candidateVisibility=Private" in prepared.stdout
            assert f"sourceWorkshopId={item_id}" in prepared.stdout
            assert f"{journal_prefix}-legacy-candidate-from-{item_id}.jsonl" in prepared.stdout
            assert "creatorApp=636750" in prepared.stdout
            assert "consumerApp=457140" in prepared.stdout
            assert (
                "legacyZipSha256=" + hashlib.sha256(archive.read_bytes()).hexdigest().upper()
                in prepared.stdout
            )
            assert (
                "previewSha256=" + hashlib.sha256(preview.read_bytes()).hexdigest().upper()
                in prepared.stdout
            )
            assert "Steam API not initialized" in prepared.stdout
            assert "[S_API]" not in prepared.stderr
            plan_hash = next(
                line.split("=", 1)[1]
                for line in prepared.stdout.splitlines()
                if line.startswith("planSha256=")
            )
            repeated = subprocess.run(
                ["dotnet", str(PUBLISHER), "--prepare-private-candidate", "--vdf", str(vdf)],
                capture_output=True,
                text=True,
                check=False,
                env=env,
            )
            assert repeated.returncode == 0, repeated.stderr
            assert f"planSha256={plan_hash}" in repeated.stdout
            rejected_plan = subprocess.run(
                [
                    "dotnet", str(PUBLISHER), "--create-private-candidate", "--vdf", str(vdf),
                    "--expected-plan-sha256", "0" * 64, "--confirm-private-create-once",
                ],
                capture_output=True,
                text=True,
                check=False,
                env=env,
            )
            assert rejected_plan.returncode != 0
            assert "exact planSha256" in rejected_plan.stderr
            missing_confirmation = subprocess.run(
                [
                    "dotnet", str(PUBLISHER), "--create-private-candidate", "--vdf", str(vdf),
                    "--expected-plan-sha256", plan_hash,
                ],
                capture_output=True,
                text=True,
                check=False,
                env=env,
            )
            assert missing_confirmation.returncode != 0
            assert "exact planSha256" in missing_confirmation.stderr
            assert "[S_API]" not in missing_confirmation.stderr
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

for wrong_role in ("ONIM_PUBLISH_CREATOR_APP_ID", "ONIM_PUBLISH_CONSUMER_APP_ID"):
    env = os.environ.copy()
    env[wrong_role] = "1"
    rejected = subprocess.run(
        ["dotnet", str(PUBLISHER), "--validate-vdf", "--vdf", "/missing.vdf"],
        capture_output=True,
        text=True,
        check=False,
        env=env,
    )
    assert rejected.returncode != 0
    assert "App ID hint must be" in rejected.stderr

wrong_launch = os.environ.copy()
wrong_launch.update(SteamAppId="636750", SteamGameId="636750")
wrong_consumer = subprocess.run(
    ["dotnet", str(PUBLISHER), "--query-only", "--consumer-context"],
    capture_output=True,
    text=True,
    check=False,
    env=wrong_launch,
)
assert wrong_consumer.returncode != 0
assert "Set it before starting the process" in wrong_consumer.stderr

unconfirmed_promotion = subprocess.run(
    ["dotnet", str(PUBLISHER), "--stage-private-metadata"],
    capture_output=True,
    text=True,
    check=False,
)
assert unconfirmed_promotion.returncode != 0
assert "requires --confirm-promotion-stage" in unconfirmed_promotion.stderr
assert "[S_API]" not in unconfirmed_promotion.stderr
print("legacy Workshop package and directory-transport guard passed")
