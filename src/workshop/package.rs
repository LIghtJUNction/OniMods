//! Immutable payloads and metadata, independent of the Steam client.

use anyhow::{Context, Result, bail, ensure};
use clap::ValueEnum;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::BTreeMap;
use std::fs::{self, OpenOptions};
use std::io::{Cursor, Write};
use std::path::{Path, PathBuf};
use zip::write::SimpleFileOptions;

use crate::config::{Config, SelectedMod};

/// Workshop visibility, separate from whether content has passed verification.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, ValueEnum)]
pub(crate) enum Visibility {
    Public,
    FriendsOnly,
    Private,
    Unlisted,
}

impl Visibility {
    pub(super) fn code(self) -> i32 {
        match self {
            Self::Public => 0,
            Self::FriendsOnly => 1,
            Self::Private => 2,
            Self::Unlisted => 3,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct Prerequisite {
    pub id: u64,
    pub title: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct Plan {
    pub schema: u32,
    pub sha256: String,
    pub name: String,
    pub static_id: String,
    pub target_id: u64,
    pub owner: u64,
    pub title: String,
    pub english: String,
    pub chinese: String,
    pub change_note: String,
    pub visibility: Option<Visibility>,
    pub dependencies: Vec<Prerequisite>,
    pub library: PathBuf,
    pub zip_sha256: String,
    pub zip_bytes: u64,
    pub preview_sha256: String,
    pub preview_extension: String,
    pub files: BTreeMap<String, String>,
}

/// Prepared Mod files and the selected publication target.
#[derive(Debug)]
pub(crate) struct Input<'a> {
    pub folder: &'a Path,
    pub preview: &'a Path,
    pub assembly: &'a str,
    pub title: String,
    pub fallback: String,
    pub change_note: String,
    pub target_id: u64,
    pub owner: u64,
    pub visibility: Option<Visibility>,
}

impl Plan {
    pub(super) fn prepare(
        cfg: &Config,
        selected: &SelectedMod,
        input: Input<'_>,
    ) -> Result<PathBuf> {
        let yaml = fs::read_to_string(input.folder.join("mod.yaml"))?;
        ensure!(
            !input.change_note.contains('\0') && input.change_note.len() <= 7999,
            "更新说明包含 NUL 或超过 7999 UTF-8 字节"
        );
        let static_id = crate::publish::yaml_value(&yaml, "staticID")
            .filter(|id| !id.is_empty())
            .context("mod.yaml 缺少 staticID")?;
        let english = localized(
            input.folder,
            "steam-description-en.md",
            "description.en.txt",
        )
        .unwrap_or_else(|| input.fallback.clone());
        let chinese = localized(input.folder, "steam-description-zh.md", "description.txt")
            .unwrap_or(input.fallback);
        for (label, text, max) in [
            ("标题", &input.title, 128),
            ("英文说明", &english, 7999),
            ("中文说明", &chinese, 7999),
        ] {
            ensure!(
                !text.trim().is_empty() && text.len() <= max && !text.contains('\0'),
                "{label} 为空、包含 NUL 或超过 {max} UTF-8 字节"
            );
        }
        let (zip, files) = build_zip(input.folder, input.assembly)?;
        let preview = fs::read(input.preview)?;
        ensure!(
            !preview.is_empty() && preview.len() <= 1024 * 1024,
            "预览图必须小于 1 MiB"
        );
        let extension = input
            .preview
            .extension()
            .and_then(|x| x.to_str())
            .context("预览图缺少扩展名")?
            .to_lowercase();
        ensure!(
            matches!(extension.as_str(), "png" | "jpg" | "jpeg"),
            "预览图必须为 PNG 或 JPEG"
        );
        let mut dependencies = Vec::new();
        for key in &selected.config.workshop_dependencies {
            ensure!(key != &selected.name, "不能依赖自身：{key}");
            let dependency = cfg
                .mods
                .get(key)
                .with_context(|| format!("前置 {key} 没有登记在 onim.toml"))?;
            let id = dependency
                .publishedfileid
                .as_deref()
                .context("前置尚未发布，请先发布前置并登记 publishedfileid")?
                .parse::<u64>()?;
            ensure!(
                id != 0 && id != u64::MAX && id != input.target_id,
                "前置 ID 无效或指向自身"
            );
            dependencies.push(Prerequisite {
                id,
                title: dependency
                    .workshop_title
                    .clone()
                    .unwrap_or_else(|| key.clone()),
            });
        }
        dependencies.sort_by_key(|d| d.id);
        dependencies.dedup_by_key(|d| d.id);
        let mut plan = Self {
            schema: 1,
            sha256: String::new(),
            name: selected.name.clone(),
            static_id,
            target_id: input.target_id,
            owner: input.owner,
            title: input.title,
            english,
            chinese,
            change_note: input.change_note,
            visibility: input.visibility,
            dependencies,
            library: steam_library(&cfg.game_path)?,
            zip_sha256: hash(&zip),
            zip_bytes: zip.len() as u64,
            preview_sha256: hash(&preview),
            preview_extension: extension,
            files,
        };
        plan.sha256 = plan.digest()?;
        let dir = state_root()?.join("plans").join(&plan.sha256);
        fs::create_dir_all(&dir)?;
        write_immutable(&dir.join("payload.zip"), &zip)?;
        write_immutable(
            &dir.join(format!("preview.{}", plan.preview_extension)),
            &preview,
        )?;
        let path = dir.join("plan.json");
        write_immutable(&path, &serde_json::to_vec_pretty(&plan)?)?;
        Ok(path)
    }

    fn digest(&self) -> Result<String> {
        let mut unsigned = self.clone();
        unsigned.sha256.clear();
        Ok(hash(&serde_json::to_vec(&unsigned)?))
    }

    pub(super) fn load(path: &Path) -> Result<Self> {
        let plan: Self = serde_json::from_slice(&read_regular(path)?)?;
        ensure!(
            plan.schema == 1 && plan.sha256 == plan.digest()?,
            "发布计划不完整或已被修改"
        );
        ensure!(plan.target_id != u64::MAX, "目标 ID 无效");
        let zip = plan.payload(path)?;
        ensure!(zip.len() as u64 == plan.zip_bytes, "ZIP 大小与发布计划不符");
        plan.preview(path)?;
        Ok(plan)
    }

    pub(super) fn payload(&self, path: &Path) -> Result<Vec<u8>> {
        checked_bytes(
            &path.parent().context("计划缺少父目录")?.join("payload.zip"),
            &self.zip_sha256,
        )
    }

    pub(super) fn preview(&self, path: &Path) -> Result<Vec<u8>> {
        ensure!(
            matches!(self.preview_extension.as_str(), "png" | "jpg" | "jpeg"),
            "预览图扩展名无效"
        );
        checked_bytes(
            &path
                .parent()
                .context("计划缺少父目录")?
                .join(format!("preview.{}", self.preview_extension)),
            &self.preview_sha256,
        )
    }
}

pub(super) fn hash(bytes: &[u8]) -> String {
    Sha256::digest(bytes)
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}

pub(super) fn state_root() -> Result<PathBuf> {
    let root = if cfg!(target_os = "windows") {
        std::env::var_os("LOCALAPPDATA")
            .map(PathBuf::from)
            .map(|p| p.join("OniMods"))
    } else {
        std::env::var_os("XDG_STATE_HOME")
            .map(PathBuf::from)
            .filter(|p| p.is_absolute())
            .or_else(|| std::env::var_os("HOME").map(|p| PathBuf::from(p).join(".local/state")))
            .map(|p| p.join("onim"))
    }
    .context("无法定位 onim 状态目录")?;
    Ok(root.join("native-workshop"))
}

pub(super) fn steam_library(game: &Path) -> Result<PathBuf> {
    let candidates = [
        game.join("OxygenNotIncluded_Data/Plugins/libsteam_api.so"),
        game.join("OxygenNotIncluded_Data/Plugins/x86_64/steam_api64.dll"),
        game.join("OxygenNotIncluded_Data/Plugins/steam_api64.dll"),
        game.join("Contents/Plugins/libsteam_api.dylib"),
    ];
    candidates
        .into_iter()
        .find(|p| p.is_file())
        .context("找不到游戏自带 Steam API；请安装完整的 ONI")
}

fn localized(folder: &Path, markdown: &str, text: &str) -> Option<String> {
    [
        folder.join(markdown),
        folder.join("docs").join(markdown),
        folder.join(text),
    ]
    .into_iter()
    .find_map(|p| {
        fs::read_to_string(p)
            .ok()
            .map(|s| s.trim().to_owned())
            .filter(|s| !s.is_empty())
    })
}

fn checked_bytes(path: &Path, expected: &str) -> Result<Vec<u8>> {
    let bytes = read_regular(path)?;
    ensure!(
        hash(&bytes) == expected,
        "文件已偏离发布计划：{}",
        path.display()
    );
    Ok(bytes)
}

fn read_regular(path: &Path) -> Result<Vec<u8>> {
    let metadata = fs::symlink_metadata(path)?;
    ensure!(
        metadata.is_file() && !metadata.is_symlink(),
        "必须是普通文件：{}",
        path.display()
    );
    ensure!(
        metadata.len() <= 100 * 1024 * 1024,
        "单个发布文件超过 100 MiB：{}",
        path.display()
    );
    Ok(fs::read(path)?)
}

fn write_immutable(path: &Path, bytes: &[u8]) -> Result<()> {
    match OpenOptions::new().write(true).create_new(true).open(path) {
        Ok(mut file) => {
            file.write_all(bytes)?;
            file.sync_all()?;
        }
        Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => ensure!(
            read_regular(path)? == bytes,
            "不可变发布文件已存在且内容不同：{}",
            path.display()
        ),
        Err(error) => return Err(error.into()),
    }
    Ok(())
}

fn build_zip(root: &Path, assembly: &str) -> Result<(Vec<u8>, BTreeMap<String, String>)> {
    let metadata = fs::symlink_metadata(root)?;
    ensure!(
        metadata.is_dir() && !metadata.is_symlink(),
        "Mod 发行目录不能是符号链接"
    );
    let mut entries = BTreeMap::new();
    collect(root, root, &mut entries, 0)?;
    for required in [
        format!("{assembly}.dll"),
        "mod.yaml".into(),
        "mod_info.yaml".into(),
    ] {
        ensure!(
            entries.contains_key(&required),
            "发行目录缺少根文件：{required}"
        );
    }
    let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
    let options = SimpleFileOptions::default()
        .compression_method(zip::CompressionMethod::Deflated)
        .compression_level(Some(6))
        .unix_permissions(0o644);
    let mut total = 0_u64;
    let mut hashes = BTreeMap::new();
    for (name, path) in entries {
        let basename = path
            .file_name()
            .and_then(|x| x.to_str())
            .context("发行文件名称不是 UTF-8")?
            .to_lowercase();
        if assembly != "Jlib" && basename == "jlib.dll" {
            bail!("消费者不能内嵌 Jlib.dll");
        }
        if basename.ends_with(".dll")
            && (basename.starts_with("unityengine")
                || basename.starts_with("assembly-csharp")
                || basename.starts_with("system.")
                || matches!(
                    basename.as_str(),
                    "mscorlib.dll" | "netstandard.dll" | "0harmony.dll"
                ))
        {
            bail!("发行目录包含游戏或框架 DLL：{name}");
        }
        let bytes = read_regular(&path)?;
        total += bytes.len() as u64;
        ensure!(total <= 100 * 1024 * 1024, "发行内容总大小超过 100 MiB");
        hashes.insert(name.clone(), hash(&bytes));
        zip.start_file(name, options)?;
        zip.write_all(&bytes)?;
    }
    Ok((zip.finish()?.into_inner(), hashes))
}

fn collect(
    root: &Path,
    dir: &Path,
    entries: &mut BTreeMap<String, PathBuf>,
    depth: usize,
) -> Result<()> {
    ensure!(depth <= 32 && entries.len() <= 4096, "发行目录超过遍历预算");
    for entry in fs::read_dir(dir)? {
        let path = entry?.path();
        let metadata = fs::symlink_metadata(&path)?;
        ensure!(
            !metadata.is_symlink(),
            "发行目录包含符号链接：{}",
            path.display()
        );
        let name = path
            .file_name()
            .and_then(|x| x.to_str())
            .context("发行文件名称不是 UTF-8")?
            .to_lowercase();
        ensure!(
            !name.starts_with('.')
                && !name.ends_with(".key")
                && !name.ends_with(".pem")
                && !matches!(name.as_str(), "onimcpconfig.json" | "steam_appid.txt"),
            "发行目录包含私人配置或密钥文件：{}",
            path.display()
        );
        if metadata.is_dir() {
            collect(root, &path, entries, depth + 1)?;
        } else {
            ensure!(
                metadata.is_file(),
                "发行目录包含特殊文件：{}",
                path.display()
            );
            let relative = path
                .strip_prefix(root)?
                .to_str()
                .context("发行路径不是 UTF-8")?
                .replace('\\', "/");
            ensure!(
                !relative
                    .split('/')
                    .any(|part| matches!(part, "." | "..") || part.contains(':')),
                "发行路径无效：{relative}"
            );
            ensure!(entries.insert(relative, path).is_none(), "发行路径冲突");
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sha256_uses_the_standard_lowercase_encoding() {
        assert_eq!(
            hash(b"abc"),
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        );
    }

    struct Fixture(PathBuf);
    impl Fixture {
        fn new() -> Self {
            let p = std::env::temp_dir().join(format!(
                "onim-native-package-{}-{}",
                std::process::id(),
                std::time::SystemTime::now()
                    .duration_since(std::time::UNIX_EPOCH)
                    .unwrap()
                    .as_nanos()
            ));
            fs::create_dir(&p).unwrap();
            for name in ["Consumer.dll", "mod.yaml", "mod_info.yaml"] {
                fs::write(p.join(name), name).unwrap();
            }
            Self(p)
        }
    }
    impl Drop for Fixture {
        fn drop(&mut self) {
            fs::remove_dir_all(&self.0).unwrap();
        }
    }

    #[test]
    fn zip_is_deterministic_and_keeps_implementation_paths() {
        let fixture = Fixture::new();
        fs::create_dir(fixture.0.join("implementation")).unwrap();
        fs::write(
            fixture.0.join("implementation/Consumer.dll"),
            b"implementation",
        )
        .unwrap();
        let (a, _) = build_zip(&fixture.0, "Consumer").unwrap();
        let (b, _) = build_zip(&fixture.0, "Consumer").unwrap();
        assert_eq!(a, b);
        let mut archive = zip::ZipArchive::new(Cursor::new(a)).unwrap();
        assert!(archive.by_name("implementation/Consumer.dll").is_ok());
    }

    #[test]
    fn unsafe_or_embedded_files_never_produce_an_upload_package() {
        for name in [
            ".env",
            ".env.local",
            "test.key",
            "test.pem",
            "OniMcpConfig.json",
            "Jlib.dll",
            "UnityEngine.CoreModule.dll",
            "Assembly-CSharp.dll",
        ] {
            let fixture = Fixture::new();
            fs::write(fixture.0.join(name), b"fixture").unwrap();
            assert!(
                build_zip(&fixture.0, "Consumer").is_err(),
                "accepted {name}"
            );
        }
    }

    #[test]
    #[cfg(unix)]
    fn symlinks_cannot_escape_the_package_root() {
        let fixture = Fixture::new();
        std::os::unix::fs::symlink("mod.yaml", fixture.0.join("linked")).unwrap();
        assert!(build_zip(&fixture.0, "Consumer").is_err());
    }

    #[test]
    fn localized_text_is_selected_and_markdown_keeps_precedence() {
        let fixture = Fixture::new();
        fs::write(fixture.0.join("description.en.txt"), "English\n").unwrap();
        assert_eq!(
            localized(&fixture.0, "steam-description-en.md", "description.en.txt").as_deref(),
            Some("English")
        );
        fs::create_dir(fixture.0.join("docs")).unwrap();
        fs::write(fixture.0.join("docs/steam-description-en.md"), "Preferred").unwrap();
        assert_eq!(
            localized(&fixture.0, "steam-description-en.md", "description.en.txt").as_deref(),
            Some("Preferred")
        );
    }
}
