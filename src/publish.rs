use anyhow::{Context, Result};
use std::env;
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

use crate::build;
use crate::config::{Config, SelectedMod};

#[derive(Debug, Default)]
pub struct PublishOptions {
    pub use_gui: bool,
    pub auto_note: bool,
    pub non_interactive: bool,
    pub dry_run: bool,
    pub steamcmd: Option<PathBuf>,
    pub steam_user: Option<String>,
}

fn uploader_path() -> Option<PathBuf> {
    let home = env::var_os("HOME")?;

    #[cfg(target_os = "linux")]
    {
        let p = PathBuf::from(&home)
            .join(".local/share/Steam/steamapps/common/OxygenNotIncludedUploader/OniUploader64");
        if p.exists() {
            return Some(p);
        }
    }

    #[cfg(target_os = "macos")]
    {
        let p = PathBuf::from(&home)
            .join("Library/Application Support/Steam/steamapps/common/OxygenNotIncludedUploader/OxygenNotIncludedUploader.app/Contents/MacOS/OxygenNotIncludedUploader");
        if p.exists() {
            return Some(p);
        }
    }

    #[cfg(target_os = "windows")]
    {
        for p in [
            "C:\\Program Files (x86)\\Steam\\steamapps\\common\\OxygenNotIncludedUploader\\OxygenNotIncludedUploader.exe",
            "C:\\Program Files\\Steam\\steamapps\\common\\OxygenNotIncludedUploader\\OxygenNotIncludedUploader.exe",
        ] {
            let pb = PathBuf::from(p);
            if pb.exists() {
                return Some(pb);
            }
        }
    }

    None
}

fn executable_on_path(name: &str) -> Option<PathBuf> {
    let path = env::var_os("PATH")?;
    env::split_paths(&path)
        .map(|dir| dir.join(name))
        .find(|candidate| candidate.is_file())
}

fn resolve_steamcmd(explicit: Option<&Path>) -> Option<PathBuf> {
    if let Some(path) = explicit {
        return path.is_file().then(|| path.to_path_buf());
    }
    if let Some(path) = env::var_os("STEAMCMD").map(PathBuf::from) {
        return path.is_file().then_some(path);
    }
    executable_on_path(if cfg!(target_os = "windows") {
        "steamcmd.exe"
    } else {
        "steamcmd"
    })
}

fn generate_vdf(
    dist_mod: &Path,
    preview: &Path,
    title: &str,
    description: &str,
    changenote: &str,
    publishedfileid: &str,
) -> Result<PathBuf> {
    // Keep uploader metadata outside contentfolder so it is not shipped as mod content.
    let vdf_path = dist_mod.with_extension("workshop.vdf");
    let safe_title = escape_vdf_value(title);
    let safe_description = escape_vdf_value(description);
    let safe_changenote = escape_vdf_value(changenote);
    let content = format!(
        r#""workshopitem"
{{
	"appid"		"457140"
	"publishedfileid"	"{}"
	"contentfolder"	"{}"
	"previewfile"	"{}"
	"visibility"	"0"
	"title"		"{}"
	"description"	"{}"
	"changenote"	"{}"
}}
	"#,
        publishedfileid,
        dist_mod.to_string_lossy().replace('\\', "/"),
        preview.to_string_lossy().replace('\\', "/"),
        safe_title,
        safe_description,
        safe_changenote,
    );
    fs::write(&vdf_path, content)
        .with_context(|| format!("写入 vdf 失败：{}", vdf_path.display()))?;
    Ok(vdf_path)
}

fn yaml_value(yaml: &str, key: &str) -> Option<String> {
    yaml.lines().find_map(|line| {
        let (candidate, value) = line.trim().split_once(':')?;
        (candidate.trim() == key).then(|| {
            value
                .trim()
                .trim_matches('"')
                .trim_matches('\'')
                .to_string()
        })
    })
}

fn read_mod_info(dist_mod: &Path) -> Option<(String, String, String)> {
    let yaml = fs::read_to_string(dist_mod.join("mod.yaml")).ok()?;
    let title = yaml_value(&yaml, "title").unwrap_or_else(|| "Untitled Mod".to_string());
    let description = steam_markdown_description(dist_mod)
        .or_else(|| yaml_value(&yaml, "description"))
        .unwrap_or_default();
    let version = fs::read_to_string(dist_mod.join("mod_info.yaml"))
        .ok()
        .and_then(|contents| yaml_value(&contents, "version"))
        .unwrap_or_else(|| "1.0.0".to_string());
    Some((title, description, version))
}

fn read_file(path: &Path) -> Option<String> {
    fs::read_to_string(path).ok().map(|s| s.trim().to_string())
}

fn escape_vdf_value(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('\"', "\\\"")
        .replace('\r', "")
        .replace('\n', "\\n")
}

fn steam_description_file(dist_mod: &Path, name: &str) -> Option<String> {
    read_file(&dist_mod.join("docs").join(name)).or_else(|| read_file(&dist_mod.join(name)))
}

fn steam_markdown_description(dist_mod: &Path) -> Option<String> {
    let zh = steam_description_file(dist_mod, "steam-description-zh.md");
    let en = steam_description_file(dist_mod, "steam-description-en.md");
    match (zh, en) {
        (Some(zh_txt), Some(en_txt)) if !zh_txt.is_empty() && !en_txt.is_empty() => {
            Some(format!("{}\n\n{}", zh_txt, en_txt))
        }
        (Some(zh_txt), _) if !zh_txt.is_empty() => Some(zh_txt),
        (_, Some(en_txt)) if !en_txt.is_empty() => Some(en_txt),
        _ => None,
    }
}

fn latest_changelog_summary(content: &str, max_items: usize) -> Option<String> {
    if max_items == 0 {
        return None;
    }

    let mut section_started = false;
    let mut entries: Vec<String> = Vec::new();
    for raw in content.lines() {
        let line = raw.trim();
        if line.starts_with("## ") {
            if section_started {
                break;
            }
            section_started = true;
            continue;
        }
        if !section_started || line.is_empty() {
            continue;
        }
        if let Some(entry) = line.strip_prefix("- ") {
            entries.push(format!("- {}", entry.trim()));
            if entries.len() >= max_items {
                break;
            }
        }
    }

    (!entries.is_empty()).then(|| {
        format!(
            "Auto changelog (latest {} items):\n{}",
            entries.len(),
            entries.join("\n")
        )
    })
}

fn extract_changelog_summary(project_dir: &Path, max_items: usize) -> Option<String> {
    let changelog = project_dir.join("CHANGELOG.md");
    let content = fs::read_to_string(changelog).ok()?;
    latest_changelog_summary(&content, max_items)
        .or_else(|| extract_git_summary(project_dir, max_items))
}

fn extract_git_summary(project_dir: &Path, max_items: usize) -> Option<String> {
    let max_items = max_items.to_string();
    let output = Command::new("git")
        .arg("-C")
        .arg(project_dir)
        .args([
            "log",
            "--max-count",
            &max_items,
            "--pretty=format:- %h %s",
            "--",
            ".",
        ])
        .output()
        .ok()?;

    if !output.status.success() {
        return None;
    }

    let lines = String::from_utf8_lossy(&output.stdout);
    let entries: Vec<&str> = lines
        .lines()
        .map(|line| line.trim())
        .filter(|line| !line.is_empty())
        .take(max_items.parse().ok()?)
        .collect();

    if entries.is_empty() {
        return None;
    }

    Some(format!(
        "Auto changelog (latest {} items):\n{}",
        entries.len(),
        entries.join("\n")
    ))
}

fn prompt(question: &str, default: Option<&str>) -> Result<String> {
    print!("{}", question);
    io::stdout().flush()?;
    let mut buf = String::new();
    io::stdin().read_line(&mut buf)?;
    let trimmed = buf.trim().to_string();
    if trimmed.is_empty() {
        Ok(default.unwrap_or("").to_string())
    } else {
        Ok(trimmed)
    }
}

fn steamcmd_error_summary(output: &str) -> String {
    if output.contains("Cached credentials not found") || output.contains("Invalid Password") {
        return "SteamCMD 没有有效的缓存登录。先运行 scripts/publish_cycletrim_steam.sh --login。"
            .to_string();
    }

    let lines: Vec<&str> = output
        .lines()
        .filter(|line| !line.trim().is_empty())
        .collect();
    lines[lines.len().saturating_sub(20)..].join("\n")
}

pub fn run(cfg: &Config, selected: &SelectedMod, options: PublishOptions) -> Result<()> {
    let PublishOptions {
        use_gui,
        auto_note,
        non_interactive,
        dry_run,
        steamcmd,
        steam_user,
    } = options;
    let repo_root = match env::var_os("ONI_CLI_REPO_ROOT") {
        Some(path) => PathBuf::from(path),
        None => env::current_dir().context("读取当前目录失败")?,
    };

    let assembly_name = selected.assembly_name(&repo_root);
    println!("🚀 构建发布包：{}", selected.name);
    build::run(cfg, selected, true)?;

    let dist_mod = cfg.dist_dir(&repo_root).join(&assembly_name);
    if !dist_mod.join("mod_info.yaml").exists() {
        anyhow::bail!("发布包缺少 mod_info.yaml: {}", dist_mod.display());
    }

    let preview_png = dist_mod.join("preview.png");
    let preview_jpg = dist_mod.join("preview.jpg");
    let preview = if preview_png.exists() {
        preview_png
    } else if preview_jpg.exists() {
        preview_jpg
    } else {
        anyhow::bail!(
            "发布包缺少 preview.png 或 preview.jpg: {}",
            dist_mod.display()
        );
    };

    if use_gui {
        launch_uploader(&dist_mod, &preview);
        return Ok(());
    }

    let (mod_title, description, version) = read_mod_info(&dist_mod)
        .unwrap_or_else(|| (selected.name.clone(), String::new(), "1.0.0".to_string()));
    let title = selected.config.workshop_title.clone().unwrap_or(mod_title);
    if description.trim().is_empty() {
        anyhow::bail!("Steam 描述为空，请更新 docs/steam-description-*.md");
    }
    let description_chars = description.chars().count();
    if description_chars > 8_000 {
        anyhow::bail!("Steam 描述超过 8000 字符：{}", description_chars);
    }

    let project_dir = selected.config.project_abs(&repo_root);
    let default_changenote = extract_changelog_summary(&project_dir, 6)
        .unwrap_or_else(|| format!("Release {}", version));
    let changenote = if auto_note || non_interactive || dry_run {
        default_changenote
    } else {
        prompt(
            &format!("更新说明 [默认: {}]: ", default_changenote),
            Some(default_changenote.as_str()),
        )?
    };

    let publishedfileid = if let Some(ref id) = selected.config.publishedfileid {
        id.clone()
    } else if non_interactive || dry_run {
        anyhow::bail!("无人值守发布要求在 onim.toml 配置 publishedfileid");
    } else {
        let id = prompt("已有 Workshop ID？首次上传输入 0: ", Some("0"))?;
        if id.trim().is_empty() {
            "0".to_string()
        } else {
            id
        }
    };

    let legacy_vdf = dist_mod.join("workshop.vdf");
    if legacy_vdf.exists() {
        fs::remove_file(&legacy_vdf)
            .with_context(|| format!("删除旧 VDF 失败: {}", legacy_vdf.display()))?;
    }
    let vdf = generate_vdf(
        &dist_mod,
        &preview,
        &title,
        &description,
        &changenote,
        &publishedfileid,
    )?;
    println!("   Workshop ID: {}", publishedfileid);
    println!("   标题: {}", title);
    println!("   描述: {} 字符", description_chars);
    println!("   VDF: {}", vdf.display());

    if dry_run {
        println!("✅ dry-run 通过，未调用 SteamCMD");
        return Ok(());
    }

    let Some(steamcmd_path) = resolve_steamcmd(steamcmd.as_deref()) else {
        if non_interactive {
            anyhow::bail!(
                "找不到 SteamCMD；运行 scripts/publish_cycletrim_steam.sh 可安装用户级副本"
            );
        }
        launch_uploader(&dist_mod, &preview);
        return Ok(());
    };

    let steam_user = steam_user
        .or_else(|| env::var("STEAM_USERNAME").ok())
        .or_else(|| env::var("STEAM_USER").ok())
        .filter(|value| !value.trim().is_empty());
    let steam_user = match steam_user {
        Some(user) => user,
        None if non_interactive => {
            anyhow::bail!("无人值守发布要求 --steam-user 或 STEAM_USERNAME")
        }
        None => prompt("Steam 用户名: ", None)?,
    };

    println!("📤 上传 Steam Workshop...");
    let mut command = Command::new(&steamcmd_path);
    command
        .arg("+login")
        .arg(&steam_user)
        .arg("+workshop_build_item")
        .arg(&vdf)
        .arg("+quit");
    if non_interactive {
        command.stdin(Stdio::null());
    }
    let output = command
        .output()
        .with_context(|| format!("启动 SteamCMD 失败: {}", steamcmd_path.display()))?;

    let stdout = String::from_utf8_lossy(&output.stdout);
    let stderr = String::from_utf8_lossy(&output.stderr);
    let full_output = format!("{}\n{}", stdout, stderr);
    let steamcmd_reported_error = full_output
        .lines()
        .any(|line| line.to_ascii_lowercase().contains("error!"));
    if !output.status.success() || steamcmd_reported_error {
        let summary = steamcmd_error_summary(&full_output);
        if non_interactive {
            anyhow::bail!("SteamCMD 上传失败：{}", summary);
        }
        eprintln!("SteamCMD 上传失败：{}", summary);
        launch_uploader(&dist_mod, &preview);
        return Ok(());
    }

    let workshop_id = extract_workshop_id(&full_output)
        .or_else(|| read_publishedfileid_from_vdf(&vdf))
        .unwrap_or(publishedfileid);
    println!("✅ Steam Workshop 更新完成");
    println!(
        "   https://steamcommunity.com/sharedfiles/filedetails/?id={}",
        workshop_id
    );
    if workshop_id == "0" {
        println!("   首次发布后请把 SteamCMD 返回的 ID 写入 onim.toml");
    }
    Ok(())
}

fn extract_workshop_id(output: &str) -> Option<String> {
    // SteamCMD 输出中可能包含 "PublishedFileId" 或数字 ID
    // 常见格式："PublishedFileId" "123456789" 或 Success. ID: 123456789
    for line in output.lines() {
        // 尝试匹配 "PublishedFileId" "12345"
        if let Some(pos) = line.find("PublishedFileId") {
            let rest = &line[pos..];
            if let Some(start) = rest.find('"').and_then(|s| rest[s + 1..].find('"')) {
                let after_first = &rest[start + 2..];
                if let Some(end) = after_first.find('"') {
                    let id = after_first[..end].trim();
                    if !id.is_empty() && id.chars().all(|c| c.is_ascii_digit()) {
                        return Some(id.to_string());
                    }
                }
            }
        }
        // 尝试匹配简单的数字 ID（8-12 位数字）
        for word in line.split_whitespace() {
            let clean = word.trim_matches(|c: char| !c.is_ascii_digit());
            if clean.len() >= 8 && clean.len() <= 12 && clean.chars().all(|c| c.is_ascii_digit()) {
                return Some(clean.to_string());
            }
        }
    }
    None
}

fn read_publishedfileid_from_vdf(vdf: &Path) -> Option<String> {
    let content = fs::read_to_string(vdf).ok()?;
    for line in content.lines() {
        if line.contains("publishedfileid")
            && let Some(id) = line.split('"').nth(3).map(str::trim)
            && !id.is_empty()
            && id != "0"
            && id.chars().all(|c| c.is_ascii_digit())
        {
            return Some(id.to_string());
        }
    }
    None
}

fn launch_uploader(dist_mod: &Path, preview: &Path) {
    let uploader = match uploader_path() {
        Some(p) => p,
        None => {
            println!("\n❌ 找不到上传工具！");
            println!("方案一（推荐）：安装 steamcmd 实现全自动上传");
            println!("  Arch:    paru -S steamcmd");
            println!("  Ubuntu:  sudo apt install steamcmd");
            println!("  其他:    https://developer.valvesoftware.com/wiki/SteamCMD");
            println!();
            println!("方案二：从 Steam 库 → 工具 → 安装 'Oxygen Not Included Uploader'");
            return;
        }
    };

    println!("\n📤 启动 OniUploader...");
    println!("   {}", uploader.display());
    println!();
    println!("请按以下步骤操作：");
    println!("  1. 点击 'Add' 添加新 Mod");
    println!("  2. Mod 目录选择：{}", dist_mod.display());
    println!("  3. 预览图已就绪：{}", preview.display());
    println!("  4. 填写信息后点击 'Publish'");
    println!();

    #[cfg(target_os = "linux")]
    {
        let _ = Command::new("xdg-open").arg(dist_mod).spawn();
    }
    #[cfg(target_os = "macos")]
    {
        let _ = Command::new("open").arg(dist_mod).spawn();
    }
    #[cfg(target_os = "windows")]
    {
        let _ = Command::new("explorer").arg(dist_mod).spawn();
    }

    let _ = Command::new(&uploader).spawn();
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::error::Error;
    use std::io;
    use std::time::{SystemTime, UNIX_EPOCH};

    type TestResult = Result<(), Box<dyn Error>>;

    fn test_dir(name: &str) -> PathBuf {
        let nonce = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_nanos();
        env::temp_dir().join(format!("onim-{name}-{}-{nonce}", std::process::id()))
    }

    #[test]
    fn latest_changelog_accepts_plain_bullets() -> TestResult {
        let content = "# Changelog\n\n## 2026-08-23\n\n- 修复空闲差事\n- Fix idle chores\n\n## 2026-07-17\n\n- [abc] older\n";
        let summary = latest_changelog_summary(content, 6)
            .ok_or_else(|| io::Error::other("latest section missing"))?;

        assert!(summary.contains("- 修复空闲差事"));
        assert!(summary.contains("- Fix idle chores"));
        assert!(!summary.contains("older"));
        Ok(())
    }

    #[test]
    fn steam_description_reads_flat_package_assets() -> TestResult {
        let root = test_dir("description");
        fs::create_dir_all(&root)?;
        fs::write(root.join("steam-description-zh.md"), "中文说明")?;
        fs::write(root.join("steam-description-en.md"), "English description")?;

        let description = steam_markdown_description(&root)
            .ok_or_else(|| io::Error::other("combined description missing"))?;

        assert_eq!(description, "中文说明\n\nEnglish description");
        fs::remove_dir_all(root)?;
        Ok(())
    }

    #[test]
    fn generated_vdf_stays_outside_content_folder() -> TestResult {
        let root = test_dir("vdf");
        let content = root.join("CycleTrim");
        fs::create_dir_all(&content)?;
        let preview = content.join("preview.png");
        fs::write(&preview, b"preview")?;

        let vdf = generate_vdf(
            &content,
            &preview,
            "CycleTrim",
            "line one\nline two",
            "fixed idle chores",
            "3766318556",
        )?;
        let text = fs::read_to_string(&vdf)?;

        assert_eq!(vdf, root.join("CycleTrim.workshop.vdf"));
        assert!(text.contains("\"publishedfileid\"\t\"3766318556\""));
        assert!(text.contains("line one\\nline two"));
        assert!(!content.join("workshop.vdf").exists());

        fs::remove_dir_all(root)?;
        Ok(())
    }
}
