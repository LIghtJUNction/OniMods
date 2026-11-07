use anyhow::{Context, Result, bail};
use serde_json::Value;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::{SystemTime, UNIX_EPOCH};

use crate::archive;
use crate::config::{Config, validate_mod_directory_name};
use crate::game_process;
use crate::identity::{self, KeepSource, SourceKind};

struct Repo {
    owner: String,
    name: String,
}

pub fn install_release(cfg: &Config, selected: Option<String>, source: &str) -> Result<()> {
    game_process::ensure_stopped()?;
    let repo = parse_repo(source)?;
    let release = request_json(&format!(
        "https://api.github.com/repos/{}/{}/releases/latest",
        repo.owner, repo.name
    ))?;
    let assets = release
        .get("assets")
        .and_then(Value::as_array)
        .context("GitHub 最新 Release 没有 assets 列表")?;
    let cache = cache_root()?
        .join("releases")
        .join(&repo.owner)
        .join(&repo.name);
    fs::create_dir_all(&cache)?;
    let mut candidates = Vec::new();
    for asset in assets {
        let name = asset
            .get("name")
            .and_then(Value::as_str)
            .unwrap_or_default();
        let url = asset
            .get("browser_download_url")
            .and_then(Value::as_str)
            .unwrap_or_default();
        if !name.to_ascii_lowercase().ends_with(".zip") || url.is_empty() {
            continue;
        }
        let file = cache.join(safe_filename(name)?);
        download(url, &file)?;
        let metadata = match archive::read_mod_yaml_from_zip(&file) {
            Ok(metadata) => metadata,
            Err(_) => continue,
        };
        let static_id =
            yaml_value(&metadata, "staticID").context("Release ZIP 的 mod.yaml 缺少 staticID")?;
        let title = yaml_value(&metadata, "title")
            .unwrap_or_else(|| name.trim_end_matches(".zip").to_owned());
        candidates.push(ReleaseAsset {
            asset_name: name.to_owned(),
            title,
            static_id,
            file,
        });
    }
    let chosen = choose_release(candidates, selected.as_deref())?;
    let install_name = install_directory_name(&chosen)?;
    install_zip(cfg, &chosen.file, &install_name, &chosen.static_id, None)
}

pub fn install_repository(
    cfg: &Config,
    selected: Option<String>,
    source: &str,
    reference: Option<&str>,
) -> Result<()> {
    game_process::ensure_stopped()?;
    let repo = parse_repo(source)?;
    let reference = reference.unwrap_or("HEAD");
    let root = cache_root()?
        .join("repositories")
        .join(&repo.owner)
        .join(&repo.name);
    fs::create_dir_all(&root)?;
    let checkout = root.join(format!("checkout-{}", short_hash(reference)));
    if checkout.exists() {
        let status = Command::new("git")
            .args(["-C"])
            .arg(&checkout)
            .args(["status", "--porcelain"])
            .output()
            .context("检查缓存 Git 仓库失败")?;
        if !status.status.success() || !status.stdout.is_empty() {
            bail!(
                "GitHub 缓存目录有本地修改，已保留并停止：{}",
                checkout.display()
            );
        }
        fetch_detach(&checkout, reference)?;
    } else {
        let url = format!("https://github.com/{}/{}.git", repo.owner, repo.name);
        fs::create_dir_all(&checkout)?;
        let prepared = (|| -> Result<()> {
            run_git(&checkout, &["init"])?;
            run_git(&checkout, &["remote", "add", "origin", &url])?;
            fetch_detach(&checkout, reference)
        })();
        if let Err(error) = prepared {
            let _ = fs::remove_dir_all(&checkout);
            return Err(error).context(
                "克隆 GitHub 仓库失败；--ref 可以是分支、标签或提交，省略时使用远端 HEAD",
            );
        }
    }

    let mut projects = Vec::new();
    scan_projects(&checkout, &checkout, &mut projects)?;
    projects.sort();
    if projects.is_empty() {
        bail!("仓库中没有找到 .csproj 项目：{}", source);
    }

    let mut packages = Vec::new();
    for project in projects {
        let assembly = assembly_name(&project)?;
        let output = Command::new("dotnet")
            .args(["build"])
            .arg(&project)
            .args(["--disable-build-servers", "-c", "Release"])
            .arg(format!("-p:OniGamePath={}", cfg.game_path.display()))
            .arg(format!(
                "-p:OniManagedPath={}",
                cfg.managed_path().display()
            ))
            .arg(format!("-p:DistPath={}", root.join("dist").display()))
            .env("DOTNET_CLI_USE_MSBUILD_SERVER", "false")
            .env("MSBUILDUSESERVER", "0")
            .current_dir(&checkout)
            .output()
            .context("启动 dotnet build 失败")?;
        if !output.status.success() {
            bail!(
                "构建 {} 失败：\n{}{}",
                project.display(),
                String::from_utf8_lossy(&output.stdout),
                String::from_utf8_lossy(&output.stderr)
            );
        }
        let package = build_package(&checkout, &project, &assembly, &root)?;
        let yaml = fs::read_to_string(package.join("mod.yaml"))
            .with_context(|| format!("构建产物缺少 mod.yaml：{}", package.display()))?;
        let static_id = yaml_value(&yaml, "staticID").context("构建产物 mod.yaml 缺少 staticID")?;
        let title = yaml_value(&yaml, "title").unwrap_or_else(|| assembly.clone());
        packages.push((project.clone(), assembly, title, static_id, package));
    }

    let chosen = if let Some(selection) = selected.as_deref() {
        let matches: Vec<_> = packages
            .iter()
            .filter(|item| {
                item.0.file_stem().and_then(|v| v.to_str()) == Some(selection)
                    || item.1 == selection
                    || item.2 == selection
            })
            .collect();
        match matches.as_slice() {
            [item] => vec![*item],
            [] => bail!(
                "仓库中找不到名为 {selection} 的 Mod；扫描到：{}",
                packages
                    .iter()
                    .map(|m| m.2.as_str())
                    .collect::<Vec<_>>()
                    .join(", ")
            ),
            _ => bail!("仓库中有多个 Mod 匹配 {selection}，请用 AssemblyName 或项目名指定"),
        }
    } else {
        packages.iter().collect()
    };
    let mut identities = std::collections::BTreeSet::new();
    for item in &chosen {
        if !identities.insert(item.3.as_str()) {
            bail!(
                "仓库中多个项目使用相同 staticID {}；停止安装以避免重复版本",
                item.3
            );
        }
    }
    for item in chosen {
        let name = selected.as_deref().unwrap_or(&item.2);
        install_directory(cfg, &item.4, name, &item.1, &item.3)?;
    }
    Ok(())
}

fn install_zip(
    cfg: &Config,
    zip: &Path,
    name: &str,
    static_id: &str,
    assembly_override: Option<&str>,
) -> Result<()> {
    let files = zip_files(zip)?;
    let assembly = files
        .iter()
        .filter_map(|file| zip_root_name(file))
        .filter_map(|file| {
            Path::new(file)
                .extension()
                .and_then(|e| e.to_str())
                .filter(|extension| *extension == "dll")?;
            Path::new(file).file_stem()?.to_str().map(str::to_owned)
        })
        .collect::<Vec<_>>();
    let assembly = if let Some(name) = assembly_override {
        if !files
            .iter()
            .filter_map(|entry| zip_root_name(entry))
            .any(|entry| entry == format!("{name}.dll"))
        {
            bail!("安装包根目录缺少 Mod DLL：{name}.dll");
        }
        name.to_owned()
    } else if assembly.len() == 1 {
        assembly[0].clone()
    } else {
        bail!(
            "Release ZIP 必须在根目录恰好包含一个 Mod DLL：{}",
            zip.display()
        )
    };
    validate_mod_directory_name(name)?;
    let target = cfg.local_mod_dir(name)?;
    archive::install(zip, &target, &assembly, || {
        let yaml = fs::read_to_string(target.join("mod.yaml"))?;
        if yaml_value(&yaml, "staticID").as_deref() != Some(static_id) {
            bail!("Release ZIP staticID 与预检值不一致");
        }
        identity::enforce_single_source(
            cfg,
            static_id,
            Some(&KeepSource {
                kind: SourceKind::Local,
                id: name.to_owned(),
            }),
        )
    })
}

fn install_directory(
    cfg: &Config,
    package: &Path,
    name: &str,
    assembly: &str,
    static_id: &str,
) -> Result<()> {
    validate_mod_directory_name(name)?;
    let mut staged = std::env::temp_dir();
    staged.push(format!(
        "onim-source-{}-{}",
        std::process::id(),
        unique_nonce()?
    ));
    fs::create_dir_all(staged.parent().context("temp directory has no parent")?)?;
    let zip = staged.with_extension("zip");
    let status = Command::new("zip")
        .args(["-q", "-r"])
        .arg(&zip)
        .arg(".")
        .current_dir(package)
        .status()
        .context("创建 Mod 安装包失败；请确认已安装 zip")?;
    if !status.success() {
        bail!("创建 Mod 安装包失败：{}", package.display());
    }
    let result = install_zip(cfg, &zip, name, static_id, Some(assembly));
    let _ = fs::remove_file(zip);
    result
}

fn build_package(root: &Path, project: &Path, assembly: &str, cache: &Path) -> Result<PathBuf> {
    let output_root = project
        .parent()
        .context("csproj has no parent")?
        .join("bin/Release");
    let dlls = find_files(&output_root, &format!("{assembly}.dll"))?;
    let dll = dlls
        .into_iter()
        .next()
        .context("构建成功但找不到 Mod DLL")?;
    let output = dll.parent().context("Mod DLL output has no parent")?;
    let mut metadata = output.join("mod.yaml");
    if !metadata.is_file() {
        metadata =
            find_named_upward(project, root, "mod.yaml")?.context("找不到项目的 mod.yaml")?;
    }
    let mut info = output.join("mod_info.yaml");
    if !info.is_file() {
        info = find_named_upward(project, root, "mod_info.yaml")?
            .context("构建输出与源码均缺少 mod_info.yaml")?;
    }
    let package = cache
        .join("packages")
        .join(format!("{}-{}", assembly, unique_nonce()?));
    fs::create_dir_all(&package)?;
    copy_tree(output, &package)?;
    fs::copy(metadata, package.join("mod.yaml"))?;
    fs::copy(info, package.join("mod_info.yaml"))?;
    if !package.join(format!("{assembly}.dll")).is_file() {
        bail!("构建目录 DLL 与 AssemblyName 不一致：{}", dll.display());
    }
    Ok(package)
}

fn scan_projects(root: &Path, directory: &Path, found: &mut Vec<PathBuf>) -> Result<()> {
    for entry in fs::read_dir(directory)? {
        let entry = entry?;
        let path = entry.path();
        let kind = entry.file_type()?;
        if kind.is_symlink() {
            continue;
        }
        if kind.is_dir() {
            if matches!(
                entry.file_name().to_str(),
                Some(".git" | "bin" | "obj" | "node_modules")
            ) {
                continue;
            }
            scan_projects(root, &path, found)?;
        } else if path.extension().and_then(|ext| ext.to_str()) == Some("csproj") {
            found.push(path);
        }
    }
    let _ = root;
    Ok(())
}

fn find_files(root: &Path, filename: &str) -> Result<Vec<PathBuf>> {
    let mut found = Vec::new();
    if !root.is_dir() {
        return Ok(found);
    }
    for entry in fs::read_dir(root)? {
        let entry = entry?;
        let path = entry.path();
        if entry.file_type()?.is_dir() {
            found.extend(find_files(&path, filename)?);
        } else if entry.file_name() == filename {
            found.push(path);
        }
    }
    Ok(found)
}

fn copy_tree(source: &Path, destination: &Path) -> Result<()> {
    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let path = entry.path();
        let target = destination.join(entry.file_name());
        let kind = entry.file_type()?;
        if kind.is_symlink() {
            continue;
        }
        if kind.is_dir() {
            fs::create_dir_all(&target)?;
            copy_tree(&path, &target)?;
        } else if kind.is_file() {
            fs::copy(path, target)?;
        }
    }
    Ok(())
}

fn find_named_upward(project: &Path, root: &Path, name: &str) -> Result<Option<PathBuf>> {
    let mut current = project.parent();
    while let Some(path) = current {
        let candidate = path.join(name);
        if candidate.is_file() {
            return Ok(Some(candidate));
        }
        if path == root {
            break;
        }
        current = path.parent();
    }
    Ok(None)
}

fn assembly_name(project: &Path) -> Result<String> {
    let text = fs::read_to_string(project)?;
    let start = text.find("<AssemblyName>");
    let end = text.find("</AssemblyName>");
    if let (Some(start), Some(end)) = (start, end) {
        let value = text[start + 14..end].trim();
        if !value.is_empty() {
            return Ok(value.to_owned());
        }
    }
    Ok(project
        .file_stem()
        .and_then(|s| s.to_str())
        .context("csproj name is not UTF-8")?
        .to_owned())
}

fn zip_files(zip: &Path) -> Result<Vec<String>> {
    let output = Command::new("unzip")
        .args(["-Z1"])
        .arg(zip)
        .output()
        .context("检查 Release ZIP 失败；请确认已安装 unzip")?;
    if !output.status.success() {
        bail!("无法读取 Release ZIP：{}", zip.display());
    }
    let text = String::from_utf8(output.stdout).context("ZIP entry names are not UTF-8")?;
    let files: Vec<_> = text.lines().map(str::to_owned).collect();
    if files.iter().any(|name| {
        Path::new(name).components().any(|part| {
            matches!(
                part,
                std::path::Component::ParentDir
                    | std::path::Component::RootDir
                    | std::path::Component::Prefix(_)
            )
        })
    }) {
        bail!("ZIP 包含越界路径");
    }
    Ok(files)
}

fn zip_root_name(entry: &str) -> Option<&str> {
    let mut name = None;
    for component in Path::new(entry).components() {
        match component {
            std::path::Component::CurDir => {}
            std::path::Component::Normal(value) if name.is_none() => {
                name = value.to_str();
            }
            _ => return None,
        }
    }
    name
}

#[derive(Debug)]
struct ReleaseAsset {
    asset_name: String,
    title: String,
    static_id: String,
    file: PathBuf,
}

fn choose_release(items: Vec<ReleaseAsset>, selected: Option<&str>) -> Result<ReleaseAsset> {
    if items.is_empty() {
        bail!("Release 中没有可安装的 Legacy ZIP");
    }
    let Some(selection) = selected else {
        return one_release(
            items,
            "Release 中有多个可安装的 Legacy ZIP；请用 --mod 指定标题、staticID 或资源名",
        );
    };
    let (matched, rest): (Vec<_>, Vec<_>) = items
        .into_iter()
        .partition(|item| release_matches(item, selection));
    match matched.len() {
        1 => one_release(matched, "匹配结果丢失"),
        count if count > 1 => bail!("Release 中有 {count} 个 ZIP 匹配 {selection}"),
        _ => one_release(
            rest,
            &format!("Release 中没有与 {selection} 匹配的 Legacy ZIP"),
        ),
    }
}

fn one_release(mut items: Vec<ReleaseAsset>, message: &str) -> Result<ReleaseAsset> {
    if items.len() == 1 {
        return Ok(items.remove(0));
    }
    bail!("{message}");
}

fn release_matches(asset: &ReleaseAsset, selection: &str) -> bool {
    let stem = Path::new(&asset.asset_name)
        .file_stem()
        .and_then(|name| name.to_str())
        .unwrap_or("");
    selection == asset.asset_name
        || selection == stem
        || selection == asset.title
        || selection == asset.static_id
}

fn install_directory_name(chosen: &ReleaseAsset) -> Result<String> {
    if validate_mod_directory_name(&chosen.title).is_ok() {
        return Ok(chosen.title.clone());
    }
    if validate_mod_directory_name(&chosen.static_id).is_ok() {
        return Ok(chosen.static_id.clone());
    }
    let stem = Path::new(&chosen.asset_name)
        .file_stem()
        .and_then(|name| name.to_str())
        .context("Release 资源名无效")?;
    validate_mod_directory_name(stem).with_context(|| {
        format!("无法从 Release 标题、staticID 或资源名确定安全安装目录：{stem}")
    })?;
    Ok(stem.to_owned())
}

fn fetch_detach(checkout: &Path, reference: &str) -> Result<()> {
    run_git(
        checkout,
        &["fetch", "--depth=1", "--no-tags", "origin", reference],
    )?;
    let status = Command::new("git")
        .arg("-C")
        .arg(checkout)
        .args(["checkout", "--detach", "FETCH_HEAD"])
        .status()
        .context("启动 git checkout 失败")?;
    if !status.success() {
        bail!("切换 GitHub 仓库版本失败");
    }
    Ok(())
}

fn parse_repo(input: &str) -> Result<Repo> {
    let value = input.trim().trim_end_matches('/').trim_end_matches(".git");
    let path = if let Some(rest) = value.strip_prefix("https://github.com/") {
        rest
    } else {
        value
    };
    let parts: Vec<_> = path.split('/').collect();
    if parts.len() != 2
        || !parts.iter().all(|part| {
            !part.is_empty()
                && part
                    .bytes()
                    .all(|byte| byte.is_ascii_alphanumeric() || b"-_.".contains(&byte))
        })
    {
        bail!("GitHub 仓库格式应为 owner/repo 或 https://github.com/owner/repo");
    }
    Ok(Repo {
        owner: parts[0].to_owned(),
        name: parts[1].to_owned(),
    })
}

fn request_json(url: &str) -> Result<Value> {
    let output = Command::new("curl")
        .args([
            "--fail",
            "--silent",
            "--show-error",
            "--location",
            "--retry",
            "2",
            "-H",
            "Accept: application/vnd.github+json",
            "-H",
            "User-Agent: onim",
        ])
        .arg(url)
        .output()
        .context("启动 curl 失败")?;
    if !output.status.success() {
        bail!(
            "GitHub API 请求失败：{}",
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    serde_json::from_slice(&output.stdout).context("解析 GitHub API 响应失败")
}

fn download(url: &str, path: &Path) -> Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let output = Command::new("curl")
        .args([
            "--fail",
            "--silent",
            "--show-error",
            "--location",
            "--retry",
            "2",
            "--output",
        ])
        .arg(path)
        .arg(url)
        .output()
        .context("启动 curl 下载失败")?;
    if !output.status.success() {
        let _ = fs::remove_file(path);
        bail!(
            "下载 GitHub Release 资源失败：{}",
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    Ok(())
}

fn run_git(directory: &Path, args: &[&str]) -> Result<()> {
    let output = Command::new("git")
        .arg("-C")
        .arg(directory)
        .args(args)
        .output()
        .context("启动 git 失败")?;
    if !output.status.success() {
        bail!(
            "git {} 失败：{}",
            args.join(" "),
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    Ok(())
}

fn cache_root() -> Result<PathBuf> {
    let root = std::env::var_os("XDG_CACHE_HOME")
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("HOME").map(|home| PathBuf::from(home).join(".cache")))
        .context("无法确定用户缓存目录")?;
    Ok(root.join("onim/github"))
}

fn safe_filename(name: &str) -> Result<&str> {
    if name.is_empty() || name == "." || name == ".." || name.contains(['/', '\\']) {
        bail!("GitHub Release 资源名不安全");
    }
    Ok(name)
}

fn yaml_value(text: &str, key: &str) -> Option<String> {
    text.lines().find_map(|line| {
        let (found, value) = line.split_once(':')?;
        if found.trim() != key {
            return None;
        }
        let value = value.trim().trim_matches('"').trim_matches('\'');
        (!value.is_empty()).then(|| value.to_owned())
    })
}

fn short_hash(text: &str) -> String {
    let hash = text.bytes().fold(0xcbf29ce484222325u64, |value, byte| {
        (value ^ u64::from(byte)).wrapping_mul(0x100000001b3)
    });
    format!("{hash:016x}")
}

fn unique_nonce() -> Result<u128> {
    Ok(SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos())
}

#[cfg(test)]
mod tests {
    use super::{ReleaseAsset, choose_release, install_directory_name};

    fn asset(name: &str, title: &str, static_id: &str) -> ReleaseAsset {
        ReleaseAsset {
            asset_name: name.to_owned(),
            title: title.to_owned(),
            static_id: static_id.to_owned(),
            file: std::path::PathBuf::from(name),
        }
    }

    #[test]
    fn release_selection_matches_static_id_among_several_archives() {
        let chosen = choose_release(
            vec![
                asset("A.zip", "Alpha", "id.A"),
                asset("B.zip", "Beta", "id.B"),
            ],
            Some("id.B"),
        )
        .expect("staticID should select one archive");
        assert_eq!(chosen.asset_name, "B.zip");
        assert_eq!(install_directory_name(&chosen).expect("directory"), "Beta");
    }

    #[test]
    fn single_release_accepts_an_explicit_directory_name() {
        let chosen = choose_release(vec![asset("A.zip", "Alpha", "id.A")], Some("InstalledName"))
            .expect("the only archive can be renamed");
        assert_eq!(chosen.static_id, "id.A");
        assert_eq!(install_directory_name(&chosen).expect("directory"), "Alpha");
    }

    #[test]
    fn ambiguous_release_requires_a_selector() {
        let error = choose_release(
            vec![
                asset("A.zip", "Alpha", "id.A"),
                asset("B.zip", "Beta", "id.B"),
            ],
            None,
        )
        .expect_err("two archives need a selector");
        assert!(error.to_string().contains("多个"));
    }

    #[test]
    fn unsafe_title_falls_back_to_static_id() {
        let chosen = asset("A.zip", "../escape", "id.A");
        assert_eq!(
            install_directory_name(&chosen).expect("safe fallback"),
            "id.A"
        );
    }
}
