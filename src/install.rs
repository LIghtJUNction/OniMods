use anyhow::Result;

use crate::archive;
use crate::build;
use crate::config::{Config, SelectedMod};

pub fn run(cfg: &Config, selected: &SelectedMod) -> Result<()> {
    let dev_dir = cfg.dev_mod_dir(&selected.name)?;
    let legacy_dev_dir = cfg.legacy_dev_mod_dir(&selected.name)?;
    if dev_dir.exists() || (legacy_dev_dir != dev_dir && legacy_dev_dir.exists()) {
        let shadow = if dev_dir.exists() {
            dev_dir
        } else {
            legacy_dev_dir
        };
        anyhow::bail!(
            "检测到同名 Dev 模组：{}；请先执行 `onim uninstall --scope dev` 或移走该目录，再安装 Local 版本",
            shadow.display()
        );
    }

    let package = build::run(cfg, selected, true)?;

    let local_dir = cfg.local_mod_dir(&selected.name)?;
    println!("\n📥 安装到 Local 目录：{}", local_dir.display());

    archive::install(&package.archive, &local_dir, &package.assembly_name, || {
        Ok(())
    })?;

    println!("✅ 已正式安装到游戏 Local 目录");
    println!("   启动游戏 → Mod 列表 → 启用 '{}'", selected.name);

    Ok(())
}
