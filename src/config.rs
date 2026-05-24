use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::env;
use std::path::{Path, PathBuf};

const DEFAULT_CONFIG_NAME: &str = "oni-mods.toml";
const BUILD_PROPS: &str = "Directory.Build.props";

/// 单个 Mod 配置
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ModConfig {
    pub path: PathBuf,
    pub name: Option<String>,
    /// Steam 创意工坊 ID（publishedfileid），配置后发布时自动使用
    pub publishedfileid: Option<String>,
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

        if let Some(csproj) = csproj {
            if let Ok(content) = std::fs::read_to_string(&csproj) {
                // 简单解析 <AssemblyName>值</AssemblyName>
                if let Some(start) = content.find("<AssemblyName>") {
                    if let Some(end) = content.find("</AssemblyName>") {
                        if end > start {
                            let val = &content[start + "<AssemblyName>".len()..end];
                            let trimmed = val.trim();
                            if !trimmed.is_empty() {
                                return trimmed.to_string();
                            }
                        }
                    }
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

impl Config {
    pub fn select_mod(&self, explicit: Option<String>) -> Result<SelectedMod> {
        if self.mods.is_empty() {
            anyhow::bail!(
                "没有配置任何 Mod，请在 {} 中添加 [mods.XXX]",
                DEFAULT_CONFIG_NAME
            );
        }
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
            None => {
                if let Some(ref default) = self.default_mod {
                    if self.mods.contains_key(default) {
                        default.clone()
                    } else {
                        self.mods.keys().next().unwrap().clone()
                    }
                } else {
                    self.mods.keys().next().unwrap().clone()
                }
            }
        };
        let cfg = self.mods.get(&key).unwrap().clone();
        Ok(SelectedMod {
            name: cfg.mod_name(&key),
            config: cfg,
        })
    }

    pub fn managed_path(&self) -> PathBuf {
        #[cfg(target_os = "windows")]
        {
            self.game_path
                .join("OxygenNotIncluded_Data")
                .join("Managed")
        }
        #[cfg(not(target_os = "windows"))]
        {
            let mac_managed = self
                .game_path
                .join("OxygenNotIncluded.app")
                .join("Contents")
                .join("OxygenNotIncluded_Data")
                .join("Managed");
            if mac_managed.exists() {
                mac_managed
            } else {
                self.game_path
                    .join("OxygenNotIncluded_Data")
                    .join("Managed")
            }
        }
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

    let repo_root = config_path
        .parent()
        .map(|p| p.to_path_buf())
        .unwrap_or_else(|| env::current_dir().unwrap());

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
        let local_low = env::var_os("USERPROFILE").context("无法获取 USERPROFILE")?;
        Ok(PathBuf::from(local_low).join("AppData/LocalLow/Klei/Oxygen Not Included"))
    }

    #[cfg(target_os = "macos")]
    {
        let home = env::var_os("HOME").context("无法获取 HOME 环境变量")?;
        Ok(PathBuf::from(home).join("Library/Application Support/unity.Klei.Oxygen Not Included"))
    }
}
