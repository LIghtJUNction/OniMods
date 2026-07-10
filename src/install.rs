use anyhow::{Context, Result};
use std::env;
use std::fs;
use std::path::PathBuf;

use crate::archive;
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

    archive::unzip(&zip, &local_dir)?;

    println!("✅ 已正式安装到游戏 Local 目录");
    println!("   启动游戏 → Mod 列表 → 启用 '{}'", selected.name);

    Ok(())
}
