use anyhow::{Context, Result, ensure};
use std::collections::{BTreeMap, BTreeSet};
use std::fs;
use std::io::{self, ErrorKind, Read};
use std::path::{Path, PathBuf};

/// Replace an installed mod only after extraction and package validation succeed.
///
/// # Errors
/// Returns an error if staging, extraction, replacement or activation fails.
/// Failed activation restores the previous payload; failed rollback retains the
/// transaction directory and reports its path for recovery.
pub fn install(
    zip: &Path,
    dest: &Path,
    assembly_name: &str,
    activate: impl FnOnce() -> Result<()>,
) -> Result<()> {
    install_with(dest, assembly_name, |staged| unzip(zip, staged), activate)
}

/// Install an already built directory without compressing and reading it back.
///
/// # Errors
/// Rejects links, special files, ambiguous names and incomplete packages.
/// Staging and activation use the same rollback as ZIP installation.
pub fn install_directory(
    source: &Path,
    dest: &Path,
    assembly_name: &str,
    activate: impl FnOnce() -> Result<()>,
) -> Result<()> {
    install_with(
        dest,
        assembly_name,
        |staged| {
            ensure!(
                !staged.canonicalize()?.starts_with(source.canonicalize()?),
                "Mod 安装暂存目录不能位于来源目录内"
            );
            copy_directory(source, staged)
        },
        activate,
    )
}

fn install_with(
    dest: &Path,
    assembly_name: &str,
    populate: impl FnOnce(&Path) -> Result<()>,
    activate: impl FnOnce() -> Result<()>,
) -> Result<()> {
    let parent = dest.parent().context("安装目录缺少父目录")?;
    let name = dest.file_name().context("安装目录缺少名称")?;
    let existing = match fs::symlink_metadata(dest) {
        Ok(metadata) if metadata.is_dir() && !metadata.is_symlink() => true,
        Ok(_) => anyhow::bail!("安装目标不是普通目录，已保留：{}", dest.display()),
        Err(error) if error.kind() == ErrorKind::NotFound => false,
        Err(error) => return Err(error).context("读取旧安装目录失败"),
    };
    fs::create_dir_all(parent).context("创建模组目录失败")?;
    let transaction = parent.join(format!(".{}.onim-install", name.to_string_lossy()));
    fs::create_dir(&transaction).with_context(|| {
        format!(
            "无法独占安装事务目录；请检查进行中的安装或保留的恢复文件：{}",
            transaction.display()
        )
    })?;
    let package = transaction.join("package");
    let previous = transaction.join("previous");
    let rejected = transaction.join("rejected");
    let mut previous_moved = false;
    let mut installed = false;
    let result = (|| -> Result<()> {
        fs::create_dir(&package)?;
        populate(&package)?;
        for file in [format!("{assembly_name}.dll"), "mod_info.yaml".to_owned()] {
            let path = package.join(file);
            let metadata = fs::symlink_metadata(&path)
                .with_context(|| format!("模组包缺少文件：{}", path.display()))?;
            if !metadata.is_file() || metadata.is_symlink() {
                anyhow::bail!("模组包文件不是普通文件：{}", path.display());
            }
        }
        if existing {
            fs::rename(dest, &previous).context("暂存旧安装目录失败；原目录未删除")?;
            previous_moved = true;
        }
        fs::rename(&package, dest).context("应用新模组目录失败")?;
        installed = true;
        activate()
    })();
    if let Err(error) = result {
        let rollback = (|| -> Result<()> {
            if installed {
                fs::rename(dest, &rejected).context("隔离未完成的新安装失败")?;
            }
            if previous_moved {
                fs::rename(&previous, dest).context("恢复旧安装目录失败")?;
            }
            Ok(())
        })();
        if let Err(rollback_error) = rollback {
            return Err(error).context(format!(
                "安装回滚失败：{rollback_error:#}；保留恢复目录：{}；安装目标：{}",
                transaction.display(),
                dest.display()
            ));
        }
        cleanup_transaction(&transaction);
        return Err(error).context("安装未完成，已保留原安装状态");
    }
    cleanup_transaction(&transaction);
    Ok(())
}

fn cleanup_transaction(path: &Path) {
    if let Err(error) = fs::remove_dir_all(path) {
        eprintln!(
            "⚠️ 安装状态已确定，但临时目录清理失败：{}：{error}",
            path.display()
        );
    }
}

fn unzip(zip: &Path, dest: &Path) -> Result<()> {
    let mut archive = checked_archive(zip)?;
    let metadata = fs::symlink_metadata(dest)?;
    ensure!(
        metadata.is_dir()
            && !metadata.is_symlink()
            && fs::read_dir(dest)?.next().transpose()?.is_none(),
        "ZIP 解压目标必须是新建的空目录：{}",
        dest.display()
    );
    let mut created = BTreeSet::new();
    let mut directories = Vec::new();
    for index in 0..archive.len() {
        let mut file = archive.by_index(index)?;
        let relative = PathBuf::from(checked_entry_path(file.name()?.as_ref())?);
        let path = dest.join(&relative);
        if file.is_dir() {
            create_zip_directories(dest, &relative, &mut created)?;
            directories.push((path, file.unix_mode()));
        } else {
            create_zip_directories(
                dest,
                relative.parent().context("ZIP 文件缺少父目录")?,
                &mut created,
            )?;
            // Also reject aliases only the destination filesystem can detect.
            let mut output = fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(&path)
                .with_context(|| format!("ZIP 文件已存在或无法创建：{}", path.display()))?;
            io::copy(&mut file, &mut output).context("读取 ZIP 内容或校验 CRC 失败")?;
            apply_zip_permissions(&path, file.unix_mode())?;
        }
    }
    directories.sort_by_key(|(path, _)| std::cmp::Reverse(path.components().count()));
    for (path, mode) in directories {
        apply_zip_permissions(&path, mode)?;
    }
    Ok(())
}

fn create_zip_directories(
    root: &Path,
    relative: &Path,
    created: &mut BTreeSet<PathBuf>,
) -> Result<()> {
    let mut prefix = PathBuf::new();
    for component in relative.components() {
        prefix.push(component);
        if !created.contains(&prefix) {
            let target = root.join(&prefix);
            fs::create_dir(&target).with_context(|| {
                format!(
                    "ZIP 目录已存在、含文件系统别名或无法创建：{}",
                    target.display()
                )
            })?;
            created.insert(prefix.clone());
        }
    }
    Ok(())
}

fn apply_zip_permissions(path: &Path, mode: Option<u32>) -> Result<()> {
    if let Some(mode) = mode {
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            fs::set_permissions(path, fs::Permissions::from_mode(mode & 0o777))?;
        }
        #[cfg(not(unix))]
        {
            let mut permissions = fs::metadata(path)?.permissions();
            permissions.set_readonly(mode & 0o222 == 0);
            fs::set_permissions(path, permissions)?;
        }
    }
    Ok(())
}

/// List regular files after applying the same checks used by installation.
pub fn file_names(zip: &Path) -> Result<Vec<String>> {
    let mut archive = checked_archive(zip)?;
    let mut names = Vec::new();
    for index in 0..archive.len() {
        let file = archive.by_index(index)?;
        if file.is_file() {
            names.push(file.name()?.into_owned());
        }
    }
    Ok(names)
}

fn checked_archive(path: &Path) -> Result<zip::ZipArchive<fs::File>> {
    let file =
        fs::File::open(path).with_context(|| format!("读取 ZIP 失败：{}", path.display()))?;
    let mut archive = zip::ZipArchive::new(file).context("不是可读取的 ZIP 文件")?;
    archive
        .validate_header_consistency()
        .context("ZIP 文件头不一致或存在重复条目")?;
    let mut paths = BTreeMap::new();
    let mut spellings = BTreeMap::new();
    for index in 0..archive.len() {
        let file = archive.by_index(index)?;
        let name = file.name()?;
        let normalized = checked_entry_path(&name)?;
        // Register every component, including implicit parent directories.
        // Node IDs avoid storing a separate full string for every prefix.
        let mut parent = 0;
        for part in normalized.split('/').filter(|part| !part.is_empty()) {
            let id = spellings.len() + 1;
            let node = spellings
                .entry((parent, part.to_lowercase()))
                .or_insert_with(|| (id, part.to_owned()));
            ensure!(node.1 == part, "ZIP 目录或文件存在跨平台大小写别名：{name}");
            parent = node.0;
        }
        let kind = file.unix_mode().unwrap_or(0) & 0o170000;
        ensure!(
            !file.is_symlink() && matches!(kind, 0 | 0o100000 | 0o040000),
            "ZIP 不允许符号链接或特殊文件：{name}"
        );
        ensure!(
            !normalized.is_empty() || file.is_dir(),
            "ZIP 文件路径为空：{name}"
        );
        // A package must not depend on the destination filesystem's case rules.
        ensure!(
            paths
                .insert(normalized.to_lowercase(), file.is_dir())
                .is_none(),
            "ZIP 路径重复或存在跨平台别名：{name}"
        );
    }
    for path in paths.keys() {
        let mut parent = path.as_str();
        while let Some((prefix, _)) = parent.rsplit_once('/') {
            ensure!(
                paths.get(prefix) != Some(&false),
                "ZIP 文件被用作父目录：{prefix}"
            );
            parent = prefix;
        }
    }
    Ok(archive)
}

fn checked_entry_path(name: &str) -> Result<String> {
    ensure!(
        !name.is_empty() && !name.starts_with('/') && !name.contains('\\'),
        "包路径必须是使用 / 分隔的相对路径：{name}"
    );
    let mut parts = Vec::new();
    for part in name.split('/') {
        if part.is_empty() || part == "." {
            continue;
        }
        let stem = part
            .split('.')
            .next()
            .unwrap_or_default()
            .to_ascii_uppercase();
        let device = matches!(
            stem.as_str(),
            "CON" | "PRN" | "AUX" | "NUL" | "CONIN$" | "CONOUT$"
        ) || ["COM", "LPT"].iter().any(|prefix| {
            stem.strip_prefix(*prefix).is_some_and(|suffix| {
                matches!(
                    suffix,
                    "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" | "¹" | "²" | "³"
                )
            })
        });
        ensure!(
            part != ".."
                && !part.ends_with(['.', ' '])
                && !part
                    .chars()
                    .any(|c| c.is_control() || matches!(c, ':' | '<' | '>' | '"' | '|' | '?' | '*'))
                && !device,
            "包路径越界或含不支持的跨平台名称：{name}"
        );
        parts.push(part);
    }
    Ok(parts.join("/"))
}

fn copy_directory(source: &Path, dest: &Path) -> Result<()> {
    let mut directories = Vec::new();
    copy_directory_contents(source, dest, &mut directories)?;
    // Children precede parents. Keep all directories writable until the
    // complete copy succeeds, including validation of the last source entry.
    for (path, permissions) in directories {
        fs::set_permissions(path, permissions)?;
    }
    Ok(())
}

fn copy_directory_contents(
    source: &Path,
    dest: &Path,
    directories: &mut Vec<(PathBuf, fs::Permissions)>,
) -> Result<()> {
    let metadata = fs::symlink_metadata(source)?;
    ensure!(
        metadata.is_dir() && !metadata.is_symlink(),
        "Mod 来源不是普通目录：{}",
        source.display()
    );
    let mut names = BTreeSet::new();
    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let name = entry.file_name();
        let name_text = name.to_str().context("Mod 文件名不是 UTF-8")?;
        let normalized = checked_entry_path(name_text)?;
        ensure!(
            names.insert(normalized.to_lowercase()),
            "Mod 目录含跨平台重名文件：{name_text}"
        );
        let path = entry.path();
        let target = dest.join(&name);
        let info = fs::symlink_metadata(&path)?;
        if info.is_dir() && !info.is_symlink() {
            fs::create_dir(&target)?;
            copy_directory_contents(&path, &target, directories)?;
        } else if info.is_file() && !info.is_symlink() {
            let mut input = fs::File::open(&path)?;
            let mut output = fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(&target)?;
            io::copy(&mut input, &mut output)?;
            fs::set_permissions(&target, info.permissions())?;
        } else {
            anyhow::bail!("Mod 来源含符号链接或特殊文件：{}", path.display());
        }
    }
    directories.push((dest.to_owned(), metadata.permissions()));
    Ok(())
}

pub fn read_static_id_from_zip(zip: &Path) -> Result<String> {
    let text = read_mod_yaml_from_zip(zip)?;
    for line in text.lines() {
        if let Some((key, value)) = line.split_once(':')
            && key.trim() == "staticID"
        {
            let value = value.trim().trim_matches('"').trim_matches('\'');
            if !value.is_empty() {
                return Ok(value.to_owned());
            }
        }
    }
    anyhow::bail!("Workshop ZIP 的 mod.yaml 缺少 staticID")
}

pub fn read_mod_yaml_from_zip(zip: &Path) -> Result<String> {
    let mut archive = checked_archive(zip)?;
    let mut metadata_indices = Vec::new();
    for index in 0..archive.len() {
        let file = archive.by_index(index)?;
        if file.is_file()
            && Path::new(file.name()?.as_ref())
                .file_name()
                .and_then(|name| name.to_str())
                == Some("mod.yaml")
        {
            metadata_indices.push(index);
        }
    }
    ensure!(
        metadata_indices.len() == 1,
        "Workshop Legacy ZIP 必须恰好包含一个 mod.yaml；发现 {} 个",
        metadata_indices.len()
    );
    let mut text = String::new();
    archive
        .by_index(metadata_indices[0])?
        .read_to_string(&mut text)
        .context("读取 mod.yaml 失败，或内容不是 UTF-8")?;
    Ok(text)
}

#[cfg(test)]
#[path = "archive_tests.rs"]
mod tests;
