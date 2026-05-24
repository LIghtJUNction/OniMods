use anyhow::{Context, Result};
use std::env;
use std::fs;
use std::path::PathBuf;
use std::process::Command;

use crate::build;
use crate::config::{Config, SelectedMod};

pub fn run(cfg: &Config, selected: &SelectedMod) -> Result<()> {
    let repo_root = env::var_os("ONI_CLI_REPO_ROOT")
        .map(PathBuf::from)
        .unwrap_or_else(|| env::current_dir().unwrap());

    // 1. 构建
    build::run(cfg, selected, false)?;

    // 2. 安装到 Dev 目录
    let dist = cfg.dist_dir(&repo_root);
    let assembly_name = selected.assembly_name(&repo_root);
    let zip = dist.join(format!("{}.zip", assembly_name));

    if !zip.exists() {
        anyhow::bail!("找不到构建产物：{}", zip.display());
    }

    let dev_dir = cfg.dev_mod_dir(&selected.name)?;
    let legacy_dev_dir = cfg.legacy_dev_mod_dir(&selected.name)?;
    println!("\n🔧 安装到 Dev 目录：{}", dev_dir.display());

    if dev_dir.exists() {
        fs::remove_dir_all(&dev_dir)
            .with_context(|| format!("清理旧 Dev 目录失败：{}", dev_dir.display()))?;
    }
    if legacy_dev_dir != dev_dir && legacy_dev_dir.exists() {
        fs::remove_dir_all(&legacy_dev_dir)
            .with_context(|| format!("清理旧版小写 dev 目录失败：{}", legacy_dev_dir.display()))?;
    }
    fs::create_dir_all(&dev_dir)
        .with_context(|| format!("创建 Dev 目录失败：{}", dev_dir.display()))?;

    unzip(&zip, &dev_dir)?;

    println!("✅ 已安装到游戏 Dev 目录");

    if is_game_running() {
        println!("⚠️  检测到游戏正在运行，需要重启游戏才能加载新版本的 Mod");
    } else {
        println!("💡 游戏未运行，启动游戏后在 Mod 列表中启用即可");
    }

    Ok(())
}

fn unzip(zip: &PathBuf, dest: &PathBuf) -> Result<()> {
    #[cfg(target_os = "windows")]
    {
        let status = Command::new("powershell")
            .args([
                "-Command",
                "Expand-Archive",
                "-Path",
                &zip.to_string_lossy(),
                "-DestinationPath",
                &dest.to_string_lossy(),
                "-Force",
            ])
            .status()
            .context("解压失败（PowerShell Expand-Archive）")?;
        if !status.success() {
            anyhow::bail!("解压失败");
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        let status = Command::new("unzip")
            .args(["-o", &zip.to_string_lossy(), "-d", &dest.to_string_lossy()])
            .status()
            .context("解压失败（unzip），请确认已安装 unzip")?;
        if !status.success() {
            anyhow::bail!("解压失败");
        }
    }

    Ok(())
}

fn is_game_running() -> bool {
    #[cfg(target_os = "linux")]
    {
        Command::new("pgrep")
            .arg("-f")
            .arg("OxygenNotIncluded")
            .output()
            .map(|o| o.status.success())
            .unwrap_or(false)
    }

    #[cfg(target_os = "windows")]
    {
        Command::new("tasklist")
            .arg("/FI")
            .arg("IMAGENAME eq OxygenNotIncluded.exe")
            .output()
            .map(|o| String::from_utf8_lossy(&o.stdout).contains("OxygenNotIncluded.exe"))
            .unwrap_or(false)
    }

    #[cfg(target_os = "macos")]
    {
        Command::new("pgrep")
            .arg("-x")
            .arg("OxygenNotIncluded")
            .output()
            .map(|o| o.status.success())
            .unwrap_or(false)
    }
}
