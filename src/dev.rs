use anyhow::{Context, Result, bail};
use serde_json::Value;
use std::fs;

use crate::archive;
use crate::build;
use crate::config::{Config, SelectedMod};
use crate::game_process;
use crate::identity::{self, KeepSource, SourceKind};
use crate::mod_config_backup;

pub fn run(cfg: &Config, selected: &SelectedMod) -> Result<()> {
    game_process::ensure_stopped()?;
    let source_dir = selected.config.project_abs(&cfg.repo_root);
    let static_id =
        identity::static_id_from_project(&source_dir).context("源码缺少可确认的 Mod staticID")?;
    let keep = KeepSource {
        kind: SourceKind::Dev,
        id: selected.name.clone(),
    };
    identity::preflight(cfg, &static_id, Some(&keep))?;
    let package = build::run(cfg, selected, false)?;

    let dev_dir = cfg.dev_mod_dir(&selected.name)?;
    println!("\n🔧 安装到 Dev 目录：{}", dev_dir.display());

    archive::install(&package.archive, &dev_dir, &package.assembly_name, || {
        enable_dev_mod(cfg, selected, &static_id)?;
        identity::enforce_single_source(cfg, &static_id, Some(&keep))
    })?;
    remove_legacy_dev_copy(cfg, &selected.name)?;

    println!("✅ 已安装到游戏 Dev 目录");
    println!("💡 启动游戏后在 Mod 列表中确认启用即可");

    Ok(())
}

fn remove_legacy_dev_copy(cfg: &Config, name: &str) -> Result<()> {
    let legacy = cfg.legacy_dev_mod_dir(name)?;
    let current = cfg.dev_mod_dir(name)?;
    let metadata = match fs::symlink_metadata(&legacy) {
        Ok(metadata) => metadata,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(error) => {
            return Err(error)
                .with_context(|| format!("检查旧 dev 目录失败：{}", legacy.display()));
        }
    };
    if metadata.file_type().is_symlink() {
        return Ok(());
    }
    if !metadata.is_dir() {
        bail!("旧 dev 路径不是普通目录，已保留：{}", legacy.display());
    }
    if let (Ok(legacy_real), Ok(current_real)) =
        (fs::canonicalize(&legacy), fs::canonicalize(&current))
        && legacy_real == current_real
    {
        return Ok(());
    }
    fs::remove_dir_all(&legacy)
        .with_context(|| format!("删除旧 dev 目录失败：{}", legacy.display()))?;
    println!("🧹 已删除旧版小写 dev 目录：{}", legacy.display());
    Ok(())
}

fn enable_dev_mod(cfg: &Config, selected: &SelectedMod, static_id: &str) -> Result<()> {
    let mods_file = cfg.game_mods_dir()?.join("mods.json");
    if !mods_file.exists() {
        println!(
            "⚠️  未找到 mods.json，首次进游戏后仍需确认启用 '{}'",
            selected.name
        );
        return Ok(());
    }

    let content = fs::read_to_string(&mods_file)
        .with_context(|| format!("读取 mods.json 失败：{}", mods_file.display()))?;
    let mut data: Value = serde_json::from_str(&content)
        .with_context(|| format!("解析 mods.json 失败：{}", mods_file.display()))?;
    let (before_enabled, after_enabled) = {
        let mods = data
            .get_mut("mods")
            .and_then(Value::as_array_mut)
            .context("mods.json 缺少 mods 数组")?;
        let before_enabled = count_enabled_mods(mods);

        let mut found = false;
        for item in mods.iter_mut() {
            let same_dev_id = item
                .get("label")
                .and_then(|label| label.get("id"))
                .and_then(Value::as_str)
                .map(|value| value == selected.name)
                .unwrap_or(false);
            let is_dev = item
                .get("label")
                .and_then(|label| label.get("distribution_platform"))
                .and_then(Value::as_u64)
                == Some(4);
            if same_dev_id && is_dev {
                item["enabled"] = Value::Bool(true);
                item["status"] = Value::Number(1.into());
                ensure_expansion_enabled(item);
                found = true;
            }
        }

        if !found {
            mods.push(new_dev_mod_entry(selected, static_id));
        }

        (before_enabled, count_enabled_mods(mods))
    };
    data["mod_load_in_progress"] = Value::Bool(false);
    if after_enabled < before_enabled {
        anyhow::bail!(
            "refusing to write mods.json: enabled mod count would drop from {} to {}",
            before_enabled,
            after_enabled
        );
    }
    if after_enabled <= 1 {
        println!(
            "⚠️  mods.json currently has only {} enabled mod(s). If this save depends on other mods, restore them in the ONI Mods menu before loading the save.",
            after_enabled
        );
    }

    let formatted = serde_json::to_string_pretty(&data)?;
    let update =
        mod_config_backup::write_config(&mods_file, &content, &format!("{}\n", formatted))?;
    let backup = update
        .backup
        .as_ref()
        .map(|path| path.display().to_string())
        .unwrap_or_else(|| "配置未变化，未生成备份".to_string());
    println!(
        "✅ 已在 mods.json 启用 Dev mod：{}（启用数 {} -> {}，备份：{}）",
        static_id, before_enabled, after_enabled, backup
    );
    if let Some(count) = update.pruned.filter(|count| *count > 0) {
        println!("🧹 清理 {} 份过期配置备份，最多保留最近 5 份", count);
    }
    if let Some(error) = update.cleanup_error {
        eprintln!("⚠️ 配置已启用，但备份清理未完成：{}", error);
    }
    Ok(())
}

fn count_enabled_mods(mods: &[Value]) -> usize {
    mods.iter()
        .filter(|item| {
            item.get("enabled").and_then(Value::as_bool) == Some(true)
                || item.get("status").and_then(Value::as_i64) == Some(1)
        })
        .count()
}

fn ensure_expansion_enabled(item: &mut Value) {
    let Some(array) = item.get_mut("enabledForDlc").and_then(Value::as_array_mut) else {
        item["enabledForDlc"] = Value::Array(vec![Value::String("EXPANSION1_ID".to_string())]);
        return;
    };
    if !array
        .iter()
        .any(|value| value.as_str() == Some("EXPANSION1_ID"))
    {
        array.push(Value::String("EXPANSION1_ID".to_string()));
    }
}

fn new_dev_mod_entry(selected: &SelectedMod, static_id: &str) -> Value {
    serde_json::json!({
     "label": {
      "distribution_platform": 4,
      "id": selected.name,
      "title": selected.name,
      "version": 0
     },
     "status": 1,
     "enabled": true,
     "enabledForDlc": ["EXPANSION1_ID"],
     "crash_count": 0,
     "reinstall_path": null,
     "staticID": static_id
    })
}
