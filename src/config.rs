use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::env;
use std::path::{Path, PathBuf};
#[cfg(target_os = "windows")]
use std::process::Command;

const DEFAULT_CONFIG_NAME: &str = "onim.toml";
const BUILD_PROPS: &str = "Directory.Build.props";

/// 单个 Mod 配置
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ModConfig {
    pub path: PathBuf,
    pub name: Option<String>,
    /// Steam 创意工坊 ID（publishedfileid），配置后发布时自动使用
    pub publishedfileid: Option<String>,
    /// 可选的 Steam 创意工坊标题；未配置时使用 mod.yaml 的 title
    pub workshop_title: Option<String>,
}

impl ModConfig {
    pub fn project_abs(&self, repo_root: &Path) -> PathBuf {
        if self.path.is_absolute() {
            self.path.clone()
        } else {
            repo_root.join(&self.path)
        }
    }
    pub fn mod_name(&self, key: &str) -> String {
        self.name.clone().unwrap_or_else(|| key.to_string())
    }
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Config {
    #[serde(skip)]
    pub game_path: PathBuf,
    #[serde(default)]
    pub mods: HashMap<String, ModConfig>,
    pub default_mod: Option<String>,
}

/// `onim doctor` 所需的非阻断式配置诊断结果。
#[derive(Debug)]
pub struct DoctorConfigDiagnostics {
    pub repo_root: PathBuf,
    pub config_path: PathBuf,
    pub config_error: Option<String>,
    pub mods: Option<HashMap<String, ModConfig>>,
    pub props_path: PathBuf,
    pub game_path_error: Option<String>,
    pub game_path: Option<PathBuf>,
    pub managed_path: Option<PathBuf>,
    pub game_mods_dir_error: Option<String>,
    pub game_mods_dir: Option<PathBuf>,
}

#[derive(Debug)]
pub struct SelectedMod {
    pub name: String,
    pub config: ModConfig,
}

impl SelectedMod {
    /// 从 csproj 文件中读取 AssemblyName
    /// 如果找不到，返回 name 的帕斯卡命名版本
    pub fn assembly_name(&self, repo_root: &std::path::Path) -> String {
        let project_dir = self.config.project_abs(repo_root);
        let csproj = project_dir.read_dir().ok().and_then(|mut entries| {
            entries.find_map(|e| {
                let e = e.ok()?;
                let name = e.file_name().into_string().ok()?;
                if name.ends_with(".csproj") {
                    Some(e.path())
                } else {
                    None
                }
            })
        });

        if let Some(csproj) = csproj
            && let Ok(content) = std::fs::read_to_string(&csproj)
        {
            // 简单解析 <AssemblyName>值</AssemblyName>
            if let Some(start) = content.find("<AssemblyName>")
                && let Some(end) = content.find("</AssemblyName>")
                && end > start
            {
                let val = &content[start + "<AssemblyName>".len()..end];
                let trimmed = val.trim();
                if !trimmed.is_empty() {
                    return trimmed.to_string();
                }
            }
        }

        // 回退：将 snake_case 转为 PascalCase
        to_pascal_case(&self.name)
    }
}

fn to_pascal_case(s: &str) -> String {
    s.split('_')
        .map(|word| {
            let mut chars = word.chars();
            match chars.next() {
                None => String::new(),
                Some(first) => {
                    first.to_uppercase().collect::<String>() + &chars.as_str().to_lowercase()
                }
            }
        })
        .collect()
}

/// 从 Directory.Build.props 读取游戏路径
fn read_game_path_from_props(repo_root: &Path) -> Result<PathBuf> {
    let props = repo_root.join(BUILD_PROPS);
    if !props.exists() {
        anyhow::bail!("找不到 {}，请先运行 `onim setup` 初始化项目", BUILD_PROPS);
    }
    let content = std::fs::read_to_string(&props)
        .with_context(|| format!("读取 {} 失败", props.display()))?;

    parse_game_path_from_props(&content)
}

fn parse_game_path_from_props(content: &str) -> Result<PathBuf> {
    // 简单解析 <OniGamePath>值</OniGamePath>
    let start = content.find("<OniGamePath>");
    let end = content.find("</OniGamePath>");
    match (start, end) {
        (Some(s), Some(e)) if e > s => {
            let val = &content[s + "<OniGamePath>".len()..e];
            let trimmed = val.trim();
            if trimmed.is_empty() {
                anyhow::bail!("{} 中的 OniGamePath 为空", BUILD_PROPS);
            }
            Ok(PathBuf::from(trimmed))
        }
        _ => anyhow::bail!(
            "{} 中找不到 <OniGamePath> 标签，请先运行 `onim setup`",
            BUILD_PROPS
        ),
    }
}

/// 读取 `onim doctor` 的配置诊断信息，不执行常规命令的路径验证。
pub fn read_doctor_diagnostics(explicit_path: Option<PathBuf>) -> Result<DoctorConfigDiagnostics> {
    let (config_path, repo_root) = resolve_doctor_config_location(explicit_path)?;
    let (mods, config_error) = inspect_config_for_doctor(&config_path);
    let props_path = repo_root.join(BUILD_PROPS);
    let (game_path, game_path_error) = inspect_game_path_for_doctor(&props_path);
    let managed_path = game_path.as_ref().map(|path| managed_path_for_game(path));
    let (game_mods_dir, game_mods_dir_error) = match game_user_data_dir() {
        Ok(base) => (Some(base.join("mods")), None),
        Err(error) => (None, Some(error.to_string())),
    };

    Ok(DoctorConfigDiagnostics {
        repo_root,
        config_path,
        config_error,
        mods,
        props_path,
        game_path_error,
        game_path,
        managed_path,
        game_mods_dir_error,
        game_mods_dir,
    })
}

fn resolve_doctor_config_location(explicit_path: Option<PathBuf>) -> Result<(PathBuf, PathBuf)> {
    let current_dir = env::current_dir().context("无法获取当前工作目录")?;
    let config_path = match explicit_path {
        Some(path) if path.is_absolute() => path,
        Some(path) => current_dir.join(path),
        None => find_config_file()?.unwrap_or_else(|| current_dir.join(DEFAULT_CONFIG_NAME)),
    };
    let repo_root = config_path
        .parent()
        .map(Path::to_path_buf)
        .context("无法确定 onim 配置文件所在目录")?;
    Ok((config_path, repo_root))
}

fn inspect_config_for_doctor(
    config_path: &Path,
) -> (Option<HashMap<String, ModConfig>>, Option<String>) {
    let content = match std::fs::read_to_string(config_path) {
        Ok(content) => content,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return (
                None,
                Some(format!("配置文件不存在：{}", config_path.display())),
            );
        }
        Err(error) => {
            return (
                None,
                Some(format!(
                    "读取配置文件失败：{}（{error}）",
                    config_path.display()
                )),
            );
        }
    };

    match parse_config_for_doctor(&content) {
        Ok(mods) => (Some(mods), None),
        Err(error) => (
            None,
            Some(format!(
                "解析配置文件失败：{}（{error}）",
                config_path.display()
            )),
        ),
    }
}

fn parse_config_for_doctor(content: &str) -> Result<HashMap<String, ModConfig>> {
    Ok(toml::from_str::<Config>(content)?.mods)
}

fn inspect_game_path_for_doctor(props_path: &Path) -> (Option<PathBuf>, Option<String>) {
    let content = match std::fs::read_to_string(props_path) {
        Ok(content) => content,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return (None, Some(format!("找不到 {}", props_path.display())));
        }
        Err(error) => {
            return (
                None,
                Some(format!("读取 {} 失败：{error}", props_path.display())),
            );
        }
    };

    match parse_game_path_from_props(&content) {
        Ok(path) => (Some(path), None),
        Err(error) => (None, Some(error.to_string())),
    }
}

fn managed_path_for_game(game_path: &Path) -> PathBuf {
    #[cfg(target_os = "windows")]
    {
        game_path.join("OxygenNotIncluded_Data").join("Managed")
    }
    #[cfg(not(target_os = "windows"))]
    {
        let mac_managed = game_path
            .join("OxygenNotIncluded.app")
            .join("Contents")
            .join("OxygenNotIncluded_Data")
            .join("Managed");
        if mac_managed.exists() {
            mac_managed
        } else {
            game_path.join("OxygenNotIncluded_Data").join("Managed")
        }
    }
}

impl Config {
    pub fn select_all_mods(&self) -> Result<Vec<SelectedMod>> {
        if self.mods.is_empty() {
            anyhow::bail!(
                "没有配置任何 Mod，请在 {} 中添加 [mods.XXX]",
                DEFAULT_CONFIG_NAME
            );
        }

        let mut keys: Vec<_> = self.mods.keys().cloned().collect();
        keys.sort();

        let mut result = Vec::with_capacity(keys.len());
        for key in keys {
            let cfg = self
                .mods
                .get(&key)
                .with_context(|| format!("配置缺失：{}", key))?
                .clone();
            result.push(SelectedMod {
                name: cfg.mod_name(&key),
                config: cfg,
            });
        }
        Ok(result)
    }

    pub fn select_mod(&self, explicit: Option<String>) -> Result<SelectedMod> {
        if self.mods.is_empty() {
            anyhow::bail!(
                "没有配置任何 Mod，请在 {} 中添加 [mods.XXX]",
                DEFAULT_CONFIG_NAME
            );
        }
        let fallback = self
            .mods
            .keys()
            .next()
            .cloned()
            .ok_or_else(|| anyhow::anyhow!("没有可选择的 Mod"))?;
        let key = match explicit {
            Some(k) => {
                if !self.mods.contains_key(&k) {
                    anyhow::bail!(
                        "找不到 Mod '{}'，已配置的 Mod：{}\n请用 -m 指定正确的名称。",
                        k,
                        self.mods.keys().cloned().collect::<Vec<_>>().join(", ")
                    );
                }
                k
            }
            None => self
                .default_mod
                .as_ref()
                .filter(|default| self.mods.contains_key(*default))
                .cloned()
                .unwrap_or(fallback),
        };
        let cfg = self
            .mods
            .get(&key)
            .cloned()
            .ok_or_else(|| anyhow::anyhow!("找不到已选择的 Mod：{}", key))?;
        Ok(SelectedMod {
            name: cfg.mod_name(&key),
            config: cfg,
        })
    }

    pub fn managed_path(&self) -> PathBuf {
        managed_path_for_game(&self.game_path)
    }

    pub fn game_mods_dir(&self) -> Result<PathBuf> {
        let base = game_user_data_dir()?;
        Ok(base.join("mods"))
    }

    pub fn dev_mod_dir(&self, mod_name: &str) -> Result<PathBuf> {
        Ok(self.game_mods_dir()?.join("Dev").join(mod_name))
    }

    pub fn legacy_dev_mod_dir(&self, mod_name: &str) -> Result<PathBuf> {
        Ok(self.game_mods_dir()?.join("dev").join(mod_name))
    }

    pub fn local_mod_dir(&self, mod_name: &str) -> Result<PathBuf> {
        Ok(self.game_mods_dir()?.join("Local").join(mod_name))
    }

    pub fn dist_dir(&self, repo_root: &Path) -> PathBuf {
        repo_root.join("dist")
    }

    pub fn validate(&self) -> Result<()> {
        if !self.game_path.exists() {
            anyhow::bail!(
                "游戏路径不存在：{}\n请运行 `onim setup` 重新配置",
                self.game_path.display()
            );
        }
        let managed = self.managed_path();
        if !managed.join("Assembly-CSharp.dll").exists() {
            anyhow::bail!(
                "找不到 Assembly-CSharp.dll，游戏路径可能不正确：{}\n期望位置：{}\n请运行 `onim setup` 重新配置",
                self.game_path.display(),
                managed.display()
            );
        }
        Ok(())
    }
}

pub fn load(explicit_path: Option<PathBuf>) -> Result<Config> {
    let config_path = if let Some(p) = explicit_path {
        p
    } else {
        find_config_file()?.unwrap_or_else(|| PathBuf::from(DEFAULT_CONFIG_NAME))
    };

    let repo_root = match config_path.parent() {
        Some(parent) => parent.to_path_buf(),
        None => env::current_dir().context("读取当前目录失败")?,
    };

    // 从 Directory.Build.props 读取游戏路径
    let game_path = read_game_path_from_props(&repo_root)?;

    let cfg: Config = if config_path.exists() {
        let content = std::fs::read_to_string(&config_path)
            .with_context(|| format!("读取配置文件失败：{}", config_path.display()))?;
        let mut parsed: Config = toml::from_str(&content)
            .with_context(|| format!("解析配置文件失败：{}", config_path.display()))?;
        parsed.game_path = game_path;
        parsed
    } else {
        Config {
            game_path,
            mods: HashMap::new(),
            default_mod: None,
        }
    };

    for (key, m) in &cfg.mods {
        let abs = m.project_abs(&repo_root);
        if !abs.exists() {
            anyhow::bail!(
                "Mod '{}' 的路径不存在：{}\n请在 {} 中检查 [mods.{}] 的 path",
                key,
                abs.display(),
                config_path.display(),
                key
            );
        }
    }

    unsafe {
        env::set_var("ONI_CLI_REPO_ROOT", repo_root.as_os_str());
    }

    cfg.validate()?;
    Ok(cfg)
}

fn find_config_file() -> Result<Option<PathBuf>> {
    let mut dir = env::current_dir()?;
    loop {
        let candidate = dir.join(DEFAULT_CONFIG_NAME);
        if candidate.exists() {
            return Ok(Some(candidate));
        }
        if !dir.pop() {
            break;
        }
    }
    Ok(None)
}

fn game_user_data_dir() -> Result<PathBuf> {
    #[cfg(target_os = "linux")]
    {
        let home = env::var_os("HOME").context("无法获取 HOME 环境变量")?;
        Ok(PathBuf::from(home).join(".config/unity3d/Klei/Oxygen Not Included"))
    }

    #[cfg(target_os = "windows")]
    {
        // ONI loads Steam/Dev/Local mods from Documents\Klei\OxygenNotIncluded\mods.
        // LocalLow\...\Oxygen Not Included holds logs/config for some installs, not the
        // mod folders used by this Windows layout (see Player.log Loaded assembly path).
        let output = Command::new("powershell")
            .args([
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)",
            ])
            .output()
            .context("无法通过 Windows Known Folder API 获取 Documents 目录")?;
        if !output.status.success() {
            let stderr = String::from_utf8_lossy(&output.stderr);
            anyhow::bail!(
                "通过 Windows Known Folder API 获取 Documents 目录失败（{}）：{}",
                output.status,
                stderr.trim()
            );
        }

        let documents = String::from_utf8(output.stdout)
            .context("Windows Known Folder API 返回的 Documents 目录不是有效 UTF-8")?;
        let documents = documents.trim();
        if documents.is_empty() {
            anyhow::bail!("Windows Known Folder API 返回了空的 Documents 目录");
        }
        Ok(PathBuf::from(documents).join("Klei/OxygenNotIncluded"))
    }

    #[cfg(target_os = "macos")]
    {
        let home = env::var_os("HOME").context("无法获取 HOME 环境变量")?;
        Ok(PathBuf::from(home).join("Library/Application Support/unity.Klei.Oxygen Not Included"))
    }
}

#[cfg(test)]
mod tests {
    use super::parse_config_for_doctor;
    use std::path::Path;

    #[test]
    fn doctor_config_parser_should_preserve_resolvable_mods() {
        let mods = parse_config_for_doctor(
            r#"
default_mod = "Example"

[mods.Example]
path = "mods/Example"
"#,
        )
        .expect("valid doctor config should parse");

        assert_eq!(
            mods["Example"].project_abs(Path::new("/repo")),
            Path::new("/repo/mods/Example")
        );
    }
}
