use anyhow::{Context, Result, bail};
use std::env;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};

const CONFIG_FILE: &str = "onim.toml";
const DEFAULT_MOD_NAME: &str = "OniModTemplate";

struct TemplateFile {
    path: &'static str,
    contents: &'static str,
}

// These files are compiled into the binary so `cargo install onim` works from
// directories that do not contain a checkout of OniMods.
const MOD_TEMPLATE: &[TemplateFile] = &[
    TemplateFile {
        path: "OniModTemplate.csproj",
        contents: include_str!("../mods/OniModTemplate/OniModTemplate.csproj"),
    },
    TemplateFile {
        path: "ModInfo.cs",
        contents: include_str!("../mods/OniModTemplate/ModInfo.cs"),
    },
    TemplateFile {
        path: "Patches/ExamplePatch.cs",
        contents: include_str!("../mods/OniModTemplate/Patches/ExamplePatch.cs"),
    },
    TemplateFile {
        path: "README.md",
        contents: include_str!("../mods/OniModTemplate/README.md"),
    },
    TemplateFile {
        path: "CHANGELOG.md",
        contents: include_str!("../mods/OniModTemplate/CHANGELOG.md"),
    },
    TemplateFile {
        path: "README_EN.md",
        contents: include_str!("../mods/OniModTemplate/README_EN.md"),
    },
    TemplateFile {
        path: "docs/steam-description-zh.md",
        contents: include_str!("../mods/OniModTemplate/docs/steam-description-zh.md"),
    },
    TemplateFile {
        path: "docs/steam-description-en.md",
        contents: include_str!("../mods/OniModTemplate/docs/steam-description-en.md"),
    },
];

const BUILD_PROPS_EXAMPLE: &str = include_str!("../Directory.Build.props.example");

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

fn validate_mod_name(name: &str) -> Result<String> {
    if name.is_empty()
        || !name
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || matches!(byte, b'_' | b'-'))
    {
        bail!("Mod 名称只能包含英文字母、数字、下划线和连字符");
    }

    let namespace = pascal_case(name);
    let first = namespace
        .chars()
        .next()
        .context("Mod 名称必须至少包含一个字母或数字")?;
    if !(first.is_ascii_alphabetic() || first == '_') {
        bail!("Mod 名称转换后的 C# 命名空间必须以字母或下划线开头");
    }

    Ok(namespace)
}

fn xml_escape(value: &str) -> String {
    value
        .replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

fn rendered_template_file(
    template: &TemplateFile,
    name: &str,
    namespace: &str,
    author: &str,
    description: &str,
    mod_version: &str,
) -> String {
    let static_id = format!("{}.{}", author, namespace);
    let mut contents = template.contents.to_string();

    if template.path == "OniModTemplate.csproj" {
        contents = contents
            .replace(
                "<ModTitle>OniModTemplate</ModTitle>",
                &format!("<ModTitle>{}</ModTitle>", xml_escape(name)),
            )
            .replace(
                "<ModDescription>这是一个示例 Mod</ModDescription>",
                &format!(
                    "<ModDescription>{}</ModDescription>",
                    xml_escape(description)
                ),
            )
            .replace(
                "<ModStaticID>YourName.OniModTemplate</ModStaticID>",
                &format!("<ModStaticID>{}</ModStaticID>", xml_escape(&static_id)),
            )
            .replace(
                "<ModVersion>0.1.7</ModVersion>",
                &format!("<ModVersion>{}</ModVersion>", xml_escape(mod_version)),
            );
    }

    contents = contents.replace("OniModTemplate", namespace);
    if template.path == "README.md" {
        contents = contents.replace(&format!("# {}", namespace), &format!("# {}", name));
    }

    contents
}

fn write_new_file(path: &Path, contents: &str) -> Result<()> {
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(path)
        .with_context(|| format!("创建文件失败：{}", path.display()))?;
    file.write_all(contents.as_bytes())
        .with_context(|| format!("写入文件失败：{}", path.display()))
}

fn write_mod_template(
    mod_dir: &Path,
    name: &str,
    namespace: &str,
    author: &str,
    description: &str,
    mod_version: &str,
) -> Result<()> {
    fs::create_dir_all(mod_dir)
        .with_context(|| format!("创建 Mod 目录失败：{}", mod_dir.display()))?;

    for template in MOD_TEMPLATE {
        let relative_path = if template.path == "OniModTemplate.csproj" {
            format!("{}.csproj", namespace)
        } else {
            template.path.to_string()
        };
        let destination = mod_dir.join(&relative_path);
        if let Some(parent) = destination.parent() {
            fs::create_dir_all(parent)
                .with_context(|| format!("创建目录失败：{}", parent.display()))?;
        }

        let contents =
            rendered_template_file(template, name, namespace, author, description, mod_version);
        write_new_file(&destination, &contents)?;
    }

    Ok(())
}

fn is_empty_directory(path: &Path) -> Result<bool> {
    let mut entries =
        fs::read_dir(path).with_context(|| format!("读取目标目录失败：{}", path.display()))?;
    Ok(entries.next().is_none())
}

fn is_onim_project(path: &Path) -> bool {
    path.join(CONFIG_FILE).is_file() && path.join("mods").is_dir()
}

fn create_project(
    root: &Path,
    name: &str,
    author: &str,
    description: &str,
    mod_version: &str,
) -> Result<()> {
    if !is_empty_directory(root)? {
        bail!(
            "目标目录不为空，为避免覆盖现有文件，已取消初始化：{}",
            root.display()
        );
    }

    let namespace = validate_mod_name(name)?;
    let mods_dir = root.join("mods");
    fs::create_dir(&mods_dir)
        .with_context(|| format!("创建 Mod 根目录失败：{}", mods_dir.display()))?;
    let mod_dir = mods_dir.join(name);
    fs::create_dir(&mod_dir)
        .with_context(|| format!("创建 Mod 目录失败：{}", mod_dir.display()))?;
    write_mod_template(&mod_dir, name, &namespace, author, description, mod_version)?;

    let config = format!(
        "# onim 项目配置\n# 首次构建前，请在 Directory.Build.props 中填写本机的 OniGamePath。\n\ndefault_mod = \"{name}\"\n\n[mods.{name}]\npath = \"mods/{name}\"\n"
    );
    let readme = format!(
        "# {name}\n\n这是一个《缺氧》（Oxygen Not Included）Mod 工程，由 `onim init` 创建。\n\n## 构建\n\n1. 在 `Directory.Build.props` 中填写游戏安装目录的 `OniGamePath`。\n2. 在本目录运行 `onim build`。\n3. 需要安装到开发目录时运行 `onim dev`。\n\nMod 源码位于 `mods/{name}/`。\n"
    );
    let gitignore = "# Local game path\nDirectory.Build.props\n\n# .NET build outputs\n**/bin/\n**/obj/\n\n# Mod packages\ndist/\n";

    write_new_file(&root.join("onim.toml"), &config)?;
    write_new_file(
        &root.join("Directory.Build.props.example"),
        BUILD_PROPS_EXAMPLE,
    )?;
    write_new_file(&root.join("Directory.Build.props"), BUILD_PROPS_EXAMPLE)?;
    write_new_file(&root.join(".gitignore"), gitignore)?;
    write_new_file(&root.join("README.md"), &readme)?;

    println!("✅ 缺氧 Mod 工程初始化完成：{}", root.display());
    println!("   Mod：{}（命名空间：{}）", name, namespace);
    println!("   首次构建前，编辑 Directory.Build.props 填写 OniGamePath");
    println!("   然后运行：onim build");
    Ok(())
}

fn add_mod_to_project(
    root: &Path,
    name: &str,
    author: &str,
    description: &str,
    mod_version: &str,
) -> Result<()> {
    let namespace = validate_mod_name(name)?;
    let config_path = root.join(CONFIG_FILE);
    let content = fs::read_to_string(&config_path)
        .with_context(|| format!("读取配置文件失败：{}", config_path.display()))?;
    let parsed: toml::Value = toml::from_str(&content)
        .with_context(|| format!("解析配置文件失败：{}", config_path.display()))?;
    if parsed
        .get("mods")
        .and_then(toml::Value::as_table)
        .is_some_and(|mods| mods.contains_key(name))
    {
        bail!("配置中已存在 Mod：{}", name);
    }
    if !parsed.get("mods").is_some_and(toml::Value::is_table) {
        bail!("{} 中没有 [mods] 配置表", CONFIG_FILE);
    }

    let mod_dir = root.join("mods").join(name);
    if mod_dir.exists() {
        bail!("Mod 目录已存在：{}", mod_dir.display());
    }
    write_mod_template(&mod_dir, name, &namespace, author, description, mod_version)?;

    let updated_config = format!(
        "{}\n\n[mods.{name}]\npath = \"mods/{name}\"\n",
        content.trim_end()
    );
    fs::write(&config_path, updated_config)
        .with_context(|| format!("更新 {} 失败", config_path.display()))?;

    println!("✅ Mod '{}' 创建成功！", name);
    println!("   目录：{}", mod_dir.display());
    println!("   已追加到 {}", config_path.display());
    println!("\n下一步：");
    println!("  onim build -m {}      构建", name);
    println!("  onim dev -m {}        开发测试", name);
    Ok(())
}

pub fn run(
    name: Option<String>,
    author: Option<String>,
    desc: Option<String>,
    mod_version: String,
    _config_path: Option<PathBuf>,
) -> Result<()> {
    crate::config::validate_mod_directory_name(name.as_deref().unwrap_or(DEFAULT_MOD_NAME))?;
    let cwd = env::current_dir()?;
    let author = author.unwrap_or_else(|| "YourName".to_string());
    let name_for_description = name.as_deref().unwrap_or(DEFAULT_MOD_NAME);
    let description = desc.unwrap_or_else(|| format!("{} Mod", name_for_description));

    if is_empty_directory(&cwd)? {
        let name = name.as_deref().unwrap_or(DEFAULT_MOD_NAME);
        return create_project(&cwd, name, &author, &description, &mod_version);
    }

    if is_onim_project(&cwd) {
        let name = name.context("在已有工程中添加 Mod 时请提供名称：`onim init <name>`")?;
        return add_mod_to_project(&cwd, &name, &author, &description, &mod_version);
    }

    bail!(
        "目标目录不为空且不是 onim 工程，未修改任何文件：{}",
        cwd.display()
    )
}

#[cfg(test)]
mod tests {
    use super::{DEFAULT_MOD_NAME, add_mod_to_project, create_project, validate_mod_name};
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::sync::atomic::{AtomicU64, Ordering};

    static NEXT_TEMP_DIR: AtomicU64 = AtomicU64::new(0);

    struct TestDir(PathBuf);

    impl TestDir {
        fn new() -> Self {
            let id = NEXT_TEMP_DIR.fetch_add(1, Ordering::Relaxed);
            let path =
                std::env::temp_dir().join(format!("onim-init-test-{}-{id}", std::process::id()));
            fs::create_dir(&path).expect("create test directory");
            Self(path)
        }

        fn path(&self) -> &Path {
            &self.0
        }
    }

    impl Drop for TestDir {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    #[test]
    fn empty_directory_gets_a_complete_project_using_embedded_templates() {
        let temp = TestDir::new();
        create_project(
            temp.path(),
            "fresh-mod",
            "ExampleAuthor",
            "A starter mod",
            "1.2.3",
        )
        .expect("initialize project");

        let expected = [
            "README.md",
            ".gitignore",
            "onim.toml",
            "Directory.Build.props",
            "Directory.Build.props.example",
            "mods/fresh-mod/FreshMod.csproj",
            "mods/fresh-mod/ModInfo.cs",
            "mods/fresh-mod/Patches/ExamplePatch.cs",
            "mods/fresh-mod/README.md",
            "mods/fresh-mod/README_EN.md",
            "mods/fresh-mod/CHANGELOG.md",
            "mods/fresh-mod/docs/steam-description-zh.md",
            "mods/fresh-mod/docs/steam-description-en.md",
        ];
        for relative in expected {
            assert!(temp.path().join(relative).is_file(), "missing {relative}");
        }

        let config = fs::read_to_string(temp.path().join("onim.toml")).unwrap();
        let parsed: toml::Value = toml::from_str(&config).unwrap();
        assert_eq!(parsed["default_mod"].as_str(), Some("fresh-mod"));
        assert_eq!(
            parsed["mods"]["fresh-mod"]["path"].as_str(),
            Some("mods/fresh-mod")
        );

        let project =
            fs::read_to_string(temp.path().join("mods/fresh-mod/FreshMod.csproj")).unwrap();
        assert!(project.contains("<ModTitle>fresh-mod</ModTitle>"));
        assert!(project.contains("<AssemblyName>FreshMod</AssemblyName>"));
        assert!(project.contains("<RootNamespace>FreshMod</RootNamespace>"));
        assert!(project.contains("<ModDescription>A starter mod</ModDescription>"));
        assert!(project.contains("<ModStaticID>ExampleAuthor.FreshMod</ModStaticID>"));
        assert!(project.contains("<ModVersion>1.2.3</ModVersion>"));
        assert!(!project.contains("OniModTemplate"));
        assert_eq!(
            fs::read_to_string(temp.path().join("Directory.Build.props")).unwrap(),
            fs::read_to_string(temp.path().join("Directory.Build.props.example")).unwrap()
        );
    }

    #[test]
    fn empty_directory_without_a_name_uses_the_standard_mod_name() {
        let temp = TestDir::new();
        create_project(
            temp.path(),
            DEFAULT_MOD_NAME,
            "YourName",
            "OniModTemplate Mod",
            "0.1.7",
        )
        .expect("initialize default project");

        assert!(
            temp.path()
                .join("mods/OniModTemplate/OniModTemplate.csproj")
                .is_file()
        );
        let config = fs::read_to_string(temp.path().join("onim.toml")).unwrap();
        assert!(config.contains("default_mod = \"OniModTemplate\""));
    }

    #[test]
    fn nonempty_directory_is_rejected_without_changing_existing_files() {
        let temp = TestDir::new();
        let sentinel = temp.path().join("keep.txt");
        fs::write(&sentinel, "user data").unwrap();

        let error = create_project(temp.path(), "FreshMod", "YourName", "FreshMod Mod", "0.1.7")
            .unwrap_err();

        assert!(error.to_string().contains("不为空"));
        assert_eq!(fs::read_to_string(sentinel).unwrap(), "user data");
        assert_eq!(fs::read_dir(temp.path()).unwrap().count(), 1);
    }

    #[test]
    fn existing_onim_project_can_add_a_mod_from_the_embedded_template() {
        let temp = TestDir::new();
        create_project(
            temp.path(),
            DEFAULT_MOD_NAME,
            "YourName",
            "OniModTemplate Mod",
            "0.1.7",
        )
        .unwrap();

        add_mod_to_project(
            temp.path(),
            "OtherMod",
            "ExampleAuthor",
            "Description",
            "2.0.0",
        )
        .expect("add module");

        assert!(temp.path().join("mods/OtherMod/OtherMod.csproj").is_file());
        let config = fs::read_to_string(temp.path().join("onim.toml")).unwrap();
        let parsed: toml::Value = toml::from_str(&config).unwrap();
        assert_eq!(
            parsed["mods"]["OtherMod"]["path"].as_str(),
            Some("mods/OtherMod")
        );
    }

    #[test]
    fn unsafe_names_are_rejected_before_creating_paths() {
        let temp = TestDir::new();
        assert!(validate_mod_name("../outside").is_err());
        assert!(
            create_project(
                temp.path(),
                "../outside",
                "YourName",
                "Description",
                "0.1.7"
            )
            .is_err()
        );
        assert_eq!(fs::read_dir(temp.path()).unwrap().count(), 0);
    }
}
