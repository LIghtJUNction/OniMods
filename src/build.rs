use anyhow::{Context, Result};
use std::env;
use std::path::PathBuf;
use std::process::Command;

use crate::config::{Config, SelectedMod};

pub fn run(cfg: &Config, selected: &SelectedMod, release: bool) -> Result<()> {
    let repo_root = env::var_os("ONI_CLI_REPO_ROOT")
        .map(PathBuf::from)
        .unwrap_or_else(|| env::current_dir().unwrap());

    let mod_project = selected.config.project_abs(&repo_root);
    println!("📦 构建 Mod: {} ({})", selected.name, mod_project.display());

    let mut cmd = Command::new("dotnet");
    cmd.arg("build").current_dir(&mod_project);

    if release {
        cmd.args(["-c", "Release"]);
        println!("   模式: Release");
    } else {
        println!("   模式: Debug");
    }

    let status = cmd
        .status()
        .context("执行 dotnet build 失败，请确认已安装 .NET SDK")?;

    if !status.success() {
        anyhow::bail!("dotnet build 失败");
    }

    // 显示产物
    let dist = cfg.dist_dir(&repo_root);
    let assembly_name = selected.assembly_name(&repo_root);
    let zip = dist.join(format!("{}.zip", assembly_name));
    let src = dist.join(format!("{}-src.tar.gz", assembly_name));

    println!("✅ 构建成功！");
    if zip.exists() {
        println!("   📁 {}", zip.display());
    }
    if src.exists() {
        println!("   📁 {}", src.display());
    }

    Ok(())
}
