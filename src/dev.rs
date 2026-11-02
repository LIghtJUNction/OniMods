use anyhow::{Context, Result};
use serde_json::Value;
use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

use crate::archive;
use crate::build;
use crate::config::{Config, SelectedMod};
use crate::mod_config_backup;

pub fn run(cfg: &Config, selected: &SelectedMod) -> Result<()> {
    let repo_root = match env::var_os("ONI_CLI_REPO_ROOT") {
        Some(path) => PathBuf::from(path),
        None => env::current_dir().context("读取当前目录失败")?,
    };

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

    archive::install(&zip, &dev_dir, &assembly_name, || {
        enable_dev_mod(cfg, selected, &dev_dir)
    })?;
    if distinct_legacy_directory(&legacy_dev_dir, &dev_dir).unwrap_or_else(|error| {
        eprintln!("⚠️ 新 Dev 模组已安装，但无法确认旧目录身份，已保留：{error:#}");
        false
    }) && let Err(error) = fs::remove_dir_all(&legacy_dev_dir)
    {
        eprintln!(
            "⚠️ 新 Dev 模组已安装，但旧版小写 dev 目录清理失败：{}：{error}",
            legacy_dev_dir.display()
        );
    }

    println!("✅ 已安装到游戏 Dev 目录");

    if is_game_running() {
        println!("⚠️  检测到游戏正在运行，需要重启游戏才能加载新版本的 Mod");
    } else {
        println!("💡 游戏未运行，启动游戏后在 Mod 列表中启用即可");
    }

    Ok(())
}

fn distinct_legacy_directory(legacy: &Path, current: &Path) -> Result<bool> {
    if !legacy.try_exists()? {
        return Ok(false);
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::MetadataExt;
        let old = fs::metadata(legacy)?;
        let new = fs::metadata(current)?;
        Ok(old.dev() != new.dev() || old.ino() != new.ino())
    }
    #[cfg(not(unix))]
    {
        let old = fs::canonicalize(legacy)?;
        let new = fs::canonicalize(current)?;
        // Dev/dev can be the same directory on a case-insensitive filesystem.
        // Retain an ambiguous alias rather than deleting the new installation.
        Ok(!old
            .to_string_lossy()
            .eq_ignore_ascii_case(&new.to_string_lossy()))
    }
}

fn enable_dev_mod(cfg: &Config, selected: &SelectedMod, dev_dir: &Path) -> Result<()> {
    let static_id = read_static_id(dev_dir).unwrap_or_else(|| format!("local.{}", selected.name));
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
            mods.push(new_dev_mod_entry(selected, &static_id));
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

fn read_static_id(dev_dir: &Path) -> Option<String> {
    let content = fs::read_to_string(dev_dir.join("mod.yaml")).ok()?;
    content.lines().find_map(|line| {
        let (key, value) = line.split_once(':')?;
        if key.trim() != "staticID" {
            return None;
        }
        let cleaned = value
            .trim()
            .trim_matches('"')
            .trim_matches('\'')
            .to_string();
        if cleaned.is_empty() {
            None
        } else {
            Some(cleaned)
        }
    })
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

fn is_game_running() -> bool {
    // Match the game process name only. Never use full-command-line matching
    // (e.g. `pgrep -f`): Steam uploader paths like
    // `.../OxygenNotIncludedUploader/OniUploader64` would false-positive.
    #[cfg(target_os = "linux")]
    {
        process_name_running(&["OxygenNotIncluded", "OxygenNotIncluded.x86_64"])
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
        process_name_running(&["OxygenNotIncluded"])
    }
}

/// True if any of the exact process names is running (`pgrep -x`).
#[cfg(any(target_os = "linux", target_os = "macos"))]
fn process_name_running(names: &[&str]) -> bool {
    names.iter().any(|name| {
        Command::new("pgrep")
            .args(["-x", name])
            .output()
            .map(|o| o.status.success())
            .unwrap_or(false)
    })
}
