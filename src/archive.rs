use anyhow::{Context, Result};
use std::path::Path;
use std::process::Command;

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
