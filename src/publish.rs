use anyhow::{Context, Result};
use std::env;
use std::fs;
use std::io::{self, Write};
use std::path::PathBuf;
use std::process::Command;

use crate::build;
use crate::config::{Config, SelectedMod};

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
            if pb.exists() { return Some(pb); }
        }
    }

    None
}

fn has_steamcmd() -> bool {
    Command::new("sh")
        .args(["-c", "command -v steamcmd > /dev/null 2>&1"])
        .status()
        .map(|s| s.success())
        .unwrap_or(false)
}

fn generate_vdf(
    dist_mod: &PathBuf,
    preview: &PathBuf,
    title: &str,
    description: &str,
    changenote: &str,
    publishedfileid: &str,
) -> Result<PathBuf> {
    let vdf_path = dist_mod.join("workshop.vdf");
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
        title,
        description,
        changenote,
    );
    fs::write(&vdf_path, content)
        .with_context(|| format!("写入 vdf 失败：{}", vdf_path.display()))?;
    Ok(vdf_path)
}

fn read_mod_info(dist_mod: &PathBuf) -> Option<(String, String, String)> {
    // 从 mod.yaml 读取标题和描述（游戏运行时使用）
    let mut title = None;
    let mut desc = None;
    let yaml = fs::read_to_string(dist_mod.join("mod.yaml")).ok()?;
    for line in yaml.lines() {
        let line = line.trim();
        if let Some((k, v)) = line.split_once(':') {
            let k = k.trim();
            let v = v.trim().trim_matches('"').trim_matches('\'');
            if k == "title" {
                title = Some(v.to_string());
            } else if k == "description" {
                desc = Some(v.to_string());
            }
        }
    }
    // 从 mod_info.yaml 读取版本号
    let mut version = None;
    if let Ok(yaml2) = fs::read_to_string(dist_mod.join("mod_info.yaml")) {
        for line in yaml2.lines() {
            let line = line.trim();
            if let Some((k, v)) = line.split_once(':') {
                let k = k.trim();
                let v = v.trim().trim_matches('"').trim_matches('\'');
                if k == "version" {
                    version = Some(v.to_string());
                }
            }
        }
    }
    Some((
        title.unwrap_or_else(|| "Untitled Mod".to_string()),
        desc.unwrap_or_default(),
        version.unwrap_or_else(|| "1.0.0".to_string()),
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

pub fn run(cfg: &Config, selected: &SelectedMod, use_gui: bool, desc_file: Option<&std::path::Path>) -> Result<()> {
    let repo_root = env::var_os("ONI_CLI_REPO_ROOT")
        .map(PathBuf::from)
        .unwrap_or_else(|| env::current_dir().unwrap());

    let assembly_name = selected.assembly_name(&repo_root);
    println!("🚀 准备发布：{}...", selected.name);
    build::run(cfg, selected, true)?;

    let dist_mod = cfg.dist_dir(&repo_root).join(&assembly_name);

    // 检查 mod_info.yaml
    let mod_info = dist_mod.join("mod_info.yaml");
    if !mod_info.exists() {
        println!("⚠️  警告：找不到 mod_info.yaml");
    }

    // 检查预览图
    let preview_png = dist_mod.join("preview.png");
    let preview_jpg = dist_mod.join("preview.jpg");
    let preview = if preview_png.exists() {
        preview_png
    } else if preview_jpg.exists() {
        preview_jpg
    } else {
        println!("\n⚠️  未找到预览图 (preview.png / preview.jpg)");
        println!("   OniUploader / SteamCMD 都需要预览图。");
        println!(
            "   建议：在 {} 下放一张 preview.png",
            selected.config.project_abs(&repo_root).display()
        );
        anyhow::bail!("缺少预览图");
    };

    // 强制使用 GUI
    if use_gui {
        println!("\n📌 已强制指定使用 OniUploader GUI");
        launch_uploader(&dist_mod, &preview);
        return Ok(());
    }

    // 尝试 SteamCMD 全自动上传
    if has_steamcmd() {
        println!("\n📡 检测到 steamcmd，支持全自动上传！");
        let (title, mut desc, version) = read_mod_info(&dist_mod)
            .unwrap_or_else(|| (selected.name.clone(), String::new(), "1.0.0".to_string()));

        // 如果指定了 desc-file，优先读取文件内容作为长描述
        if let Some(path) = desc_file {
            if path.exists() {
                desc = std::fs::read_to_string(path)
                    .with_context(|| format!("读取描述文件 {} 失败", path.display()))?;
                println!("   使用描述文件: {}", path.display());
            } else {
                println!("⚠️  描述文件 {} 不存在，回退到 mod.yaml 中的描述", path.display());
            }
        }

        let changenote = prompt(
            &format!("更新说明 [默认: 版本 {}]: ", version),
            Some(&format!("Release {}", version)),
        )?;

        let publishedfileid = if let Some(ref id) = selected.config.publishedfileid {
            println!("   使用配置中的 Workshop ID: {}", id);
            id.clone()
        } else {
            let id = prompt("已有 Workshop ID？（首次上传留空）: ", Some("0"))?;
            if id.trim().is_empty() {
                "0".to_string()
            } else {
                id
            }
        };

        let vdf = generate_vdf(
            &dist_mod,
            &preview,
            &title,
            &desc,
            &changenote,
            &publishedfileid,
        )?;
        println!("\n📤 开始上传...");
        println!("   vdf: {}", vdf.display());

        let steam_user = prompt("Steam 用户名: ", None)?;
        println!("\n📤 正在上传，请稍候...");
        let output = Command::new("steamcmd")
            .args([
                "+login",
                &steam_user,
                "+workshop_build_item",
                &vdf.to_string_lossy(),
                "+quit",
            ])
            .output()
            .context("启动 steamcmd 失败")?;

        let stdout = String::from_utf8_lossy(&output.stdout);
        let stderr = String::from_utf8_lossy(&output.stderr);
        let full_output = format!("{} {}", stdout, stderr);

        if output.status.success() {
            // 尝试从输出中提取 Workshop ID
            let workshop_id =
                extract_workshop_id(&full_output).or_else(|| read_publishedfileid_from_vdf(&vdf));

            println!("✅ 上传完成！");
            println!();

            if let Some(ref id) = workshop_id {
                println!("🔗 Steam 创意工坊链接：");
                println!(
                    "   https://steamcommunity.com/sharedfiles/filedetails/?id={}",
                    id
                );
                println!();
            } else if publishedfileid != "0" {
                println!("🔗 Steam 创意工坊链接：");
                println!(
                    "   https://steamcommunity.com/sharedfiles/filedetails/?id={}",
                    publishedfileid
                );
                println!();
            }

            println!("⚠️  重要提示：SteamCMD 上传无法设置 Tags（类别）！");
            println!("   请前往 Steam 创意工坊页面手动补充：");
            println!("   1. 登录 Steam → 创意工坊 → 你的物品");
            println!("   2. 编辑 Mod → 添加 Tags（如 Buildings、Quality of Life 等）");
            println!("   3. 保存更改");
            println!();

            if workshop_id.is_none() && publishedfileid == "0" {
                println!(
                    "   首次上传，Workshop ID 已写入 {}，下次可直接更新。",
                    vdf.display()
                );
            }
        } else {
            eprintln!("❌ steamcmd 错误输出：\n{}", stderr);
            println!("\n回退到 OniUploader GUI...");
            launch_uploader(&dist_mod, &preview);
        }

        return Ok(());
    }

    // 回退到 OniUploader GUI
    launch_uploader(&dist_mod, &preview);
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

fn read_publishedfileid_from_vdf(vdf: &PathBuf) -> Option<String> {
    let content = fs::read_to_string(vdf).ok()?;
    for line in content.lines() {
        if line.contains("publishedfileid") {
            if let Some((_, val)) = line.split_once('"') {
                if let Some((_, val2)) = val.split_once('"') {
                    if let Some((id, _)) = val2.split_once('"') {
                        let id = id.trim();
                        if !id.is_empty() && id != "0" && id.chars().all(|c| c.is_ascii_digit()) {
                            return Some(id.to_string());
                        }
                    }
                }
            }
        }
    }
    None
}

fn launch_uploader(dist_mod: &PathBuf, preview: &PathBuf) {
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
