use anyhow::{Result, bail};
use std::env;
use std::fs;
use std::path::{Path, PathBuf};

use crate::config;

pub fn run(explicit_config_path: Option<PathBuf>) -> Result<()> {
    let diagnostics = config::read_doctor_diagnostics(explicit_config_path)?;
    let mut issues = Vec::new();

    println!("🩺 onim 健康检查\n");
    check_config(
        "onim 配置文件",
        &diagnostics.config_path,
        diagnostics.config_error.as_deref(),
        &mut issues,
    );
    check_config(
        "Directory.Build.props",
        &diagnostics.props_path,
        diagnostics.game_path_error.as_deref(),
        &mut issues,
    );

    match (&diagnostics.game_path, &diagnostics.managed_path) {
        (Some(game_path), Some(managed_path)) => {
            check_directory("ONI 游戏路径", game_path, &mut issues);
            check_directory("托管程序集目录", managed_path, &mut issues);
            check_file(
                "Assembly-CSharp.dll",
                &managed_path.join("Assembly-CSharp.dll"),
                &mut issues,
            );
        }
        _ => println!("❌ ONI 游戏路径与托管程序集目录：无法从 Directory.Build.props 解析"),
    }

    match (&diagnostics.game_mods_dir, &diagnostics.game_mods_dir_error) {
        (Some(path), None) => check_directory("游戏 Mod 根目录", path, &mut issues),
        (_, Some(error)) => {
            println!("❌ 游戏 Mod 根目录：无法解析（{error}）");
            issues.push(format!("游戏 Mod 根目录无法解析：{error}"));
        }
        _ => unreachable!(),
    }

    println!("\n🔧 外部工具：");
    for tool in required_tools() {
        if tool_on_path(tool) {
            println!("   ✅ {tool}");
        } else {
            println!("   ❌ {tool}（PATH 中未找到）");
            issues.push(format!("缺少外部工具：{tool}"));
        }
    }

    println!("\n📦 已配置 Mod 源码：");
    match diagnostics.mods {
        Some(mods) if mods.is_empty() => {
            println!("   ❌ 未配置任何 Mod");
            issues.push("未配置任何 Mod".to_string());
        }
        Some(mods) => {
            let mut mods: Vec<_> = mods.iter().collect();
            mods.sort_unstable_by_key(|(key, _)| *key);
            for (key, mod_cfg) in mods {
                let source = mod_cfg.project_abs(&diagnostics.repo_root);
                check_directory(
                    &format!("Mod {} ({})", mod_cfg.mod_name(key), key),
                    &source,
                    &mut issues,
                );
            }
        }
        None => println!("   ❌ 配置文件无法解析，无法读取 Mod 源码路径"),
    }

    if issues.is_empty() {
        println!("\n✅ 环境检查通过");
        Ok(())
    } else {
        println!("\n❌ 环境检查失败（{} 项）：", issues.len());
        for issue in &issues {
            println!("   • {issue}");
        }
        bail!("onim doctor 发现 {} 项环境问题", issues.len());
    }
}

fn check_config(label: &str, path: &Path, error: Option<&str>, issues: &mut Vec<String>) {
    match error {
        Some(error) => {
            println!("❌ {label}：{}（{error}）", path.display());
            issues.push(error.to_string());
        }
        None => println!("✅ {label}：{}", path.display()),
    }
}

fn check_directory(label: &str, path: &Path, issues: &mut Vec<String>) {
    if path.is_dir() {
        println!("✅ {label}：{}", path.display());
    } else {
        println!("❌ {label}：{}", path.display());
        issues.push(format!("{label} 不存在或不是目录：{}", path.display()));
    }
}

fn check_file(label: &str, path: &Path, issues: &mut Vec<String>) {
    if path.is_file() {
        println!("✅ {label}：{}", path.display());
    } else {
        println!("❌ {label}：{}", path.display());
        issues.push(format!("{label} 不存在：{}", path.display()));
    }
}

fn required_tools() -> &'static [&'static str] {
    #[cfg(target_os = "windows")]
    {
        &["dotnet", "tar"]
    }

    #[cfg(not(target_os = "windows"))]
    {
        &["dotnet", "tar", "unzip"]
    }
}

fn tool_on_path(tool: &str) -> bool {
    let Some(paths) = env::var_os("PATH") else {
        return false;
    };
    let file_name = format!("{tool}{}", env::consts::EXE_SUFFIX);
    env::split_paths(&paths).any(|directory| is_executable(&directory.join(&file_name)))
}

fn is_executable(path: &Path) -> bool {
    if !path.is_file() {
        return false;
    }

    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;

        fs::metadata(path).is_ok_and(|metadata| metadata.permissions().mode() & 0o111 != 0)
    }

    #[cfg(not(unix))]
    {
        true
    }
}

#[cfg(test)]
mod tests {
    use super::required_tools;

    #[cfg(target_os = "windows")]
    #[test]
    fn required_tools_should_use_windows_archive_support() {
        assert_eq!(required_tools(), ["dotnet", "tar"]);
    }

    #[cfg(not(target_os = "windows"))]
    #[test]
    fn required_tools_should_include_unzip_off_windows() {
        assert_eq!(required_tools(), ["dotnet", "tar", "unzip"]);
    }
}
