use anyhow::{Context, Result};
use std::fs;

use crate::UninstallScope;
use crate::config::{Config, SelectedMod};

pub fn run(cfg: &Config, selected: &SelectedMod, scope: UninstallScope) -> Result<()> {
    let dev_dir = cfg.dev_mod_dir(&selected.name)?;
    let legacy_dev_dir = cfg.legacy_dev_mod_dir(&selected.name)?;
    let local_dir = cfg.local_mod_dir(&selected.name)?;

    let mut uninstalled = vec![];

    match scope {
        UninstallScope::Dev | UninstallScope::All => {
            if dev_dir.exists() {
                fs::remove_dir_all(&dev_dir)
                    .with_context(|| format!("卸载 Dev 目录失败：{}", dev_dir.display()))?;
                uninstalled.push(format!("Dev/{} 已删除", selected.name));
            }
            if legacy_dev_dir != dev_dir && legacy_dev_dir.exists() {
                fs::remove_dir_all(&legacy_dev_dir).with_context(|| {
                    format!("卸载旧版小写 dev 目录失败：{}", legacy_dev_dir.display())
                })?;
                uninstalled.push(format!("dev/{} 已删除（旧版错误目录）", selected.name));
            }
        }
        _ => {}
    }

    match scope {
        UninstallScope::Local | UninstallScope::All => {
            if local_dir.exists() {
                fs::remove_dir_all(&local_dir)
                    .with_context(|| format!("卸载 Local 目录失败：{}", local_dir.display()))?;
                uninstalled.push(format!("Local/{} 已删除", selected.name));
            }
        }
        _ => {}
    }

    if uninstalled.is_empty() {
        println!("ℹ️  '{}' 没有安装在任何目录中", selected.name);
    } else {
        println!("✅ 已卸载 '{}'：", selected.name);
        for msg in uninstalled {
            println!("   {}", msg);
        }
    }

    Ok(())
}
