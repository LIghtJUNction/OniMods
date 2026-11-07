use crate::UninstallScope;
use crate::config::{Config, SelectedMod};
use crate::game_process;
use crate::identity::{self, SourceKind};
use anyhow::{Context, Result, bail};

pub fn run(cfg: &Config, selected: &SelectedMod, scope: UninstallScope) -> Result<()> {
    game_process::ensure_stopped()?;
    let static_id = find_static_id(cfg, selected)?;
    let excluded = match scope {
        UninstallScope::Dev => Some(SourceKind::Dev),
        UninstallScope::Local => Some(SourceKind::Local),
        UninstallScope::All => None,
    };
    let keep = match excluded {
        Some(kind) => identity::best_source_excluding(cfg, &static_id, kind)?,
        None => None,
    };
    identity::enforce_single_source(cfg, &static_id, keep.as_ref())?;
    match excluded {
        Some(SourceKind::Dev) => println!(
            "✅ 已卸载 '{}' 的所有 Dev 版本；其他来源最多保留一个",
            selected.name
        ),
        Some(SourceKind::Local) => println!(
            "✅ 已卸载 '{}' 的所有 Local 版本；其他来源最多保留一个",
            selected.name
        ),
        _ => println!("✅ 已卸载 '{}' 的所有版本", selected.name),
    }
    Ok(())
}

fn find_static_id(cfg: &Config, selected: &SelectedMod) -> Result<String> {
    let manifest = selected.config.project_abs(&cfg.repo_root).join("mod.yaml");
    if manifest.is_file() {
        return identity::static_id_from_project(&selected.config.project_abs(&cfg.repo_root))
            .with_context(|| format!("读取 Mod 身份失败：{}", manifest.display()));
    }
    if let Ok(static_id) =
        identity::static_id_from_project(&selected.config.project_abs(&cfg.repo_root))
    {
        return Ok(static_id);
    }
    let roots = cfg.game_mods_dir()?;
    for scope in ["Local", "Dev", "dev"] {
        let path = roots.join(scope).join(&selected.name).join("mod.yaml");
        if path.is_file() {
            return identity::read_static_id(&path)
                .with_context(|| format!("读取已安装 Mod 身份失败：{}", path.display()));
        }
    }
    bail!(
        "无法确认 '{}' 的 staticID；未更改任何安装内容",
        selected.name
    )
}
