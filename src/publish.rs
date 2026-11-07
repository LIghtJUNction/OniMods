use anyhow::{Context, Result};
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::process::Command;

use crate::build;
use crate::config::{Config, SelectedMod};

#[derive(Debug, Default)]
pub struct PublishOptions {
    pub use_gui: bool,
    pub auto_note: bool,
    pub non_interactive: bool,
    pub dry_run: bool,
    pub create: bool,
    pub visibility: Option<crate::workshop::Visibility>,
    pub expected_owner: Option<u64>,
    pub resume: Option<PathBuf>,
    pub preflight: bool,
}

fn uploader_path() -> Option<PathBuf> {
    #[cfg(target_os = "linux")]
    let relative = Path::new("steamapps/common/OxygenNotIncludedUploader/OniUploader64");
    #[cfg(target_os = "macos")]
    let relative = Path::new(
        "steamapps/common/OxygenNotIncludedUploader/OxygenNotIncludedUploader.app/Contents/MacOS/OxygenNotIncludedUploader",
    );
    #[cfg(target_os = "windows")]
    let relative =
        Path::new("steamapps/common/OxygenNotIncludedUploader/OxygenNotIncludedUploader.exe");

    #[cfg(any(target_os = "linux", target_os = "macos", target_os = "windows"))]
    for steam_root in crate::steam::library_roots() {
        let candidate = steam_root.join(relative);
        if candidate.is_file() {
            return Some(candidate);
        }
    }

    None
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
    let safe_publishedfileid = escape_vdf_value(publishedfileid);
    let safe_contentfolder = vdf_path_value(dist_mod, "contentfolder")?;
    let safe_previewfile = vdf_path_value(preview, "previewfile")?;
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
        safe_publishedfileid,
        safe_contentfolder,
        safe_previewfile,
        safe_title,
        safe_description,
        safe_changenote,
    );
    fs::write(&vdf_path, content)
        .with_context(|| format!("写入 vdf 失败：{}", vdf_path.display()))?;
    Ok(vdf_path)
}

fn reject_directory_upload_for_oni(vdf: &Path) -> Result<()> {
    let content = fs::read_to_string(vdf)
        .with_context(|| format!("读取 Workshop VDF 失败：{}", vdf.display()))?;
    let app_id = content
        .lines()
        .find_map(|line| {
            let mut fields = line.trim_start().split('"');
            (fields.next() == Some("") && fields.next() == Some("appid"))
                .then(|| fields.nth(1))
                .flatten()
        })
        .context("Workshop VDF 缺少 appid，拒绝目录上传")?
        .parse::<u32>()
        .context("Workshop VDF 的 appid 无效，拒绝目录上传")?;
    if app_id != 457140 {
        anyhow::bail!(
            "Workshop VDF 的 appid 是 {}，不是 ONI 的 457140；拒绝发布",
            app_id
        );
    }
    Ok(())
}

pub(crate) fn yaml_value(yaml: &str, key: &str) -> Option<String> {
    yaml.lines().find_map(|line| {
        let (candidate, value) = split_yaml_mapping(line)?;
        (candidate.trim() == key).then(|| parse_yaml_scalar(value))
    })
}

/// Split one simple YAML mapping without mistaking a colon inside a quoted key
/// for the key/value separator. The generated mod metadata is flat, so a full
/// YAML dependency would be unnecessary here.
fn split_yaml_mapping(line: &str) -> Option<(&str, &str)> {
    let line = line.trim();
    if line.is_empty() || line.starts_with('#') {
        return None;
    }

    let mut quote = None;
    let mut escaped = false;
    for (index, character) in line.char_indices() {
        match quote {
            Some('"') => {
                if escaped {
                    escaped = false;
                } else if character == '\\' {
                    escaped = true;
                } else if character == '"' {
                    quote = None;
                }
            }
            Some('\'') => {
                if character == '\'' {
                    quote = None;
                }
            }
            None => match character {
                '"' | '\'' => quote = Some(character),
                ':' => {
                    return Some((&line[..index], &line[index + character.len_utf8()..]));
                }
                _ => {}
            },
            _ => unreachable!("yaml quote state only contains YAML quote characters"),
        }
    }
    None
}

fn parse_yaml_scalar(value: &str) -> String {
    let value = strip_yaml_comment(value).trim();
    let bytes = value.as_bytes();
    if bytes.len() >= 2 && bytes.first() == Some(&b'"') && bytes.last() == Some(&b'"') {
        return serde_json::from_str(value)
            .unwrap_or_else(|_| value[1..value.len() - 1].to_string());
    }
    if bytes.len() >= 2 && bytes.first() == Some(&b'\'') && bytes.last() == Some(&b'\'') {
        return value[1..value.len() - 1].replace("''", "'");
    }
    value.to_string()
}

fn strip_yaml_comment(value: &str) -> &str {
    let first_non_whitespace = value
        .char_indices()
        .find_map(|(index, character)| (!character.is_whitespace()).then_some((index, character)));
    let quote = first_non_whitespace.and_then(|(index, character)| {
        matches!(character, '"' | '\'').then_some((index, character))
    });

    let mut chars = value.char_indices().peekable();
    let mut in_quote = false;
    let mut escaped = false;
    let mut preceded_by_whitespace = true;
    while let Some((index, character)) = chars.next() {
        if quote.is_some_and(|(quote_index, _)| quote_index == index) {
            in_quote = true;
            preceded_by_whitespace = false;
            continue;
        }

        if in_quote {
            match quote.map(|(_, character)| character) {
                Some('"') => {
                    if escaped {
                        escaped = false;
                    } else if character == '\\' {
                        escaped = true;
                    } else if character == '"' {
                        in_quote = false;
                    }
                }
                Some('\'') if character == '\'' => {
                    if chars.peek().is_some_and(|(_, next)| *next == '\'') {
                        chars.next();
                    } else {
                        in_quote = false;
                    }
                }
                _ => {}
            }
        } else if character == '#' && preceded_by_whitespace {
            return value[..index].trim_end();
        }
        preceded_by_whitespace = character.is_whitespace();
    }
    value
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

fn read_static_id(dist_mod: &Path) -> Option<String> {
    let yaml = fs::read_to_string(dist_mod.join("mod.yaml")).ok()?;
    yaml_value(&yaml, "staticID")
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

fn vdf_path_value(path: &Path, field: &str) -> Result<String> {
    let value = path
        .to_str()
        .with_context(|| format!("{field} 路径不是有效 UTF-8：{}", path.display()))?;
    Ok(escape_vdf_value(&value.replace('\\', "/")))
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

pub fn run(cfg: &Config, selected: &SelectedMod, options: PublishOptions) -> Result<()> {
    let PublishOptions {
        use_gui,
        auto_note,
        non_interactive,
        dry_run,
        create,
        visibility,
        expected_owner,
        resume,
        preflight,
    } = options;
    if let Some(path) = resume {
        return crate::workshop::resume(&path);
    }
    let owner = expected_owner.or(cfg.workshop_owner);
    if preflight {
        return crate::workshop::preflight(
            cfg,
            selected,
            owner.context("配置 workshop_owner 或使用 --expected-owner 固定发布账号")?,
        );
    }
    println!("🚀 构建发布包：{}", selected.name);
    let package = build::run(cfg, selected, true)?;
    let dist_mod = package.directory;
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

    let project_dir = selected.config.project_abs(&cfg.repo_root);
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
    } else if dry_run {
        // ID 0 only prepares metadata; the staged Legacy publisher owns creation.
        "0".to_string()
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

    reject_directory_upload_for_oni(&vdf)?;

    if create || dry_run {
        let target_id: u64 = publishedfileid
            .parse()
            .context("publishedfileid 必须是数值")?;
        if create && target_id != 0 {
            anyhow::bail!("已配置 publishedfileid，不能使用 --create；直接更新现有条目");
        }
        let owner = owner.context("配置 workshop_owner 或使用 --expected-owner 固定发布账号")?;
        let assembly_name = selected.assembly_name(&cfg.repo_root);
        return crate::workshop::upload(
            cfg,
            selected,
            crate::workshop::Publication {
                folder: &dist_mod,
                preview: &preview,
                assembly: &assembly_name,
                title,
                fallback: description,
                change_note: changenote,
                target_id,
                owner,
                visibility,
            },
            if dry_run {
                crate::workshop::Execution::DryRun
            } else {
                crate::workshop::Execution::Publish
            },
        );
    }

    run_headless_publisher(cfg, selected, &vdf, &title, &publishedfileid, &dist_mod)?;
    Ok(())
}

fn run_headless_publisher(
    cfg: &Config,
    selected: &SelectedMod,
    vdf: &Path,
    title: &str,
    publishedfileid: &str,
    dist_mod: &Path,
) -> Result<()> {
    if publishedfileid.is_empty() || publishedfileid == "0" {
        anyhow::bail!(
            "未在 onim.toml 中配置 publishedfileid。首次发布请参阅 docs/steam-publishing.md 使用受控的阶段性审查流程：\n  \
            scripts/publish_legacy_steam.sh {} --prepare-first-item --plan dist/{}.first-publication-plan.json",
            selected.name,
            selected.name
        );
    }

    let static_id = read_static_id(dist_mod)
        .ok_or_else(|| anyhow::anyhow!("发布包 mod.yaml 缺少 staticID；拒绝发布"))?;

    println!("\n📦 准备 Steam Legacy ZIP 发布器...");
    let publisher_dll = crate::embedded_publisher::ensure_publisher_ready(cfg)?;

    println!("📤 正在通过命令行发布到 Steam 创意工坊 (Legacy ZIP 模式)...");
    let mut cmd = Command::new("dotnet");
    cmd.arg(&publisher_dll)
        .arg("--vdf")
        .arg(vdf)
        .env("SteamAppId", "636750")
        .env("SteamGameId", "636750")
        .env("ONIM_PUBLISH_NAME", &selected.name)
        .env("ONIM_PUBLISH_TITLE_CONTAINS", title)
        .env("ONIM_PUBLISH_STATIC_ID", &static_id)
        .env("ONIM_PUBLISH_WORKSHOP_ID", publishedfileid);

    let status = cmd.status().context("执行 OniMods.SteamPublisher 失败")?;
    if !status.success() {
        anyhow::bail!("Steam 发布失败 (exit code: {:?})", status.code());
    }

    println!("\n🎉 成功发布到 Steam 创意工坊！");
    Ok(())
}

fn launch_uploader(dist_mod: &Path, preview: &Path) {
    let uploader = match uploader_path() {
        Some(p) => p,
        None => {
            println!("\n❌ 找不到 OniUploader！");
            println!("从 Steam 库 → 工具 → 安装 'Oxygen Not Included Uploader'");
            println!();
            println!("不要改用 SteamCMD：它的 contentfolder 目录上传会把条目");
            println!("单向转换为 UGC 目录模式，ONI 无法安装，且没有回退 API。");
            println!("无人值守发布请使用单文件 Legacy ZIP 发布流程；不要改用 SteamCMD 目录上传。");
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
    use std::env;
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
    fn yaml_value_preserves_colons_and_quoted_comments() {
        let yaml = r#"
            title: "CycleTrim: Experimental"
            description: 'Keep colon: and # hash'
            version: 1.2.3 # release version
        "#;

        assert_eq!(
            yaml_value(yaml, "title").as_deref(),
            Some("CycleTrim: Experimental")
        );
        assert_eq!(
            yaml_value(yaml, "description").as_deref(),
            Some("Keep colon: and # hash")
        );
        assert_eq!(yaml_value(yaml, "version").as_deref(), Some("1.2.3"));
        assert_eq!(
            yaml_value("title: Bob's Mod # release note", "title").as_deref(),
            Some("Bob's Mod")
        );
        assert_eq!(
            yaml_value("title: 'Bob''s # Mod' # release note", "title").as_deref(),
            Some("Bob's # Mod")
        );
    }

    #[test]
    fn vdf_paths_escape_keyvalues_characters() -> TestResult {
        let path = Path::new("mods/quoted\"name\npreview.png");

        assert_eq!(
            vdf_path_value(path, "previewfile")?,
            r#"mods/quoted\"name\npreview.png"#
        );
        Ok(())
    }

    #[cfg(unix)]
    #[test]
    fn vdf_paths_reject_non_utf8() {
        use std::ffi::OsString;
        use std::os::unix::ffi::OsStringExt;

        let path = PathBuf::from(OsString::from_vec(vec![b'/', 0xff]));

        assert!(vdf_path_value(&path, "contentfolder").is_err());
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

    #[test]
    fn publish_guard_accepts_oni_appid_for_any_workshop_id() -> TestResult {
        let root = test_dir("publish-guard");
        fs::create_dir_all(&root)?;
        let vdf = root.join("new-item.workshop.vdf");
        fs::write(
            &vdf,
            "\"workshopitem\"\n{\n\"appid\" \"457140\"\n\"publishedfileid\" \"9999999999\"\n}\n",
        )?;
        assert!(reject_directory_upload_for_oni(&vdf).is_ok());

        fs::write(&vdf, "\"workshopitem\"\n{\n\"appid\" \"12345\"\n}\n")?;
        assert!(reject_directory_upload_for_oni(&vdf).is_err());

        fs::write(
            &vdf,
            "\"workshopitem\"\n{\n\"publishedfileid\" \"457140\"\n}\n",
        )?;
        assert!(reject_directory_upload_for_oni(&vdf).is_err());

        fs::remove_dir_all(root)?;
        Ok(())
    }

    #[test]
    fn read_static_id_extracts_from_mod_yaml() -> TestResult {
        let root = test_dir("static-id");
        fs::create_dir_all(&root)?;
        fs::write(
            root.join("mod.yaml"),
            "title: \"MyMod\"\ndescription: \"desc\"\nstaticID: \"LIghtJUNction.MyMod\"\n",
        )?;
        assert_eq!(
            read_static_id(&root).as_deref(),
            Some("LIghtJUNction.MyMod")
        );
        fs::remove_dir_all(root)?;
        Ok(())
    }
}
