use anyhow::{Context, Result, bail};
use serde_json::Value;
use std::collections::BTreeSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use crate::config::Config;
use crate::mod_config_backup;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SourceKind {
    Local,
    Dev,
    Workshop,
}

#[derive(Clone, Debug)]
pub struct KeepSource {
    pub kind: SourceKind,
    pub id: String,
}

struct CleanupPlan {
    directories: Vec<PathBuf>,
    manifest: Option<(PathBuf, String, String)>,
    workshop_ids: Vec<u64>,
}

/// Retain at most the selected source for a staticID; verified local copies go
/// to a recovery directory and matching Workshop subscriptions are removed.
pub fn enforce_single_source(
    cfg: &Config,
    static_id: &str,
    keep: Option<&KeepSource>,
) -> Result<()> {
    let plan = CleanupPlan::inspect(cfg, static_id, keep)?;
    plan.apply(cfg, static_id)
}

pub fn preflight(cfg: &Config, static_id: &str, keep: Option<&KeepSource>) -> Result<()> {
    CleanupPlan::inspect(cfg, static_id, keep).map(|_| ())
}

pub fn best_non_workshop_source(cfg: &Config, static_id: &str) -> Result<Option<KeepSource>> {
    let mods_root = cfg.game_mods_dir()?;
    for (dirname, kind) in [
        ("Local", SourceKind::Local),
        ("Dev", SourceKind::Dev),
        ("dev", SourceKind::Dev),
    ] {
        let root = mods_root.join(dirname);
        if validate_scope_root(&mods_root, dirname)? {
            continue;
        }
        match fs::read_dir(&root) {
            Ok(entries) => {
                let mut matches = Vec::new();
                for entry in entries {
                    let entry = entry?;
                    if !entry.file_type()?.is_dir() || entry.file_type()?.is_symlink() {
                        continue;
                    }
                    let metadata = entry.path().join("mod.yaml");
                    if !metadata.is_file() {
                        continue;
                    }
                    if read_static_id(&metadata)? == static_id {
                        matches.push(entry.file_name().to_string_lossy().into_owned());
                    }
                }
                matches.sort();
                if let Some(id) = matches.into_iter().next() {
                    return Ok(Some(KeepSource { kind, id }));
                }
            }
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => {
                return Err(error).with_context(|| format!("检查安装来源失败：{}", root.display()));
            }
        }
    }
    Ok(None)
}

pub fn best_source_excluding(
    cfg: &Config,
    static_id: &str,
    excluded: SourceKind,
) -> Result<Option<KeepSource>> {
    if excluded != SourceKind::Dev
        && let Some(source) = find_directory_source(cfg, static_id, "Dev", SourceKind::Dev)?
    {
        return Ok(Some(source));
    }
    if excluded != SourceKind::Local
        && let Some(source) = find_directory_source(cfg, static_id, "Local", SourceKind::Local)?
    {
        return Ok(Some(source));
    }
    if excluded != SourceKind::Dev
        && let Some(source) = find_directory_source(cfg, static_id, "dev", SourceKind::Dev)?
    {
        return Ok(Some(source));
    }

    let path = cfg.game_mods_dir()?.join("mods.json");
    let content = match fs::read_to_string(&path) {
        Ok(content) => content,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => {
            return Err(error).with_context(|| format!("读取 Mod 清单失败：{}", path.display()));
        }
    };
    let data: Value = serde_json::from_str(&content).context("解析 mods.json 失败")?;
    Ok(data.get("mods").and_then(Value::as_array).and_then(|mods| {
        mods.iter().find_map(|entry| {
            if entry.get("staticID").and_then(Value::as_str) != Some(static_id)
                || entry
                    .pointer("/label/distribution_platform")
                    .and_then(Value::as_u64)
                    != Some(1)
            {
                return None;
            }
            let id = entry.pointer("/label/id").and_then(Value::as_str)?;
            Some(KeepSource {
                kind: SourceKind::Workshop,
                id: id.to_owned(),
            })
        })
    }))
}

fn find_directory_source(
    cfg: &Config,
    static_id: &str,
    dirname: &str,
    kind: SourceKind,
) -> Result<Option<KeepSource>> {
    let mods_root = cfg.game_mods_dir()?;
    let root = mods_root.join(dirname);
    if validate_scope_root(&mods_root, dirname)? {
        return Ok(None);
    }
    let entries = match fs::read_dir(&root) {
        Ok(entries) => entries,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => {
            return Err(error).with_context(|| format!("扫描安装目录失败：{}", root.display()));
        }
    };
    for entry in entries {
        let entry = entry?;
        let kind_info = entry.file_type()?;
        if kind_info.is_symlink() || !kind_info.is_dir() {
            continue;
        }
        let metadata = entry.path().join("mod.yaml");
        if metadata.is_file() && read_static_id(&metadata)? == static_id {
            return Ok(Some(KeepSource {
                kind,
                id: entry.file_name().to_string_lossy().into_owned(),
            }));
        }
    }
    Ok(None)
}

pub fn static_id_for_workshop_id(cfg: &Config, workshop_id: &str) -> Result<Option<String>> {
    let path = cfg.game_mods_dir()?.join("mods.json");
    let content = match fs::read_to_string(&path) {
        Ok(content) => content,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => {
            return Err(error).with_context(|| format!("读取 Mod 清单失败：{}", path.display()));
        }
    };
    let data: Value = serde_json::from_str(&content).context("解析 mods.json 失败")?;
    Ok(data.get("mods").and_then(Value::as_array).and_then(|mods| {
        mods.iter().find_map(|entry| {
            let platform = entry
                .pointer("/label/distribution_platform")
                .and_then(Value::as_u64);
            let id = entry.pointer("/label/id").and_then(Value::as_str);
            (platform == Some(1) && id == Some(workshop_id))
                .then(|| {
                    entry
                        .get("staticID")
                        .and_then(Value::as_str)
                        .map(str::to_owned)
                })
                .flatten()
        })
    }))
}

pub fn remove_workshop_version(cfg: &Config, static_id: &str, workshop_id: u64) -> Result<()> {
    let keep = best_non_workshop_source(cfg, static_id)?;
    let mut plan = CleanupPlan::inspect(cfg, static_id, keep.as_ref())?;
    plan.workshop_ids.push(workshop_id);
    plan.workshop_ids.sort_unstable();
    plan.workshop_ids.dedup();
    plan.apply(cfg, static_id)
}

impl CleanupPlan {
    fn inspect(cfg: &Config, static_id: &str, keep: Option<&KeepSource>) -> Result<Self> {
        let mods_root = cfg.game_mods_dir()?;
        let manifest_path = mods_root.join("mods.json");
        let mut directories = BTreeSet::new();
        let mut workshop_ids = BTreeSet::new();
        let mut manifest = None;

        if manifest_path.exists() {
            let previous = fs::read_to_string(&manifest_path)
                .with_context(|| format!("读取 Mod 清单失败：{}", manifest_path.display()))?;
            let mut data: Value = serde_json::from_str(&previous)
                .with_context(|| format!("解析 Mod 清单失败：{}", manifest_path.display()))?;
            if data.get("mod_load_in_progress").and_then(Value::as_bool) == Some(true) {
                bail!("ONI Mod loader 正在运行；未清理任何重复 Mod");
            }
            let mods = data
                .get_mut("mods")
                .and_then(Value::as_array_mut)
                .context("mods.json 缺少 mods 数组")?;
            let before = mods.len();
            for entry in mods.iter() {
                if entry.get("staticID").and_then(Value::as_str) != Some(static_id) {
                    continue;
                }
                let platform = entry
                    .pointer("/label/distribution_platform")
                    .and_then(Value::as_u64)
                    .context("重复 Mod 清单项缺少 distribution_platform")?;
                let id = entry
                    .pointer("/label/id")
                    .and_then(Value::as_str)
                    .context("重复 Mod 清单项缺少 label.id")?;
                if is_kept(keep, platform, id) {
                    continue;
                }
                match platform {
                    0 => add_verified_dir(
                        &mut directories,
                        &mods_root.join("Local").join(id),
                        static_id,
                    )?,
                    4 => add_verified_dir(
                        &mut directories,
                        &mods_root.join("Dev").join(id),
                        static_id,
                    )?,
                    1 => {
                        let id = id
                            .parse::<u64>()
                            .with_context(|| format!("Workshop 清单 ID 不是数字：{id}"))?;
                        if id > 0 {
                            workshop_ids.insert(id);
                        }
                    }
                    _ => bail!(
                        "发现相同 staticID 的未知安装来源 platform={platform}，为避免误删已停止清理"
                    ),
                }
            }
            let mut kept_row = false;
            mods.retain(|entry| {
                let same_id = entry.get("staticID").and_then(Value::as_str) == Some(static_id);
                if !same_id {
                    return true;
                }
                let platform = entry
                    .pointer("/label/distribution_platform")
                    .and_then(Value::as_u64);
                let id = entry.pointer("/label/id").and_then(Value::as_str);
                match (platform, id, keep) {
                    (Some(platform), Some(id), Some(keep))
                        if is_kept(Some(keep), platform, id) && !kept_row =>
                    {
                        kept_row = true;
                        true
                    }
                    _ => false,
                }
            });
            if mods.len() != before {
                manifest = Some((
                    manifest_path,
                    previous,
                    format!("{}\n", serde_json::to_string_pretty(&data)?),
                ));
            }
        }

        for dirname in ["Dev", "dev", "Local"] {
            let root = mods_root.join(dirname);
            if validate_scope_root(&mods_root, dirname)? {
                continue;
            }
            let entries = match fs::read_dir(&root) {
                Ok(entries) => entries,
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
                Err(error) => {
                    return Err(error)
                        .with_context(|| format!("扫描 Mod 目录失败：{}", root.display()));
                }
            };
            for entry in entries {
                let entry = entry?;
                let path = entry.path();
                let kind = entry.file_type()?;
                if kind.is_symlink() {
                    if keep.is_some_and(|target| {
                        target.id == entry.file_name().to_string_lossy()
                            && ((target.kind == SourceKind::Local && dirname == "Dev")
                                || (target.kind == SourceKind::Dev && dirname == "Local"))
                    }) {
                        bail!(
                            "发现无法验证的同名 Mod 符号链接，保留并停止：{}",
                            path.display()
                        );
                    }
                    continue;
                }
                if !kind.is_dir() {
                    continue;
                }
                let id = entry.file_name().to_string_lossy().into_owned();
                let keep_this_path = keep.is_some_and(|target| {
                    target.id == id
                        && match target.kind {
                            SourceKind::Local => dirname == "Local",
                            SourceKind::Dev => dirname == "Dev",
                            SourceKind::Workshop => false,
                        }
                });
                if keep_this_path {
                    continue;
                }
                let metadata = path.join("mod.yaml");
                if !metadata.is_file() {
                    continue;
                }
                let found = read_static_id(&metadata)
                    .with_context(|| format!("无法确认 Mod 身份，保留目录：{}", path.display()))?;
                if found == static_id {
                    directories.insert(canonical_dir(&path));
                }
            }
        }

        if let Some(target) = keep
            && target.kind != SourceKind::Workshop
        {
            let siblings: &[&str] = match target.kind {
                SourceKind::Local => &["Dev", "dev"],
                SourceKind::Dev => &["Local", "dev"],
                SourceKind::Workshop => &[],
            };
            let scope = match target.kind {
                SourceKind::Local => "Local",
                SourceKind::Dev => "Dev",
                SourceKind::Workshop => unreachable!(),
            };
            let canonical_target = canonical_dir(&mods_root.join(scope).join(&target.id));
            for sibling in siblings {
                let path = mods_root.join(sibling).join(&target.id);
                if fs::symlink_metadata(&path).is_err() || canonical_dir(&path) == canonical_target
                {
                    continue;
                }
                if path.is_dir() && !path.join("mod.yaml").is_file() {
                    if *sibling == "dev" {
                        continue;
                    }
                    bail!(
                        "无法验证同名旧 Mod 的 staticID，保留目录并停止安装：{}",
                        path.display()
                    );
                }
            }
        }

        Ok(Self {
            directories: directories.into_iter().collect(),
            manifest,
            workshop_ids: workshop_ids.into_iter().collect(),
        })
    }

    fn apply(self, cfg: &Config, static_id: &str) -> Result<()> {
        if self.directories.is_empty() && self.manifest.is_none() && self.workshop_ids.is_empty() {
            return Ok(());
        }
        let mods_root = cfg.game_mods_dir()?;
        let nonce = SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos();
        let recovery_root = mods_root.join(".onim-recovery");
        match fs::symlink_metadata(&recovery_root) {
            Ok(metadata) if metadata.file_type().is_symlink() || !metadata.is_dir() => bail!(
                "恢复路径不是普通目录，保留并停止：{}",
                recovery_root.display()
            ),
            Ok(_) => {}
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                fs::create_dir_all(&recovery_root)?
            }
            Err(error) => {
                return Err(error)
                    .with_context(|| format!("检查恢复目录失败：{}", recovery_root.display()));
            }
        }
        let recovery = if self.directories.is_empty() {
            None
        } else {
            let recovery = recovery_root.join(format!("duplicate-{nonce}"));
            fs::create_dir(&recovery).context("创建重复 Mod 恢复目录失败")?;
            Some(recovery)
        };
        let mut unsubscribed: Vec<u64> = Vec::new();
        for id in &self.workshop_ids {
            if let Err(error) = crate::steam::workshop_command(cfg, "uninstall", &id.to_string()) {
                resubscribe(cfg, &unsubscribed);
                if let Some(recovery) = &recovery {
                    let _ = fs::remove_dir(recovery);
                }
                return Err(error).context("自动取消重复 Workshop 订阅失败；保留本地副本");
            }
            unsubscribed.push(*id);
        }

        let mut moved = Vec::new();
        if let Some(recovery) = &recovery {
            for (index, source) in self.directories.iter().enumerate() {
                let name = source.file_name().context("重复 Mod 目录缺少名称")?;
                let destination = recovery.join(format!("{index}-{}", name.to_string_lossy()));
                if let Err(error) = fs::rename(source, &destination) {
                    let restore_result = restore(&moved);
                    resubscribe(cfg, &unsubscribed);
                    if restore_result.is_ok() {
                        let _ = fs::remove_dir(recovery);
                    }
                    return Err(error).with_context(|| {
                        format!(
                            "隔离重复 Mod 失败：{}{}",
                            source.display(),
                            restore_result
                                .err()
                                .map(|e| format!("；副本恢复失败：{e:#}"))
                                .unwrap_or_default()
                        )
                    });
                }
                moved.push((source.clone(), destination));
            }
        }

        let update = if let Some((path, previous, updated)) = self.manifest {
            match mod_config_backup::write_config(&path, &previous, &updated) {
                Ok(update) => update,
                Err(error) => {
                    let restore_result = restore(&moved);
                    resubscribe(cfg, &unsubscribed);
                    if let Some(recovery) = &recovery
                        && restore_result.is_ok()
                    {
                        let _ = fs::remove_dir(recovery);
                    }
                    return Err(error).context(format!(
                        "去重清单写入失败{}",
                        restore_result
                            .err()
                            .map(|e| format!("；副本恢复失败：{e:#}"))
                            .unwrap_or_else(|| "，已恢复本地副本".to_owned())
                    ));
                }
            }
        } else {
            mod_config_backup::Update {
                backup: None,
                pruned: None,
                cleanup_error: None,
            }
        };

        for (source, _) in &moved {
            println!("🧹 已移出重复版本：{}", source.display());
        }
        if let Some(recovery) = &recovery
            && !moved.is_empty()
        {
            println!("   恢复副本：{}", recovery.display());
        }
        for id in unsubscribed {
            println!("🧹 已取消重复 Workshop 订阅：{id}");
        }
        if let Some(path) = update.backup {
            println!("   mods.json 备份：{}", path.display());
        }
        if let Some(error) = update.cleanup_error {
            eprintln!("⚠️ 备份整理失败：{error}");
        }
        println!("✅ staticID {static_id} 只保留一个安装来源");
        Ok(())
    }
}

fn is_kept(keep: Option<&KeepSource>, platform: u64, id: &str) -> bool {
    keep.is_some_and(|keep| keep.kind == source_for(platform) && keep.id == id)
}

fn source_for(platform: u64) -> SourceKind {
    match platform {
        4 => SourceKind::Dev,
        1 => SourceKind::Workshop,
        _ => SourceKind::Local,
    }
}

fn add_verified_dir(paths: &mut BTreeSet<PathBuf>, path: &Path, static_id: &str) -> Result<()> {
    match fs::symlink_metadata(path) {
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(error) => {
            return Err(error).with_context(|| format!("检查 Mod 目录失败：{}", path.display()));
        }
        Ok(metadata) if metadata.file_type().is_symlink() || !metadata.is_dir() => {
            bail!("重复 Mod 路径不是普通目录，保留并停止：{}", path.display())
        }
        Ok(_) => {}
    }
    let found = read_static_id(&path.join("mod.yaml"))
        .with_context(|| format!("无法确认重复 Mod 身份，保留目录：{}", path.display()))?;
    if found != static_id {
        bail!(
            "清单与 Mod 目录身份不匹配，保留并停止：{} ({found})",
            path.display()
        );
    }
    paths.insert(canonical_dir(path));
    Ok(())
}

fn resubscribe(cfg: &Config, ids: &[u64]) {
    for id in ids.iter().rev() {
        let _ = crate::steam::workshop_command(cfg, "install", &id.to_string());
    }
}

fn canonical_dir(path: &Path) -> PathBuf {
    fs::canonicalize(path).unwrap_or_else(|_| path.to_path_buf())
}

/// Returns true only for the historical lowercase `dev` alias pointing at `Dev`.
fn validate_scope_root(mods_root: &Path, dirname: &str) -> Result<bool> {
    let root = mods_root.join(dirname);
    match fs::symlink_metadata(&root) {
        Ok(metadata) if metadata.file_type().is_symlink() => {
            let link = fs::read_link(&root)
                .with_context(|| format!("读取 Mod 目录符号链接失败：{}", root.display()))?;
            let linked = if link.is_absolute() {
                link
            } else {
                root.parent().context("Mod 根目录没有父目录")?.join(link)
            };
            let expected = mods_root.join("Dev");
            if dirname == "dev"
                && (linked == expected || canonical_dir(&linked) == canonical_dir(&expected))
            {
                return Ok(true);
            }
            bail!("Mod 来源目录不允许使用符号链接：{}", root.display());
        }
        Ok(metadata) if !metadata.is_dir() => {
            bail!("Mod 来源路径不是普通目录：{}", root.display())
        }
        Ok(_) => Ok(false),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(true),
        Err(error) => Err(error).with_context(|| format!("检查安装来源失败：{}", root.display())),
    }
}

pub fn read_static_id(path: &Path) -> Result<String> {
    let content = fs::read_to_string(path)?;
    for line in content.lines() {
        if let Some((key, value)) = line.split_once(':')
            && key.trim() == "staticID"
        {
            let id = value.trim().trim_matches('"').trim_matches('\'');
            if !id.is_empty() {
                return Ok(id.to_owned());
            }
        }
    }
    bail!("mod.yaml 缺少 staticID：{}", path.display())
}

pub fn static_id_from_project(project_dir: &Path) -> Result<String> {
    let manifest = project_dir.join("mod.yaml");
    if manifest.is_file() {
        return read_static_id(&manifest);
    }
    let mut found = BTreeSet::new();
    for entry in fs::read_dir(project_dir)
        .with_context(|| format!("读取 Mod 项目目录失败：{}", project_dir.display()))?
    {
        let entry = entry?;
        if entry.file_type()?.is_file()
            && entry.path().extension().and_then(|value| value.to_str()) == Some("csproj")
        {
            let source = fs::read_to_string(entry.path())?;
            if let Some(value) = xml_property(&source, "ModStaticID") {
                if value.contains("$(") {
                    bail!(
                        "项目 ModStaticID 依赖未解析的 MSBuild 属性：{}",
                        entry.path().display()
                    );
                }
                if !value.trim().is_empty() {
                    found.insert(value.trim().to_owned());
                }
            }
        }
    }
    match found.len() {
        1 => Ok(found.into_iter().next().expect("one staticID")),
        0 => bail!("项目中没有可确认的 staticID：{}", project_dir.display()),
        _ => bail!(
            "项目中的 .csproj 声明了多个不同的 ModStaticID：{}",
            project_dir.display()
        ),
    }
}

fn xml_property(source: &str, name: &str) -> Option<String> {
    let start_tag = format!("<{name}>");
    let end_tag = format!("</{name}>");
    let start = source.find(&start_tag)? + start_tag.len();
    let end = source[start..].find(&end_tag)? + start;
    Some(source[start..end].trim().to_owned())
}

fn restore(moved: &[(PathBuf, PathBuf)]) -> Result<()> {
    for (source, backup) in moved.iter().rev() {
        if backup.exists() {
            fs::rename(backup, source)
                .with_context(|| format!("恢复重复 Mod 失败，副本仍在：{}", backup.display()))?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::static_id_from_project;
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn reads_generated_manifest_identity_from_project_file() {
        let path = std::env::temp_dir().join(format!(
            "onim-static-id-{}-{}",
            std::process::id(),
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .expect("clock")
                .as_nanos()
        ));
        fs::create_dir_all(&path).expect("create temp project");
        fs::write(
            path.join("Example.csproj"),
            "<Project><PropertyGroup><ModStaticID>Example.Owner</ModStaticID></PropertyGroup></Project>",
        )
        .expect("write csproj");
        assert_eq!(
            static_id_from_project(&path).expect("project identity"),
            "Example.Owner"
        );
        fs::remove_dir_all(path).expect("remove temp project");
    }
}
