use anyhow::{Context, Result};
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

use crate::config::Config;

struct EmbeddedFile {
    relative_path: &'static str,
    contents: &'static str,
}

const PUBLISHER_FILES: &[EmbeddedFile] = &[
    EmbeddedFile {
        relative_path: "CandidateCreationJournal.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/CandidateCreationJournal.cs"),
    },
    EmbeddedFile {
        relative_path: "consumer/steam_appid.txt",
        contents: include_str!("../tools/OniMods.SteamPublisher/consumer/steam_appid.txt"),
    },
    EmbeddedFile {
        relative_path: "FirstPublicationJournal.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/FirstPublicationJournal.cs"),
    },
    EmbeddedFile {
        relative_path: "FirstPublicationPlan.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/FirstPublicationPlan.cs"),
    },
    EmbeddedFile {
        relative_path: "FirstPublicationRunner.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/FirstPublicationRunner.cs"),
    },
    EmbeddedFile {
        relative_path: "LegacyCandidatePlan.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/LegacyCandidatePlan.cs"),
    },
    EmbeddedFile {
        relative_path: "LegacyPackage.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/LegacyPackage.cs"),
    },
    EmbeddedFile {
        relative_path: "OniMods.SteamPublisher.csproj",
        contents: include_str!("../tools/OniMods.SteamPublisher/OniMods.SteamPublisher.csproj"),
    },
    EmbeddedFile {
        relative_path: "Program.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/Program.cs"),
    },
    EmbeddedFile {
        relative_path: "PromotionStageJournal.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/PromotionStageJournal.cs"),
    },
    EmbeddedFile {
        relative_path: "SteamAppContext.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/SteamAppContext.cs"),
    },
    EmbeddedFile {
        relative_path: "steam_appid.txt",
        contents: include_str!("../tools/OniMods.SteamPublisher/steam_appid.txt"),
    },
    EmbeddedFile {
        relative_path: "SteamPublicPageReadback.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/SteamPublicPageReadback.cs"),
    },
    EmbeddedFile {
        relative_path: "SteamWorkshopCandidatePublisher.cs",
        contents: include_str!(
            "../tools/OniMods.SteamPublisher/SteamWorkshopCandidatePublisher.cs"
        ),
    },
    EmbeddedFile {
        relative_path: "SteamWorkshopFirstPublication.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/SteamWorkshopFirstPublication.cs"),
    },
    EmbeddedFile {
        relative_path: "SteamWorkshopManager.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/SteamWorkshopManager.cs"),
    },
    EmbeddedFile {
        relative_path: "SteamWorkshopPage.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/SteamWorkshopPage.cs"),
    },
    EmbeddedFile {
        relative_path: "SteamWorkshopPromotionPublisher.cs",
        contents: include_str!(
            "../tools/OniMods.SteamPublisher/SteamWorkshopPromotionPublisher.cs"
        ),
    },
    EmbeddedFile {
        relative_path: "SteamWorkshopPromotionSnapshot.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/SteamWorkshopPromotionSnapshot.cs"),
    },
    EmbeddedFile {
        relative_path: "SteamWorkshopPublisher.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/SteamWorkshopPublisher.cs"),
    },
    EmbeddedFile {
        relative_path: "VerifiedCandidateRecord.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/VerifiedCandidateRecord.cs"),
    },
    EmbeddedFile {
        relative_path: "WorkshopMetadata.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/WorkshopMetadata.cs"),
    },
    EmbeddedFile {
        relative_path: "WorkshopPromotionPlan.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/WorkshopPromotionPlan.cs"),
    },
    EmbeddedFile {
        relative_path: "WorkshopPromotionRecovery.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/WorkshopPromotionRecovery.cs"),
    },
    EmbeddedFile {
        relative_path: "WorkshopPromotionRecoveryReadback.cs",
        contents: include_str!(
            "../tools/OniMods.SteamPublisher/WorkshopPromotionRecoveryReadback.cs"
        ),
    },
    EmbeddedFile {
        relative_path: "WorkshopPromotionRunner.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/WorkshopPromotionRunner.cs"),
    },
    EmbeddedFile {
        relative_path: "WorkshopTarget.cs",
        contents: include_str!("../tools/OniMods.SteamPublisher/WorkshopTarget.cs"),
    },
];

pub fn cache_publisher_dir() -> Result<PathBuf> {
    #[cfg(target_os = "windows")]
    let base = std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(std::env::temp_dir);
    #[cfg(target_os = "macos")]
    let base = std::env::var_os("HOME")
        .map(|h| PathBuf::from(h).join("Library").join("Caches"))
        .unwrap_or_else(std::env::temp_dir);
    #[cfg(not(any(target_os = "windows", target_os = "macos")))]
    let base = std::env::var_os("XDG_CACHE_HOME")
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".cache")))
        .unwrap_or_else(std::env::temp_dir);

    Ok(base.join("onim").join("SteamPublisher"))
}

pub fn extract_publisher_to(target_dir: &Path) -> Result<()> {
    for file in PUBLISHER_FILES {
        let path = target_dir.join(file.relative_path);
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)
                .with_context(|| format!("创建目录失败: {}", parent.display()))?;
        }
        let needs_write = match fs::read_to_string(&path) {
            Ok(existing) => existing != file.contents,
            Err(_) => true,
        };
        if needs_write {
            fs::write(&path, file.contents)
                .with_context(|| format!("写入文件失败: {}", path.display()))?;
        }
    }
    Ok(())
}

/// 确保 SteamPublisher 已准备就绪并返回生成的 DLL 绝对路径。
/// 优先使用当前仓库的 `tools/OniMods.SteamPublisher/`，
/// 若不存在（如从 cargo 安装的独立 CLI），则自动解压内置的发布器到缓存目录并构建。
pub fn ensure_publisher_ready(cfg: &Config) -> Result<PathBuf> {
    let local_project = cfg
        .repo_root
        .join("tools/OniMods.SteamPublisher/OniMods.SteamPublisher.csproj");
    let project_path = if local_project.exists() {
        local_project
    } else {
        let cached_dir = cache_publisher_dir()?;
        extract_publisher_to(&cached_dir)?;
        cached_dir.join("OniMods.SteamPublisher.csproj")
    };

    let project_dir = project_path.parent().context("无法确定发布器工程目录")?;
    let dll_path = project_dir.join("bin/Release/net10.0/OniMods.SteamPublisher.dll");

    println!("   正在准备 OniMods.SteamPublisher...");
    let mut build_cmd = Command::new("dotnet");
    build_cmd
        .args([
            "build",
            project_path.to_str().unwrap(),
            "-c",
            "Release",
            "-p:NuGetAudit=false",
        ])
        .current_dir(project_dir);

    if !cfg.game_path.as_os_str().is_empty() {
        build_cmd.arg(format!("-p:OniGamePath={}", cfg.game_path.display()));
        let managed_path = cfg.managed_path();
        build_cmd.arg(format!("-p:OniManagedPath={}", managed_path.display()));
    }

    let build_status = build_cmd
        .status()
        .context("调用 dotnet 构建 SteamPublisher 失败，请确保已安装 .NET SDK")?;

    if !build_status.success() {
        anyhow::bail!("SteamPublisher 构建失败");
    }

    if !dll_path.exists() {
        anyhow::bail!("找不到发布器程序集：{}", dll_path.display());
    }

    Ok(dll_path)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn embedded_files_contain_all_necessary_sources() {
        assert!(
            PUBLISHER_FILES
                .iter()
                .any(|f| f.relative_path == "Program.cs")
        );
        assert!(
            PUBLISHER_FILES
                .iter()
                .any(|f| f.relative_path == "OniMods.SteamPublisher.csproj")
        );
        assert!(
            PUBLISHER_FILES
                .iter()
                .any(|f| f.relative_path == "steam_appid.txt")
        );
        assert!(
            PUBLISHER_FILES
                .iter()
                .any(|f| f.relative_path == "consumer/steam_appid.txt")
        );
        assert!(
            PUBLISHER_FILES
                .iter()
                .any(|f| f.relative_path == "SteamWorkshopManager.cs")
        );
        assert!(
            PUBLISHER_FILES
                .iter()
                .any(|file| file.relative_path == "SteamWorkshopPage.cs")
        );
        assert_eq!(PUBLISHER_FILES.len(), 27);
    }

    #[test]
    fn extract_publisher_writes_cleanly_to_temp_dir() -> Result<()> {
        let nonce = SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos();
        let temp = std::env::temp_dir().join(format!("onim-publisher-test-{nonce}"));
        extract_publisher_to(&temp)?;

        for file in PUBLISHER_FILES {
            assert!(
                temp.join(file.relative_path).exists(),
                "Missing extracted file: {}",
                file.relative_path
            );
        }

        fs::remove_dir_all(temp)?;
        Ok(())
    }

    #[test]
    fn cache_publisher_dir_returns_valid_path() {
        let cache_dir = cache_publisher_dir().expect("cache dir should resolve");
        assert!(cache_dir.ends_with("SteamPublisher"));
    }

    #[test]
    fn extracted_project_builds_successfully_with_dotnet() -> Result<()> {
        let nonce = SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos();
        let temp = std::env::temp_dir().join(format!("onim-build-test-{nonce}"));
        extract_publisher_to(&temp)?;

        let csproj = temp.join("OniMods.SteamPublisher.csproj");
        let mut cmd = Command::new("dotnet");
        cmd.args([
            "build",
            csproj.to_str().unwrap(),
            "-c",
            "Release",
            "-p:NuGetAudit=false",
        ])
        .current_dir(&temp)
        .env("DOTNET_CLI_USE_MSBUILD_SERVER", "false")
        .env("MSBUILDUSESERVER", "0");

        let mock_plugins = temp.join("mock_game/OxygenNotIncluded_Data/Plugins");
        let mock_managed = temp.join("mock_game/OxygenNotIncluded_Data/Managed");
        fs::create_dir_all(&mock_plugins)?;
        fs::create_dir_all(&mock_managed)?;

        let workspace_bin = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("tools/OniMods.SteamPublisher/bin/Release/net10.0");
        if workspace_bin.join("libsteam_api.so").exists() {
            fs::copy(
                workspace_bin.join("libsteam_api.so"),
                mock_plugins.join("libsteam_api.so"),
            )?;
        }
        let steamworks_assembly = workspace_bin.join("com.rlabrecque.steamworks.net.dll");
        if steamworks_assembly.exists() {
            fs::copy(
                &steamworks_assembly,
                mock_managed.join("com.rlabrecque.steamworks.net.dll"),
            )?;
        } else {
            // A clean CI runner has no ONI installation. The extracted publisher
            // still gets file-layout coverage above, but compiling it requires
            // the game-shipped Steamworks.NET assembly. Do not turn that missing
            // external SDK into a false Rust-core regression.
            fs::remove_dir_all(&temp)?;
            println!(
                "SKIP publisher build smoke test: Steamworks.NET is unavailable;                  install ONI or provide com.rlabrecque.steamworks.net.dll in the                  publisher build output to enable this integration check"
            );
            return Ok(());
        }

        cmd.arg(format!(
            "-p:OniGamePath={}",
            temp.join("mock_game").display()
        ));
        cmd.arg(format!("-p:OniManagedPath={}", mock_managed.display()));

        if !std::path::Path::new("/run/user/1000").exists() {
            cmd.env("XDG_RUNTIME_DIR", std::env::temp_dir());
        }

        let status = cmd.status()?;

        assert!(
            status.success(),
            "dotnet build of extracted publisher should succeed"
        );
        assert!(
            temp.join("bin/Release/net10.0/OniMods.SteamPublisher.dll")
                .exists(),
            "Compiled DLL must exist"
        );

        fs::remove_dir_all(temp)?;
        Ok(())
    }
}
