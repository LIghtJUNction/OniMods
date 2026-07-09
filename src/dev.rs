use anyhow::{Context, Result};
use serde_json::Value;
use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::{SystemTime, UNIX_EPOCH};

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
    enable_dev_mod(cfg, selected, &dev_dir)?;

    println!("✅ 已安装到游戏 Dev 目录");

    if is_game_running() {
        println!("⚠️  检测到游戏正在运行，需要重启游戏才能加载新版本的 Mod");
    } else {
        println!("💡 游戏未运行，启动游戏后在 Mod 列表中启用即可");
    }

    Ok(())
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
            let same_static_id = item
                .get("staticID")
                .and_then(Value::as_str)
                .map(|value| value == static_id)
                .unwrap_or(false);
            let same_dev_id = item
                .get("label")
                .and_then(|label| label.get("id"))
                .and_then(Value::as_str)
                .map(|value| value == selected.name)
                .unwrap_or(false);
            if same_static_id || same_dev_id {
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
    let backup = mods_file.with_extension(format!(
        "json.onim-backup-{}",
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map(|duration| duration.as_secs())
            .unwrap_or(0)
    ));
    fs::write(&backup, &content)
        .with_context(|| format!("备份 mods.json 失败：{}", backup.display()))?;
    fs::write(&mods_file, format!("{}\n", formatted))
        .with_context(|| format!("写入 mods.json 失败：{}", mods_file.display()))?;
    println!(
        "✅ 已在 mods.json 启用 Dev mod：{}（启用数 {} -> {}，备份：{}）",
        static_id,
        before_enabled,
        after_enabled,
        backup.display()
    );
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
