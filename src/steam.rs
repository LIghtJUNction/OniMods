use std::env;
use std::fs;
use std::path::PathBuf;
use std::process::Command;

use crate::config::Config;
use anyhow::{Context, Result};

pub fn workshop_command(cfg: &Config, operation: &str, argument: &str) -> Result<String> {
    let project = cfg
        .repo_root
        .join("tools/OniMods.SteamPublisher/OniMods.SteamPublisher.csproj");
    if !project.is_file() {
        anyhow::bail!("缺少 Steam Workshop helper：{}", project.display());
    }
    let output = Command::new("dotnet")
        .args(["run", "--project"])
        .arg(&project)
        .args(["-c", "Release", "--", "--workshop", operation, argument])
        .current_dir(&cfg.repo_root)
        .output()
        .context("启动 Steam Workshop helper 失败；请确认已安装 .NET SDK")?;
    if !output.status.success() {
        anyhow::bail!(
            "Steam Workshop {operation} 失败：{}",
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    Ok(String::from_utf8_lossy(&output.stdout).into_owned())
}

pub fn workshop_args(cfg: &Config, args: &[String]) -> Result<()> {
    let project = cfg
        .repo_root
        .join("tools/OniMods.SteamPublisher/OniMods.SteamPublisher.csproj");
    if !project.is_file() {
        anyhow::bail!("缺少 Steam Workshop helper：{}", project.display());
    }
    let status = Command::new("dotnet")
        .args(["run", "--project"])
        .arg(&project)
        .args(["-c", "Release", "--", "--workshop"])
        .args(args)
        .current_dir(&cfg.repo_root)
        .status()
        .context("启动 Steam Workshop helper 失败；请确认已安装 .NET SDK")?;
    if !status.success() {
        anyhow::bail!("Steam Workshop 页面操作失败");
    }
    Ok(())
}

/// Return the Steam library roots that may contain ONI or its uploader.
///
/// Steam's `libraryfolders.vdf` is the source of truth for custom libraries;
/// the platform-specific roots below only seed the search for that file.
pub(crate) fn library_roots() -> Vec<PathBuf> {
    let mut roots = Vec::new();

    if let Some(root) = env::var_os("ONIM_STEAM_ROOT") {
        push_unique(&mut roots, PathBuf::from(root));
    }

    #[cfg(target_os = "linux")]
    {
        if let Some(data_home) = env::var_os("XDG_DATA_HOME") {
            push_unique(&mut roots, PathBuf::from(data_home).join("Steam"));
        }
        if let Some(home) = env::var_os("HOME") {
            let home = PathBuf::from(home);
            for suffix in [
                ".local/share/Steam",
                ".steam/steam",
                ".steam/debian-installation",
                ".steam/root",
            ] {
                push_unique(&mut roots, home.join(suffix));
            }
        }
    }

    #[cfg(target_os = "macos")]
    if let Some(home) = env::var_os("HOME") {
        push_unique(
            &mut roots,
            PathBuf::from(home).join("Library/Application Support/Steam"),
        );
    }

    #[cfg(target_os = "windows")]
    for variable in ["ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA"] {
        if let Some(root) = env::var_os(variable) {
            push_unique(&mut roots, PathBuf::from(root).join("Steam"));
        }
    }

    let mut index = 0;
    while index < roots.len() {
        let root = roots[index].clone();
        index += 1;
        let library_file = root.join("steamapps/libraryfolders.vdf");
        let Ok(contents) = fs::read_to_string(library_file) else {
            continue;
        };
        for library in parse_library_paths(&contents) {
            push_unique(&mut roots, library);
        }
    }

    roots
}

fn push_unique(roots: &mut Vec<PathBuf>, candidate: PathBuf) {
    if candidate.as_os_str().is_empty() || roots.iter().any(|root| root == &candidate) {
        return;
    }
    roots.push(candidate);
}

fn parse_library_paths(contents: &str) -> Vec<PathBuf> {
    let fields = quoted_fields(contents);
    let mut paths = Vec::new();
    for pair in fields.windows(2) {
        if pair[0] != "path" {
            continue;
        }
        let path = PathBuf::from(&pair[1]);
        if !path.as_os_str().is_empty() && !paths.iter().any(|item| item == &path) {
            paths.push(path);
        }
    }
    paths
}

fn quoted_fields(contents: &str) -> Vec<String> {
    let mut fields = Vec::new();
    let mut current = String::new();
    let mut in_quote = false;
    let mut escaped = false;

    for character in contents.chars() {
        if !in_quote {
            if character == '"' {
                in_quote = true;
                current.clear();
            }
            continue;
        }

        if escaped {
            // KeyValues escapes quotes and backslashes. Preserve a backslash
            // before any other character because it may be a Windows path
            // separator rather than an escape sequence.
            if matches!(character, '"' | '\\') {
                current.push(character);
            } else {
                current.push('\\');
                current.push(character);
            }
            escaped = false;
        } else if character == '\\' {
            escaped = true;
        } else if character == '"' {
            fields.push(std::mem::take(&mut current));
            in_quote = false;
        } else {
            current.push(character);
        }
    }

    if escaped {
        current.push('\\');
    }
    fields
}

#[cfg(test)]
mod tests {
    use super::parse_library_paths;
    use std::path::Path;

    #[test]
    fn parses_custom_steam_library_paths() {
        let vdf = r#"
            "libraryfolders"
            {
                "0"
                {
                    "path" "/home/user/.local/share/Steam"
                }
                "1"
                {
                    "path" "/mnt/games/Steam Library"
                }
            }
        "#;

        assert_eq!(
            parse_library_paths(vdf),
            vec![
                Path::new("/home/user/.local/share/Steam"),
                Path::new("/mnt/games/Steam Library")
            ]
        );
    }

    #[test]
    fn unescapes_windows_library_paths() {
        let vdf = r#""path" "D:\\SteamLibrary""#;

        assert_eq!(
            parse_library_paths(vdf),
            vec![Path::new("D:\\SteamLibrary")]
        );
    }

    #[test]
    fn preserves_unescaped_windows_separators() {
        let vdf = r#""path" "C:\SteamLibrary\Games""#;

        assert_eq!(
            parse_library_paths(vdf),
            vec![Path::new("C:\\SteamLibrary\\Games")]
        );
    }

    #[test]
    fn accepts_key_and_value_on_separate_lines() {
        let vdf = r#"
            "path"
            "/mnt/games/Steam Library"
        "#;

        assert_eq!(
            parse_library_paths(vdf),
            vec![Path::new("/mnt/games/Steam Library")]
        );
    }
}
