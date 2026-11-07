use anyhow::{Result, bail};
use clap::{Parser, Subcommand, ValueEnum};
use std::path::PathBuf;

mod archive;
mod build;
mod config;
mod dev;
mod doctor;
mod embedded_publisher;
mod game_process;
mod identity;
mod info;
mod init;
mod install;
mod mod_config_backup;
mod publish;
mod remote;
mod setup;
mod steam;
mod uninstall;
mod workshop;

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
        /// Mod 名称（英文，无空格）；在空目录中省略时使用 OniModTemplate
        name: Option<String>,
        /// 作者名
        #[arg(short, long)]
        author: Option<String>,
        /// Mod 描述
        #[arg(short, long)]
        desc: Option<String>,
        /// Mod 版本号
        #[arg(long, default_value = "0.1.7")]
        mod_version: String,
    },
    /// 构建 Mod（默认 Debug，加 --release 为 Release）
    Build {
        #[arg(short, long)]
        release: bool,
        /// 构建全部已配置 Mod
        #[arg(long)]
        all: bool,
    },
    /// 开发模式：构建并安装到游戏 Dev 目录
    Dev {
        /// 开发模式构建并安装全部已配置 Mod
        #[arg(long)]
        all: bool,
    },
    /// 正式安装到游戏 Local 目录
    Install {
        /// 从 GitHub 最新 Release 下载并安装
        #[arg(long, conflicts_with = "repo")]
        release: Option<String>,
        /// 从 GitHub 源代码仓库扫描、构建并安装
        #[arg(long, conflicts_with = "release")]
        repo: Option<String>,
        /// GitHub 源代码仓库分支、标签或提交
        #[arg(long, conflicts_with = "release")]
        r#ref: Option<String>,
    },
    /// 从游戏目录卸载 Mod
    Uninstall {
        /// 卸载范围
        #[arg(short, long, value_enum, default_value_t = UninstallScope::All)]
        scope: UninstallScope,
    },
    /// 查看已安装的 Mod 信息
    Info,
    /// 发布到 Steam 创意工坊（默认通过 Legacy ZIP 发布器纯命令行上传）
    Publish {
        /// 使用 OniUploader GUI 代替默认的纯命令行发布
        #[arg(long)]
        gui: bool,
        /// 自动使用最新更新日志作为上传说明，不弹更新说明输入
        #[arg(long)]
        auto_note: bool,
        /// 禁止交互提示；非交互式纯命令行发布使用单文件 Legacy ZIP
        #[arg(long, visible_alias = "yes", conflicts_with = "gui")]
        non_interactive: bool,
        /// 仅构建并生成 Workshop VDF，不上传
        #[arg(long, conflicts_with = "gui")]
        dry_run: bool,
        /// 首次创建条目，先以 Private 创建并验证；不会自动重试创建
        #[arg(long, conflicts_with = "gui")]
        create: bool,
        /// 最终可见性；已有条目默认保持原值，首次创建默认 Private
        #[arg(long, value_enum, conflicts_with = "gui")]
        visibility: Option<workshop::Visibility>,
        /// 固定本次发布账号的 Steam ID，覆盖 onim.toml 的 workshop_owner
        #[arg(long, conflicts_with = "gui")]
        expected_owner: Option<u64>,
        /// 读取既有不可变计划并继续验证；不会重复不确定的写入
        #[arg(long, conflicts_with_all = ["gui", "dry_run", "create", "preflight"])]
        resume: Option<PathBuf>,
        /// 只读检查 Steam 账号和条目，不构建、不上传
        #[arg(long, conflicts_with_all = ["gui", "dry_run", "create", "resume"])]
        preflight: bool,
    },
    #[command(name = "__steam-worker", hide = true)]
    SteamWorker { request: PathBuf },
    /// 列出所有配置的 Mod
    List,
    /// 检查本机 ONI Mod 开发环境
    Doctor,
    /// 搜索、安装或卸载 Steam 创意工坊 Mod
    Workshop {
        #[command(subcommand)]
        command: WorkshopCommands,
    },
}

#[derive(Subcommand)]
enum WorkshopCommands {
    /// 搜索 ONI 创意工坊
    Search { query: String },
    /// 订阅并安装一个 Legacy ZIP 条目
    Install { id: String },
    /// 取消订阅一个条目
    Uninstall { id: String },
    /// 查看或修改已发布条目的工坊页面，不替换 Legacy ZIP
    Page {
        #[command(subcommand)]
        command: PageCommands,
    },
}

#[derive(Subcommand)]
enum PageCommands {
    /// 读取标题、双语说明、标签、可见性和 Legacy 文件状态
    Show { id: String },
    /// 修改页面字段。标签会整组替换；说明从 UTF-8 文件读取
    Set {
        id: String,
        #[arg(long)]
        title: Option<String>,
        #[arg(long)]
        description_file: Option<PathBuf>,
        #[arg(long)]
        chinese_title: Option<String>,
        #[arg(long)]
        chinese_description_file: Option<PathBuf>,
        /// 逗号分隔。会替换该条目的全部标签
        #[arg(long)]
        tags: Option<String>,
        /// public、friends、private 或 unlisted
        #[arg(long)]
        visibility: Option<String>,
        #[arg(long)]
        preview: Option<PathBuf>,
        #[arg(long)]
        change_note: Option<String>,
    },
}

#[derive(Clone, ValueEnum)]
pub enum UninstallScope {
    Dev,
    Local,
    All,
}

fn main() -> Result<()> {
    let cli = Cli::parse();

    // Setup、Init 和 Doctor 不需要严格加载配置。
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
        Commands::Doctor => return doctor::run(cli.config.clone()),
        Commands::SteamWorker { request } => workshop::worker_main(request),
        _ => {}
    }

    let cfg = config::load(cli.config)?;

    match cli.command {
        Commands::Build { release, all } => {
            if all {
                if cli.r#mod.is_some() {
                    bail!("--all 与 -m/--mod 不可同时使用");
                }
                for selected in cfg.select_all_mods()? {
                    build::run(&cfg, &selected, release)?;
                }
                Ok(())
            } else {
                let selected = cfg.select_mod(cli.r#mod)?;
                build::run(&cfg, &selected, release).map(|_| ())
            }
        }
        Commands::Dev { all } => {
            if all {
                if cli.r#mod.is_some() {
                    bail!("--all 与 -m/--mod 不可同时使用");
                }
                for selected in cfg.select_all_mods()? {
                    dev::run(&cfg, &selected)?;
                }
                Ok(())
            } else {
                let selected = cfg.select_mod(cli.r#mod)?;
                dev::run(&cfg, &selected)
            }
        }
        Commands::Install {
            release,
            repo,
            r#ref,
        } => match (release, repo) {
            (Some(source), None) => remote::install_release(&cfg, cli.r#mod, &source),
            (None, Some(source)) => {
                remote::install_repository(&cfg, cli.r#mod, &source, r#ref.as_deref())
            }
            (None, None) if r#ref.is_some() => bail!("--ref 只能与 --repo 同用"),
            (None, None) => {
                let selected = cfg.select_mod(cli.r#mod)?;
                install::run(&cfg, &selected)
            }
            _ => unreachable!(),
        },
        Commands::Uninstall { scope } => {
            let selected = cfg.select_mod(cli.r#mod)?;
            uninstall::run(&cfg, &selected, scope)
        }
        Commands::Info => info::run(&cfg),
        Commands::Workshop { command } => match command {
            WorkshopCommands::Search { query } => install::workshop(&cfg, "search", &query),
            WorkshopCommands::Install { id } => install::workshop(&cfg, "install", &id),
            WorkshopCommands::Uninstall { id } => install::workshop(&cfg, "uninstall", &id),
            WorkshopCommands::Page { command } => match command {
                PageCommands::Show { id } => install::workshop_page_show(&cfg, &id),
                PageCommands::Set {
                    id,
                    title,
                    description_file,
                    chinese_title,
                    chinese_description_file,
                    tags,
                    visibility,
                    preview,
                    change_note,
                } => install::workshop_page_set(
                    &cfg,
                    &id,
                    install::WorkshopPageEdit {
                        title,
                        description_file,
                        chinese_title,
                        chinese_description_file,
                        tags,
                        visibility,
                        preview,
                        change_note,
                    },
                ),
            },
        },
        Commands::Publish {
            gui,
            auto_note,
            non_interactive,
            dry_run,
            create,
            visibility,
            expected_owner,
            resume,
            preflight,
        } => {
            let selected = cfg.select_mod(cli.r#mod)?;
            publish::run(
                &cfg,
                &selected,
                publish::PublishOptions {
                    use_gui: gui,
                    auto_note,
                    non_interactive,
                    dry_run,
                    create,
                    visibility,
                    expected_owner,
                    resume,
                    preflight,
                },
            )
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

#[cfg(test)]
mod tests {
    use super::Cli;
    use clap::Parser;

    #[test]
    fn init_command_accepts_an_optional_name() {
        assert!(Cli::try_parse_from(["onim", "init"]).is_ok());
        assert!(Cli::try_parse_from(["onim", "init", "ExampleMod"]).is_ok());
    }

    #[test]
    fn install_accepts_github_release_and_source_repository() {
        assert!(
            Cli::try_parse_from([
                "onim",
                "install",
                "--release",
                "LIghtJUNction/CycleTrim",
                "-m",
                "CycleTrim"
            ])
            .is_ok()
        );
        assert!(
            Cli::try_parse_from([
                "onim",
                "install",
                "--repo",
                "https://github.com/owner/repo",
                "--ref",
                "main"
            ])
            .is_ok()
        );
        assert!(
            Cli::try_parse_from([
                "onim",
                "install",
                "--release",
                "owner/repo",
                "--repo",
                "other/repo"
            ])
            .is_err()
        );
    }

    #[test]
    fn workshop_install_lifecycle_commands_parse() {
        assert!(Cli::try_parse_from(["onim", "workshop", "search", "CycleTrim"]).is_ok());
        assert!(Cli::try_parse_from(["onim", "workshop", "install", "12345"]).is_ok());
        assert!(Cli::try_parse_from(["onim", "workshop", "uninstall", "12345"]).is_ok());
    }

    #[test]
    fn native_publication_flags_are_explicit_and_do_not_open_the_gui() {
        assert!(
            Cli::try_parse_from([
                "onim",
                "publish",
                "--create",
                "--visibility",
                "public",
                "--yes"
            ])
            .is_ok()
        );
        assert!(Cli::try_parse_from(["onim", "publish", "--preflight"]).is_ok());
        for incompatible in ["--create", "--non-interactive", "--preflight", "--dry-run"] {
            assert!(
                Cli::try_parse_from(["onim", "publish", "--gui", incompatible]).is_err(),
                "accepted {incompatible}"
            );
        }
        assert!(
            Cli::try_parse_from(["onim", "publish", "--resume", "plan.json", "--create"]).is_err()
        );
    }
}
