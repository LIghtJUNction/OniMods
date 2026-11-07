use anyhow::{Context, Result, bail};
use std::fs;
use std::path::Path;

use crate::archive;
use crate::build;
use crate::config::{Config, SelectedMod};
use crate::game_process;
use crate::identity::{self, KeepSource, SourceKind};

pub fn run(cfg: &Config, selected: &SelectedMod) -> Result<()> {
    game_process::ensure_stopped()?;

    let source_dir = selected.config.project_abs(&cfg.repo_root);
    let static_id = read_static_id(&source_dir.join("mod.yaml"))
        .with_context(|| format!("读取目标 Mod staticID 失败：{}", source_dir.display()))?;
    let keep = KeepSource {
        kind: SourceKind::Local,
        id: selected.name.clone(),
    };
    identity::preflight(cfg, &static_id, Some(&keep))?;
    let package = build::run(cfg, selected, true)?;

    let local_dir = cfg.local_mod_dir(&selected.name)?;
    println!("\n📥 安装到 Local 目录：{}", local_dir.display());
    archive::install(&package.archive, &local_dir, &package.assembly_name, || {
        if read_static_id(&local_dir.join("mod.yaml")).as_deref().ok() != Some(static_id.as_str()) {
            bail!(
                "新安装包 staticID 与源码不一致；没有清理旧版本：{}",
                local_dir.display()
            );
        }
        identity::enforce_single_source(cfg, &static_id, Some(&keep))
    })?;
    println!("✅ 已正式安装到游戏 Local 目录；同 staticID 的其他版本已清理");
    println!("   启动游戏 → Mod 列表 → 启用 '{}'", selected.name);
    Ok(())
}

pub struct WorkshopPageEdit {
    pub title: Option<String>,
    pub description_file: Option<std::path::PathBuf>,
    pub chinese_title: Option<String>,
    pub chinese_description_file: Option<std::path::PathBuf>,
    pub tags: Option<String>,
    pub visibility: Option<String>,
    pub preview: Option<std::path::PathBuf>,
    pub change_note: Option<String>,
}

pub fn workshop_page_show(cfg: &Config, id: &str) -> Result<()> {
    crate::steam::workshop_args(cfg, &["page".to_owned(), "show".to_owned(), id.to_owned()])
}

pub fn workshop_page_set(cfg: &Config, id: &str, edit: WorkshopPageEdit) -> Result<()> {
    if let Some(visibility) = &edit.visibility
        && !matches!(
            visibility.as_str(),
            "public" | "friends" | "private" | "unlisted"
        )
    {
        bail!("可见性只能是 public、friends、private 或 unlisted");
    }
    let mut args = vec!["page".to_owned(), "set".to_owned(), id.to_owned()];
    push_arg(&mut args, "--title", edit.title);
    push_file(&mut args, "--description-file", edit.description_file)?;
    push_arg(&mut args, "--chinese-title", edit.chinese_title);
    push_file(
        &mut args,
        "--chinese-description-file",
        edit.chinese_description_file,
    )?;
    push_arg(&mut args, "--tags", edit.tags);
    push_arg(&mut args, "--visibility", edit.visibility);
    push_file(&mut args, "--preview", edit.preview)?;
    push_arg(&mut args, "--change-note", edit.change_note);
    if args.len() == 3 {
        bail!("至少指定一个要修改的页面字段");
    }
    crate::steam::workshop_args(cfg, &args)
}

fn push_arg(args: &mut Vec<String>, name: &str, value: Option<String>) {
    if let Some(value) = value {
        args.push(name.to_owned());
        args.push(value);
    }
}

fn push_file(args: &mut Vec<String>, name: &str, path: Option<std::path::PathBuf>) -> Result<()> {
    let Some(path) = path else {
        return Ok(());
    };
    let absolute = if path.is_absolute() {
        path
    } else {
        std::env::current_dir()?.join(path)
    };
    if !absolute.is_file() {
        bail!("找不到文件：{}", absolute.display());
    }
    args.push(name.to_owned());
    args.push(absolute.to_string_lossy().into_owned());
    Ok(())
}

pub fn workshop(cfg: &Config, operation: &str, id: &str) -> Result<()> {
    if operation != "search" {
        game_process::ensure_stopped()?;
    }
    if operation == "uninstall" {
        let mut static_id = identity::static_id_for_workshop_id(cfg, id)?;
        if static_id.is_none()
            && let Ok(inspection) = crate::steam::workshop_command(cfg, "inspect", id)
            && let Some(zip) = inspection
                .lines()
                .find_map(|line| {
                    line.strip_prefix("installedLegacyZip=")
                        .filter(|value| !value.is_empty())
                        .map(std::path::PathBuf::from)
                })
                .filter(|path| path.is_file())
        {
            static_id = Some(archive::read_static_id_from_zip(&zip)?);
        }
        if let Some(static_id) = static_id {
            let id = id.parse::<u64>().context("Workshop ID 必须是正整数")?;
            identity::remove_workshop_version(cfg, &static_id, id)?;
        } else {
            let output = crate::steam::workshop_command(cfg, "uninstall", id)?;
            print!("{output}");
        }
        return Ok(());
    }
    let output = crate::steam::workshop_command(cfg, operation, id)?;
    if operation != "install" {
        print!("{output}");
        return Ok(());
    }

    let zip = output
        .lines()
        .find_map(|line| {
            line.strip_prefix("installedLegacyZip=")
                .map(std::path::PathBuf::from)
        })
        .context("Steam helper 未返回 Legacy ZIP 安装路径")?;
    if !zip.is_file() {
        bail!("Steam 返回的 Workshop ZIP 不存在：{}", zip.display());
    }
    let static_id = match archive::read_static_id_from_zip(&zip) {
        Ok(static_id) => static_id,
        Err(error) => {
            let _ = crate::steam::workshop_command(cfg, "uninstall", id);
            return Err(error).context("Workshop ZIP 缺少有效 Mod 身份；已尝试清除本次订阅");
        }
    };
    let keep = KeepSource {
        kind: SourceKind::Workshop,
        id: id.to_owned(),
    };
    if let Err(error) = identity::enforce_single_source(cfg, &static_id, Some(&keep)) {
        let _ = crate::steam::workshop_command(cfg, "uninstall", id);
        return Err(error).context("Workshop 已下载，但去重失败；已尝试取消本次订阅");
    }
    println!("✅ Workshop {id} 已安装；staticID {static_id} 只保留此版本");
    Ok(())
}

fn read_static_id(path: &Path) -> Result<String> {
    let content = fs::read_to_string(path)?;
    for line in content.lines() {
        if let Some((key, value)) = line.split_once(':')
            && key.trim() == "staticID"
        {
            let value = value.trim().trim_matches('"').trim_matches('\'');
            if !value.is_empty() {
                return Ok(value.to_owned());
            }
        }
    }
    bail!("mod.yaml 缺少 staticID：{}", path.display())
}
