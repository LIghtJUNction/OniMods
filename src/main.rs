use anyhow::Result;
use clap::{Parser, Subcommand, ValueEnum};
use std::path::PathBuf;

mod build;
mod config;
mod dev;
mod info;
mod init;
mod install;
mod publish;
mod setup;
mod uninstall;

#[derive(Parser)]
#[command(name = "onim")]
#[command(about = "缺氧 (Oxygen Not Included) Mod 开发 CLI 工具")]
#[command(version)]
struct Cli {
    #[command(subcommand)]
    command: Commands,

    /// 指定配置文件路径
    #[arg(short, long, global = true)]
    config: Option<PathBuf>,

    /// 指定要操作的 Mod（不指定则使用默认/第一个 Mod）
    #[arg(short, long, global = true)]
    r#mod: Option<String>,
}

#[derive(Subcommand)]
enum Commands {
    /// 初始化项目配置（检测游戏路径、写入配置文件）
    Setup,
    /// 从模板创建新 Mod
    Init {
        /// Mod 名称（英文，无空格）
        name: String,
        /// 作者名
        #[arg(short, long)]
        author: Option<String>,
        /// Mod 描述
        #[arg(short, long)]
        desc: Option<String>,
        /// Mod 版本号
        #[arg(long, default_value = "0.1.0")]
        mod_version: String,
    },
    /// 构建 Mod（默认 Debug，加 --release 为 Release）
    Build {
        #[arg(short, long)]
        release: bool,
    },
    /// 开发模式：构建并安装到游戏 Dev 目录
    Dev,
    /// 正式安装到游戏 Local 目录
    Install,
    /// 从游戏目录卸载 Mod
    Uninstall {
        /// 卸载范围
        #[arg(short, long, value_enum, default_value_t = UninstallScope::All)]
        scope: UninstallScope,
    },
    /// 查看已安装的 Mod 信息
    Info,
    /// 发布到 Steam 创意工坊
    Publish {
        /// 强制使用 OniUploader GUI（不用 SteamCMD）
        #[arg(long)]
        gui: bool,
    },
    /// 列出所有配置的 Mod
    List,
}

#[derive(Clone, ValueEnum)]
pub enum UninstallScope {
    Dev,
    Local,
    All,
}

fn main() -> Result<()> {
    let cli = Cli::parse();

    // Setup 和 Init 不需要先加载配置
    match &cli.command {
        Commands::Setup => return setup::run(),
        Commands::Init {
            name,
            author,
            desc,
            mod_version,
        } => {
            return init::run(
                name.clone(),
                author.clone(),
                desc.clone(),
                mod_version.clone(),
                cli.config,
            );
        }
        _ => {}
    }

    let cfg = config::load(cli.config)?;

    match cli.command {
        Commands::Build { release } => {
            let selected = cfg.select_mod(cli.r#mod)?;
            build::run(&cfg, &selected, release)
        }
        Commands::Dev => {
            let selected = cfg.select_mod(cli.r#mod)?;
            dev::run(&cfg, &selected)
        }
        Commands::Install => {
            let selected = cfg.select_mod(cli.r#mod)?;
            install::run(&cfg, &selected)
        }
        Commands::Uninstall { scope } => {
            let selected = cfg.select_mod(cli.r#mod)?;
            uninstall::run(&cfg, &selected, scope)
        }
        Commands::Info => info::run(&cfg),
        Commands::Publish { gui } => {
            let selected = cfg.select_mod(cli.r#mod)?;
            publish::run(&cfg, &selected, gui)
        }
        Commands::List => {
            println!("已配置的 Mod：");
            let default = cfg.default_mod.clone();
            for (name, m) in &cfg.mods {
                let marker = if Some(name.to_string()) == default {
                    " ← 默认"
                } else {
                    ""
                };
                println!("  • {}  ({}){}", name, m.path.display(), marker);
            }
            Ok(())
        }
        _ => unreachable!(),
    }
}
