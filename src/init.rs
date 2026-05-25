use anyhow::{Context, Result};
use std::env;
use std::fs;
use std::path::PathBuf;

const TEMPLATE_DIR: &str = "mods/OniModTemplate";
const CONFIG_FILE: &str = "oni-mods.toml";

fn pascal_case(input: &str) -> String {
    let mut result = String::new();
    let mut capitalize = true;
    for c in input.chars() {
        if c.is_alphanumeric() {
            if capitalize {
                result.push(c.to_ascii_uppercase());
                capitalize = false;
            } else {
                result.push(c);
            }
        } else {
            capitalize = true;
        }
    }
    result
}

fn replace_in_file(path: &PathBuf, replacements: &[(String, String)]) -> Result<()> {
    let content = fs::read_to_string(path)
        .with_context(|| format!("读取文件失败：{}", path.display()))?;
    let mut new_content = content.clone();
    for (old, new) in replacements {
        new_content = new_content.replace(old, new);
    }
    if new_content != content {
        fs::write(path, new_content)
            .with_context(|| format!("写入文件失败：{}", path.display()))?;
    }
    Ok(())
}

pub fn run(
    name: String,
    author: Option<String>,
    desc: Option<String>,
    mod_version: String,
    _config_path: Option<PathBuf>,
) -> Result<()> {
    let cwd = env::current_dir()?;
    let template = cwd.join(TEMPLATE_DIR);

    if !template.exists() {
        anyhow::bail!(
            "模板目录不存在：{}\n请确认 {} 目录存在",
            template.display(),
            TEMPLATE_DIR
        );
    }

    let mod_dir = cwd.join("mods").join(&name);
    if mod_dir.exists() {
        anyhow::bail!("Mod 目录已存在：{}", mod_dir.display());
    }

    let namespace = pascal_case(&name);
    let author = author.unwrap_or_else(|| "YourName".to_string());
    let desc = desc.unwrap_or_else(|| format!("{} Mod", name));
    let static_id = format!("{}.{}", author, namespace);

    println!("🆕 创建新 Mod: {}", name);
    println!("   命名空间: {}", namespace);
    println!("   staticID: {}", static_id);
    println!("   版本: {}", mod_version);

    // 1. 复制模板目录
    copy_dir_all(&template, &mod_dir)?;

    // 2. 重命名 .csproj 文件
    let old_csproj = mod_dir.join("OniModTemplate.csproj");
    let new_csproj = mod_dir.join(format!("{}.csproj", namespace));
    if old_csproj.exists() {
        fs::rename(&old_csproj, &new_csproj)
            .with_context(|| format!("重命名 .csproj 失败"))?;
    }

    // 3. 修改 .csproj（先替换完整特定字符串，再替换通用名称）
    let replacements_csproj = vec![
        (
            "<ModTitle>OniModTemplate</ModTitle>".to_string(),
            format!("<ModTitle>{}</ModTitle>", name),
        ),
        (
            "<ModDescription>这是一个示例 Mod</ModDescription>".to_string(),
            format!("<ModDescription>{}</ModDescription>", desc),
        ),
        (
            "<ModStaticID>YourName.OniModTemplate</ModStaticID>".to_string(),
            format!("<ModStaticID>{}</ModStaticID>", static_id),
        ),
        (
            "<ModVersion>0.1.4</ModVersion>".to_string(),
            format!("<ModVersion>{}</ModVersion>", mod_version),
        ),
        (
            "<!-- 注意：supportedContent 在 U55 (2025-03) 已弃用".to_string(),
            "<!-- 注意：supportedContent 在 U55 (2025-03) 已弃用".to_string(),
        ),
        ("OniModTemplate".to_string(), namespace.clone()),
    ];
    replace_in_file(&new_csproj, &replacements_csproj)?;

    // 4. 修改 ModInfo.cs
    let mod_info = mod_dir.join("ModInfo.cs");
    if mod_info.exists() {
        replace_in_file(
            &mod_info,
            &[("OniModTemplate".to_string(), namespace.clone())],
        )?;
    }

    // 5. 修改 Patches/ExamplePatch.cs
    let example_patch = mod_dir.join("Patches/ExamplePatch.cs");
    if example_patch.exists() {
        replace_in_file(
            &example_patch,
            &[("OniModTemplate".to_string(), namespace.clone())],
        )?;
    }

    // 6. 修改 README.md
    let readme = mod_dir.join("README.md");
    if readme.exists() {
        replace_in_file(
            &readme,
            &[
                ("# OniModTemplate".to_string(), format!("# {}", name)),
                ("OniModTemplate/".to_string(), format!("{}/", namespace)),
                ("OniModTemplate.csproj".to_string(), format!("{}.csproj", namespace)),
            ],
        )?;
    }

    // 6. 更新 oni-mods.toml
    let config_path = cwd.join(CONFIG_FILE);
    if config_path.exists() {
        let content = fs::read_to_string(&config_path)?;
        let entry = format!(
            "\n[mods.{}]\npath = \"mods/{}\"\n",
            name, name
        );
        let new_content = format!("{}{}", content.trim_end(), entry);
        fs::write(&config_path, new_content)
            .with_context(|| format!("更新 {} 失败", CONFIG_FILE))?;
        println!("   已追加到 {}", CONFIG_FILE);
    }

    println!("\n✅ Mod '{}' 创建成功！", name);
    println!("   目录：{}", mod_dir.display());
    println!();
    println!("下一步：");
    println!("  onim build -m {}      构建", name);
    println!("  onim dev -m {}        开发测试", name);

    Ok(())
}

fn copy_dir_all(src: &PathBuf, dst: &PathBuf) -> Result<()> {
    fs::create_dir_all(dst)?;
    for entry in fs::read_dir(src)? {
        let entry = entry?;
        let path = entry.path();
        let file_name = entry.file_name();
        let dest = dst.join(&file_name);

        if path.is_dir() {
            copy_dir_all(&path, &dest)?;
        } else {
            fs::copy(&path, &dest)
                .with_context(|| format!("复制文件失败：{} -> {}", path.display(), dest.display()))?;
        }
    }
    Ok(())
}
