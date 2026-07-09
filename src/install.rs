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

    // 1. 构建 Release
    build::run(cfg, selected, true)?;

    // 2. 安装到 Local 目录
    let dist = cfg.dist_dir(&repo_root);
    let assembly_name = selected.assembly_name(&repo_root);
    let zip = dist.join(format!("{}.zip", assembly_name));

    if !zip.exists() {
        anyhow::bail!("找不到构建产物：{}", zip.display());
    }

    let local_dir = cfg.local_mod_dir(&selected.name)?;
    println!("\n📥 安装到 Local 目录：{}", local_dir.display());

    if local_dir.exists() {
        fs::remove_dir_all(&local_dir)
            .with_context(|| format!("清理旧 Local 目录失败：{}", local_dir.display()))?;
    }
    fs::create_dir_all(&local_dir)
        .with_context(|| format!("创建 Local 目录失败：{}", local_dir.display()))?;

    unzip(&zip, &local_dir)?;

    println!("✅ 已正式安装到游戏 Local 目录");
    println!("   启动游戏 → Mod 列表 → 启用 '{}'", selected.name);

    Ok(())
}

fn unzip(zip: &PathBuf, dest: &PathBuf) -> Result<()> {
    #[cfg(target_os = "windows")]
    {
        // Quote paths: unquoted Expand-Archive splits on spaces in
        // "Oxygen Not Included" and fails with ParameterBindingException.
        let zip_s = zip.to_string_lossy().replace('\'', "''");
        let dest_s = dest.to_string_lossy().replace('\'', "''");
        let ps = format!(
            "Expand-Archive -LiteralPath '{}' -DestinationPath '{}' -Force",
            zip_s, dest_s
        );
        let status = Command::new("powershell")
            .args(["-NoProfile", "-Command", &ps])
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
