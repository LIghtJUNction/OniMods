//! Publication orchestration keeps Steam initialization out of the CLI parent.

use anyhow::{Context, Result, bail, ensure};
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use super::journal::{Journal, State, lock};
use super::package::{Input, Plan, state_root, steam_library};
use super::worker::{Report, Request, require_no_duplicate, require_target};
use crate::config::{Config, SelectedMod};

/// Whether to stop after preparing files or perform the requested publication.
#[derive(Debug, Clone, Copy)]
pub(crate) enum Execution {
    DryRun,
    Publish,
}

/// Build an immutable native publication plan, optionally without contacting Steam.
pub(crate) fn upload(
    cfg: &Config,
    selected: &SelectedMod,
    input: Input<'_>,
    execution: Execution,
) -> Result<()> {
    let path = Plan::prepare(cfg, selected, input)?;
    let plan = Plan::load(&path)?;
    println!("publicationPlan={}", path.display());
    println!(
        "planSha256={}; zipSha256={}; bytes={}",
        plan.sha256, plan.zip_sha256, plan.zip_bytes
    );
    if matches!(execution, Execution::DryRun) {
        println!("Native Legacy ZIP dry-run passed; Steam API not initialized.");
        return Ok(());
    }
    resume(&path)
}

/// Resume from durable observations without repeating uncertain Steam writes.
pub(crate) fn resume(path: &Path) -> Result<()> {
    let path = fs::canonicalize(path)?;
    let plan = Plan::load(&path)?;
    ensure!(
        plan.owner != 0,
        "计划没有固定账号；请配置 workshop_owner 后重新准备"
    );
    let _lock = lock(&plan)?;
    let journal = Journal::open(&path, &plan)?;
    let id = match journal.last("upload")? {
        Some(event) if event.state == State::Complete => event.item_id,
        Some(_) => {
            println!("Recovering an uncertain upload using read-only server and ZIP checks...");
            let id = if plan.target_id != 0 {
                plan.target_id
            } else {
                let report = run(Request::Inspect {
                    library: plan.library.clone(),
                    owner: plan.owner,
                    result: result_path()?,
                })?;
                let matching: Vec<_> = report
                    .items
                    .iter()
                    .filter(|item| item.title == plan.title)
                    .collect();
                ensure!(
                    matching.len() == 1,
                    "不确定的首次创建没有唯一可核对条目；禁止再次创建"
                );
                matching[0].id
            };
            run(Request::Verify {
                plan: path.clone(),
                id,
                result: result_path()?,
            })?;
            journal.complete("upload", id)?;
            id
        }
        None => {
            println!("Inspecting the Steam owner and existing Workshop items...");
            let report = run(Request::Inspect {
                library: plan.library.clone(),
                owner: plan.owner,
                result: result_path()?,
            })?;
            if plan.target_id == 0 {
                require_no_duplicate(&report.items, &plan)?;
            } else {
                let item = report
                    .items
                    .iter()
                    .find(|item| item.id == plan.target_id)
                    .context("上传器账号下找不到配置中的目标条目")?;
                require_target(item, &plan)?;
            }
            println!("Uploading one Legacy ZIP; no automatic write retries...");
            run(Request::ConsumerInspect {
                library: plan.library.clone(),
                owner: plan.owner,
                ids: report.items.iter().take(2).map(|item| item.id).collect(),
                result: result_path()?,
            })?;
            if plan.target_id != 0 && !plan.dependencies.is_empty() {
                println!("Ensuring Workshop prerequisites before updating the payload...");
                run(Request::Prerequisites {
                    plan: path.clone(),
                    id: plan.target_id,
                    result: result_path()?,
                })?;
            }
            run(Request::Upload {
                plan: path.clone(),
                result: result_path()?,
            })?
            .item_id
        }
    };
    println!("Verifying game-context download, localized metadata and prerequisites...");
    let report = run(Request::Finish {
        plan: path.clone(),
        id,
        result: result_path()?,
    })?;
    let item = report.items.first().context("发布验证缺少最终条目")?;
    println!("Published and downloaded-byte verified: {}", plan.title);
    println!(
        "workshopId={id}; visibility={}; zipSha256={}",
        item.visibility, plan.zip_sha256
    );
    println!("https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
    if plan.target_id == 0 {
        println!(
            "请在 [mods.{}] 登记 publishedfileid = \"{id}\"，后续直接更新此 ID。",
            plan.name
        );
    }
    Ok(())
}

/// Query account and item identity without building or sending any upload.
pub(crate) fn preflight(cfg: &Config, selected: &SelectedMod, owner: u64) -> Result<()> {
    let report = run(Request::Inspect {
        library: steam_library(&cfg.game_path)?,
        owner,
        result: result_path()?,
    })?;
    let target = selected
        .config
        .publishedfileid
        .as_deref()
        .unwrap_or("0")
        .parse::<u64>()?;
    let consumer = run(Request::ConsumerInspect {
        library: steam_library(&cfg.game_path)?,
        owner,
        ids: report
            .items
            .iter()
            .filter(|item| target == 0 || item.id == target)
            .take(2)
            .map(|item| item.id)
            .collect(),
        result: result_path()?,
    })?;
    println!(
        "Consumer context verified {} matching Legacy items.",
        consumer.items.len()
    );
    println!(
        "Steam owner={}; ownedItems={}",
        report.owner,
        report.items.len()
    );
    for item in report.items {
        if target == 0 || item.id == target {
            item.require_identity(item.id, owner)?;
            println!(
                "item={}; title={}; visibility={}; legacyBytes={}",
                item.id, item.title, item.visibility, item.bytes
            );
        }
    }
    println!("Native preflight completed without uploading.");
    Ok(())
}

fn result_path() -> Result<PathBuf> {
    let directory = state_root()?.join("workers");
    fs::create_dir_all(&directory)?;
    let nanos = SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos();
    Ok(directory.join(format!("{}-{nanos}.result.json", std::process::id())))
}

fn run(request: Request) -> Result<Report> {
    let result = request.result_path().to_owned();
    let input = result.with_extension("request.json");
    let log_path = result.with_extension("log");
    let mut file = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&input)?;
    file.write_all(&serde_json::to_vec(&request)?)?;
    file.sync_all()?;
    let log = fs::File::create(&log_path)?;
    let mut child = Command::new(std::env::current_exe()?)
        .arg("__steam-worker")
        .arg(&input)
        .env("SteamAppId", request.app().to_string())
        .env("SteamGameId", request.app().to_string())
        .stdin(Stdio::null())
        .stdout(log.try_clone()?)
        .stderr(log)
        .spawn()?;
    let deadline = Instant::now() + Duration::from_secs(1800);
    let status = loop {
        if let Some(status) = child.try_wait()? {
            break status;
        }
        if Instant::now() >= deadline {
            child.kill()?;
            child.wait()?;
            bail!(
                "Steam worker 超时；检查 {} 和发布日志，不要重复写入",
                log_path.display()
            );
        }
        std::thread::sleep(Duration::from_millis(100));
    };
    if !status.success() {
        let message = fs::read_to_string(&log_path).unwrap_or_default();
        let tail: Vec<_> = message.lines().rev().take(12).collect();
        bail!(
            "Steam worker 失败；日志 {}\n{}",
            log_path.display(),
            tail.into_iter().rev().collect::<Vec<_>>().join("\n")
        );
    }
    let report = serde_json::from_slice(&fs::read(&result)?)?;
    for path in [&input, &result, &log_path] {
        fs::remove_file(path)?;
    }
    Ok(report)
}
