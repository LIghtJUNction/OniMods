use anyhow::{Context, Result};
use std::fs;
use std::io::ErrorKind;
use std::path::Path;
use std::process::Command;

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
        unzip(zip, &package)?;
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

pub fn unzip(zip: &Path, dest: &Path) -> Result<()> {
    #[cfg(target_os = "windows")]
    {
        let command = format!(
            "Expand-Archive -LiteralPath '{}' -DestinationPath '{}' -Force -ErrorAction Stop",
            escape_powershell_single_quoted(&zip.to_string_lossy()),
            escape_powershell_single_quoted(&dest.to_string_lossy())
        );
        let status = Command::new("powershell")
            .args([
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                command.as_str(),
            ])
            .status()
            .context("解压失败（PowerShell Expand-Archive）")?;
        if !status.success() {
            anyhow::bail!("解压失败（PowerShell Expand-Archive，退出状态：{status}）");
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        let status = Command::new("unzip")
            .arg("-o")
            .arg(zip)
            .arg("-d")
            .arg(dest)
            .status()
            .context("解压失败（unzip），请确认已安装 unzip")?;
        if !status.success() {
            anyhow::bail!("解压失败（unzip，退出状态：{status}）");
        }
    }

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
    let listing = Command::new("unzip")
        .args(["-Z1"])
        .arg(zip)
        .output()
        .context("读取 Workshop ZIP 内容失败；请确认已安装 unzip")?;
    if !listing.status.success() {
        anyhow::bail!("不是可读取的 ZIP 文件：{}", zip.display());
    }
    let entries = String::from_utf8(listing.stdout).context("ZIP 文件名不是 UTF-8")?;
    let mut metadata_paths = Vec::new();
    for entry in entries.lines() {
        let path = Path::new(entry);
        if path.components().any(|part| {
            matches!(
                part,
                std::path::Component::ParentDir
                    | std::path::Component::RootDir
                    | std::path::Component::Prefix(_)
            )
        }) {
            anyhow::bail!("Workshop ZIP 含越界路径：{entry}");
        }
        if path.file_name().and_then(|name| name.to_str()) == Some("mod.yaml") {
            metadata_paths.push(entry);
        }
    }
    if metadata_paths.len() != 1 {
        anyhow::bail!(
            "Workshop Legacy ZIP 必须恰好包含一个 mod.yaml；发现 {} 个",
            metadata_paths.len()
        );
    }
    let output = Command::new("unzip")
        .arg("-p")
        .arg(zip)
        .arg(metadata_paths[0])
        .output()
        .context("读取 Workshop ZIP 的 mod.yaml 失败")?;
    if !output.status.success() {
        anyhow::bail!("提取 Workshop ZIP 的 mod.yaml 失败");
    }
    let text = String::from_utf8(output.stdout).context("mod.yaml 不是 UTF-8")?;
    Ok(text)
}

#[cfg(any(target_os = "windows", test))]
fn escape_powershell_single_quoted(value: &str) -> String {
    value.replace('\'', "''")
}

#[cfg(test)]
mod tests {
    use super::escape_powershell_single_quoted;

    #[test]
    fn powershell_single_quote_escape_should_double_embedded_quotes() {
        assert_eq!(
            escape_powershell_single_quoted(r"C:\Users\O'Neil\mod.zip"),
            r"C:\Users\O''Neil\mod.zip"
        );
    }
}
