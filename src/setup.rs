use anyhow::{Context, Result};
use std::env;
use std::fs;
use std::io::{self, Write};
use std::path::PathBuf;
use std::process::Command;

const CONFIG_FILE: &str = "oni-mods.toml";
const BUILD_PROPS: &str = "Directory.Build.props";

fn check_command(cmd: &str) -> bool {
    Command::new("sh")
        .args(["-c", &format!("command -v {} > /dev/null 2>&1", cmd)])
        .status()
        .map(|s| s.success())
        .unwrap_or(false)
}

fn prompt(question: &str, default: Option<&str>) -> Result<String> {
    print!("{}", question);
    io::stdout().flush()?;
    let mut buf = String::new();
    io::stdin().read_line(&mut buf)?;
    let trimmed = buf.trim().to_string();
    if trimmed.is_empty() {
        if let Some(d) = default {
            Ok(d.to_string())
        } else {
            Ok(trimmed)
        }
    } else {
        Ok(trimmed)
    }
}

fn auto_detect() -> Option<PathBuf> {
    let home = env::var_os("HOME")?;

    #[cfg(target_os = "linux")]
    {
        let p = PathBuf::from(&home)
            .join(".local/share/Steam/steamapps/common/OxygenNotIncluded");
        if p.join("OxygenNotIncluded_Data/Managed/Assembly-CSharp.dll").exists() {
            return Some(p);
        }
    }

    #[cfg(target_os = "macos")]
    {
        let p = PathBuf::from(&home)
            .join("Library/Application Support/Steam/steamapps/common/OxygenNotIncluded");
        let mac_managed = p
            .join("OxygenNotIncluded.app/Contents/OxygenNotIncluded_Data/Managed/Assembly-CSharp.dll");
        if mac_managed.exists() {
            return Some(p);
        }
    }

    #[cfg(target_os = "windows")]
    {
        for base in [
            "C:\\Program Files (x86)\\Steam\\steamapps\\common\\OxygenNotIncluded",
            "C:\\Program Files\\Steam\\steamapps\\common\\OxygenNotIncluded",
        ] {
            let p = PathBuf::from(base);
            if p.join("OxygenNotIncluded_Data\\Managed\\Assembly-CSharp.dll").exists() {
                return Some(p);
            }
        }
    }

    None
}

fn validate_game_path(path: &PathBuf) -> Result<Vec<String>> {
    let mut errors = vec![];
    let managed = if cfg!(target_os = "macos") {
        let mac = path
            .join("OxygenNotIncluded.app/Contents/OxygenNotIncluded_Data/Managed");
        if mac.exists() { mac } else { path.join("OxygenNotIncluded_Data/Managed") }
    } else if cfg!(target_os = "windows") {
        path.join("OxygenNotIncluded_Data\\Managed")
    } else {
        path.join("OxygenNotIncluded_Data/Managed")
    };

    let required = [
        ("Assembly-CSharp.dll", "游戏主逻辑 DLL"),
        ("0Harmony.dll", "Harmony 补丁框架"),
        ("UnityEngine.CoreModule.dll", "Unity 引擎核心"),
    ];

    for (file, desc) in &required {
        let p = managed.join(file);
        if !p.exists() {
            errors.push(format!("缺少 {} ({}) 在 {}", file, desc, p.display()));
        }
    }

    Ok(errors)
}

pub fn run() -> Result<()> {
    println!("🛠️  onim 项目初始化\n");

    // 检查依赖
    println!("📋 检查依赖...");
    let mut missing = vec![];

    if !check_command("dotnet") {
        missing.push("dotnet (.NET SDK)");
    }
    #[cfg(not(target_os = "windows"))]
    {
        if !check_command("unzip") {
            missing.push("unzip");
        }
    }
    if !check_command("tar") {
        missing.push("tar");
    }

    if !missing.is_empty() {
        println!("⚠️  缺少必要工具：");
        for m in &missing {
            println!("   ❌ {}", m);
        }
        println!();
        println!("请安装后重试：");
        println!("  .NET SDK: https://dotnet.microsoft.com/download");
        #[cfg(not(target_os = "windows"))]
        {
            println!("  unzip: sudo apt install unzip  (或对应包管理器)");
        }
        anyhow::bail!("缺少依赖");
    }
    println!("✅ 所有依赖已安装\n");

    let cwd = env::current_dir()?;
    let config_path = cwd.join(CONFIG_FILE);
    let props_path = cwd.join(BUILD_PROPS);

    // 1. 检查/询问游戏路径
    let mut game_path = None;

    if config_path.exists() {
        let content = fs::read_to_string(&config_path).unwrap_or_default();
        for line in content.lines() {
            if line.trim_start().starts_with("game_path") {
                if let Some((_, val)) = line.split_once('=') {
                    let p = val.trim().trim_matches('"').trim_matches('\'');
                    let pb = PathBuf::from(p);
                    if pb.exists() {
                        game_path = Some(pb);
                    }
                }
            }
        }
    }

    if let Some(ref path) = game_path {
        println!("检测到现有游戏路径：{}", path.display());
        let answer = prompt("路径是否正确？ [Y/n] ", Some("Y"))?;
        if answer.eq_ignore_ascii_case("n") {
            game_path = None;
        }
    }

    if game_path.is_none() {
        if let Some(detected) = auto_detect() {
            println!("\n自动检测到游戏路径：{}", detected.display());
            let answer = prompt("使用此路径？ [Y/n] ", Some("Y"))?;
            if answer.eq_ignore_ascii_case("n") {
                game_path = None;
            } else {
                game_path = Some(detected);
            }
        }
    }

    if game_path.is_none() {
        println!("\n请输入缺氧游戏安装目录（包含 OxygenNotIncluded 可执行文件的那一层）：");
        let input = prompt("> ", None)?;
        if input.is_empty() {
            anyhow::bail!("未提供游戏路径，初始化取消");
        }
        game_path = Some(PathBuf::from(input));
    }

    let game_path = game_path.unwrap();

    // 2. 验证
    println!("\n🔍 验证游戏文件...");
    let errors = validate_game_path(&game_path)?;
    if !errors.is_empty() {
        println!("⚠️  发现问题：");
        for e in &errors {
            println!("   - {}", e);
        }
        let answer = prompt("文件验证未通过，是否仍继续？ [y/N] ", Some("N"))?;
        if !answer.eq_ignore_ascii_case("y") {
            anyhow::bail!("初始化已取消");
        }
    } else {
        println!("✅ 所有关键文件验证通过");
    }

    // 3. 写入 oni-mods.toml（只包含 Mod 列表，游戏路径在 Directory.Build.props 中）
    let toml_content = r#"# onim 配置文件
# 游戏路径在 Directory.Build.props 中统一管理

# 默认 Mod（不指定 -m 时使用）
default_mod = "OniModTemplate"

[mods.OniModTemplate]
path = "mods/OniModTemplate"
"#;

    println!("\n📝 写入 {} ...", CONFIG_FILE);
    fs::write(&config_path, toml_content)
        .with_context(|| format!("写入 {} 失败", CONFIG_FILE))?;

    // 4. 写入 Directory.Build.props
    let sep = std::path::MAIN_SEPARATOR_STR;
    let props_content = format!(
        r#"<Project>

  <!-- onim 自动生成的全局配置 -->

  <PropertyGroup>
    <OniGamePath>{}</OniGamePath>
  </PropertyGroup>

  <PropertyGroup>
    <OniManagedPath>$(OniGamePath){}OxygenNotIncluded_Data{}Managed</OniManagedPath>
  </PropertyGroup>

  <Target Name="ValidateOniGamePath" BeforeTargets="BeforeBuild">
    <Error Text="游戏路径未配置！" Condition="'$(OniGamePath)' == ''" />
  </Target>

</Project>
"#,
        game_path.to_string_lossy().replace('"', "&quot;"),
        sep, sep
    );

    println!("📝 写入 {} ...", BUILD_PROPS);
    fs::write(&props_path, props_content)
        .with_context(|| format!("写入 {} 失败", BUILD_PROPS))?;

    println!("\n✅ 初始化完成！");
    println!("   游戏路径：{}", game_path.display());
    println!("   配置文件：{}", config_path.display());
    println!("   MSBuild 配置：{}", props_path.display());
    println!();
    println!("下一步：");
    println!("  onim build       构建默认 Mod");
    println!("  onim init <name> 创建新 Mod");

    Ok(())
}
