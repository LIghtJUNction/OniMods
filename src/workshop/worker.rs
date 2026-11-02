//! Short-lived Steam processes keep uploader and game contexts independent.

use anyhow::{Context, Result, ensure};
use serde::{Deserialize, Serialize};
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use super::abi::{CONSUMER_APP, CREATOR_APP};
use super::journal::{Journal, State};
use super::package::{Plan, Visibility};
use super::steam::{Item, Steam};

#[derive(Debug, Serialize, Deserialize)]
#[serde(tag = "operation", deny_unknown_fields)]
pub(super) enum Request {
    Inspect {
        library: PathBuf,
        owner: u64,
        result: PathBuf,
    },
    ConsumerInspect {
        library: PathBuf,
        owner: u64,
        ids: Vec<u64>,
        result: PathBuf,
    },
    Prerequisites {
        plan: PathBuf,
        id: u64,
        result: PathBuf,
    },
    Upload {
        plan: PathBuf,
        result: PathBuf,
    },
    Verify {
        plan: PathBuf,
        id: u64,
        result: PathBuf,
    },
    Finish {
        plan: PathBuf,
        id: u64,
        result: PathBuf,
    },
}

impl Request {
    pub(super) fn app(&self) -> u32 {
        match self {
            Self::Inspect { .. } | Self::Upload { .. } => CREATOR_APP,
            Self::Verify { .. }
            | Self::Finish { .. }
            | Self::ConsumerInspect { .. }
            | Self::Prerequisites { .. } => CONSUMER_APP,
        }
    }

    pub(super) fn result_path(&self) -> &Path {
        match self {
            Self::Inspect { result, .. }
            | Self::Upload { result, .. }
            | Self::Verify { result, .. }
            | Self::Finish { result, .. }
            | Self::ConsumerInspect { result, .. }
            | Self::Prerequisites { result, .. } => result,
        }
    }
}

#[derive(Debug, Serialize, Deserialize)]
pub(super) struct Report {
    pub owner: u64,
    pub items: Vec<Item>,
    pub item_id: u64,
}

/// Run a single Steam operation, then exit without unloading native threads.
pub(crate) fn worker_main(path: &Path) -> ! {
    let result = execute(path);
    let code = match result {
        Ok(()) => 0,
        Err(error) => {
            eprintln!("{error:#}");
            1
        }
    };
    let _ = std::io::stdout().flush();
    let _ = std::io::stderr().flush();
    // Steam's process resources are reclaimed by the OS. In particular, do not
    // unload the DLL while Steam threads run, or block in Shutdown after UGCRead.
    std::process::exit(code)
}

fn execute(path: &Path) -> Result<()> {
    let request: Request = serde_json::from_slice(&fs::read(path)?)?;
    let report = match &request {
        Request::Inspect { library, owner, .. } => {
            let steam = Steam::connect(library, CREATOR_APP, *owner)?;
            Report {
                owner: steam.owner,
                items: steam.owned()?,
                item_id: 0,
            }
        }
        Request::ConsumerInspect {
            library,
            owner,
            ids,
            ..
        } => {
            let steam = Steam::connect(library, CONSUMER_APP, *owner)?;
            let mut items = Vec::new();
            for id in ids {
                let item = steam.query(*id, "english")?;
                item.require_identity(*id, *owner)?;
                items.push(item);
            }
            Report {
                owner: steam.owner,
                items,
                item_id: 0,
            }
        }
        Request::Upload { plan, .. } => {
            let reviewed = Plan::load(plan)?;
            let steam = Steam::connect(&reviewed.library, CREATOR_APP, reviewed.owner)?;
            let id = upload(&steam, plan, &reviewed)?;
            Report {
                owner: steam.owner,
                items: Vec::new(),
                item_id: id,
            }
        }
        Request::Prerequisites { plan, id, .. } => {
            let reviewed = Plan::load(plan)?;
            ensure!(
                reviewed.target_id == *id && *id != 0,
                "只能为已有父条目预先关联前置"
            );
            let steam = Steam::connect(&reviewed.library, CONSUMER_APP, reviewed.owner)?;
            let journal = Journal::open(plan, &reviewed)?;
            prerequisites(&steam, &reviewed, &journal, *id)?;
            Report {
                owner: steam.owner,
                items: Vec::new(),
                item_id: *id,
            }
        }
        Request::Verify { plan, id, .. } => {
            let reviewed = Plan::load(plan)?;
            let steam = Steam::connect(&reviewed.library, CONSUMER_APP, reviewed.owner)?;
            let item = wait_item(&steam, &reviewed, *id, "english", |item| {
                item.bytes == reviewed.zip_bytes
            })?;
            steam.verify_bytes(&item, &reviewed)?;
            Report {
                owner: steam.owner,
                items: vec![item],
                item_id: *id,
            }
        }
        Request::Finish { plan, id, .. } => {
            let reviewed = Plan::load(plan)?;
            let steam = Steam::connect(&reviewed.library, CONSUMER_APP, reviewed.owner)?;
            let item = finish(&steam, plan, &reviewed, *id)?;
            Report {
                owner: steam.owner,
                items: vec![item],
                item_id: *id,
            }
        }
    };
    let mut file = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(request.result_path())?;
    file.write_all(&serde_json::to_vec_pretty(&report)?)?;
    file.sync_all()?;
    Ok(())
}

fn upload(steam: &Steam, path: &Path, plan: &Plan) -> Result<u64> {
    ensure!(plan.owner != 0, "发布计划没有固定账号");
    let journal = Journal::open(path, plan)?;
    ensure!(
        journal.last("upload")?.is_none(),
        "已有上传意图；只能通过 --resume 回读，不重复提交"
    );
    let current = if plan.target_id == 0 {
        let items = steam.owned()?;
        require_no_duplicate(&items, plan)?;
        None
    } else {
        let item = steam.details(plan.target_id)?;
        require_target(&item, plan)?;
        Some(item)
    };
    let zip_name = format!("onim_native_{}_{}.zip", plan.target_id, plan.zip_sha256);
    let preview_name = format!(
        "onim_native_{}_{}.{}",
        plan.target_id, plan.preview_sha256, plan.preview_extension
    );
    steam.share(&zip_name, &plan.payload(path)?)?;
    steam.share(&preview_name, &plan.preview(path)?)?;
    journal.begin("upload", plan.target_id)?;
    if plan.target_id == 0 {
        journal.reserve_creation(plan)?;
    }
    let id = steam.upload(plan, &zip_name, &preview_name, current.as_ref())?;
    journal.complete("upload", id)?;
    Ok(id)
}

pub(super) fn require_no_duplicate(items: &[Item], plan: &Plan) -> Result<()> {
    let title = plan.title.to_lowercase();
    let name = plan.name.to_lowercase();
    for item in items {
        ensure!(
            item.title.to_lowercase() != title && !item.title.to_lowercase().contains(&name),
            "已存在同名条目 {} ({})；登记其 publishedfileid 后更新，不能再次创建",
            item.title,
            item.id
        );
    }
    Ok(())
}

pub(super) fn require_target(item: &Item, plan: &Plan) -> Result<()> {
    item.require_identity(plan.target_id, plan.owner)?;
    ensure!(
        item.title.eq_ignore_ascii_case(&plan.title)
            || item
                .title
                .to_lowercase()
                .contains(&plan.name.to_lowercase()),
        "配置指向另一个 Mod：{} ({})",
        item.title,
        item.id
    );
    Ok(())
}

fn finish(steam: &Steam, path: &Path, plan: &Plan, id: u64) -> Result<Item> {
    ensure!(
        id != 0 && (plan.target_id == 0 || plan.target_id == id),
        "条目 ID 与计划不符"
    );
    let journal = Journal::open(path, plan)?;
    let upload = journal.last("upload")?.context("没有已核对的上传记录")?;
    ensure!(
        upload.state == State::Complete && upload.item_id == id,
        "上传尚未得到可用成功结果"
    );
    let initial = wait_item(steam, plan, id, "english", |item| {
        item.bytes == plan.zip_bytes
    })?;
    steam.verify_bytes(&initial, plan)?;
    journal.complete("payload-verified", id)?;
    for (language, description) in [("english", &plan.english), ("schinese", &plan.chinese)] {
        let step = format!("metadata-{language}");
        let item = steam.query(id, language)?;
        item.require_identity(id, plan.owner)?;
        if item.title != plan.title || item.description != *description {
            journal.begin(&step, id)?;
            steam.metadata(id, plan, language)?;
            wait_item(steam, plan, id, language, |item| {
                item.title == plan.title && item.description == *description
            })?;
        }
        journal.complete(&step, id)?;
    }
    prerequisites(steam, plan, &journal, id)?;
    let visibility = plan
        .visibility
        .map(Visibility::code)
        .unwrap_or(if plan.target_id == 0 {
            2
        } else {
            initial.visibility
        });
    let item = steam.query(id, "english")?;
    item.require_identity(id, plan.owner)?;
    if item.visibility != visibility {
        journal.begin("visibility", id)?;
        steam.visibility(id, visibility)?;
        wait_item(steam, plan, id, "english", |item| {
            item.visibility == visibility
        })?;
    }
    journal.complete("visibility", id)?;
    let english = wait_item(steam, plan, id, "english", |item| {
        item.visibility == visibility
            && item.title == plan.title
            && item.description == plan.english
            && plan
                .dependencies
                .iter()
                .all(|dependency| item.children.contains(&dependency.id))
    })?;
    wait_item(steam, plan, id, "schinese", |item| {
        item.title == plan.title
            && item.description == plan.chinese
            && item.visibility == visibility
    })?;
    steam.verify_bytes(&english, plan)?;
    journal.complete("published-verified", id)?;
    Ok(english)
}

fn prerequisites(steam: &Steam, plan: &Plan, journal: &Journal, id: u64) -> Result<()> {
    for required in &plan.dependencies {
        let before = steam.query(id, "english")?;
        before.require_identity(id, plan.owner)?;
        let child = steam.query(required.id, "english")?;
        child.require_identity(required.id, plan.owner)?;
        ensure!(
            child.visibility == 0 && child.title.eq_ignore_ascii_case(&required.title),
            "前置没有公开或标题与配置不符：{}",
            required.id
        );
        ensure!(!child.children.contains(&id), "前置关系会形成直接循环");
        let step = format!("dependency-{}", required.id);
        if !before.children.contains(&required.id) {
            journal.begin(&step, id)?;
            steam.add_dependency(id, required.id)?;
            wait_item(steam, plan, id, "english", |item| {
                item.children.contains(&required.id)
                    && before
                        .children
                        .iter()
                        .all(|old| item.children.contains(old))
            })?;
        }
        journal.complete(&step, id)?;
    }
    Ok(())
}

fn wait_item(
    steam: &Steam,
    plan: &Plan,
    id: u64,
    language: &str,
    predicate: impl Fn(&Item) -> bool,
) -> Result<Item> {
    let deadline = Instant::now() + Duration::from_secs(120);
    loop {
        let item = steam.query(id, language)?;
        item.require_identity(id, plan.owner)?;
        if predicate(&item) {
            return Ok(item);
        }
        ensure!(
            Instant::now() < deadline,
            "工坊回读未达到计划内容；保留状态并使用 --resume 回读"
        );
        std::thread::sleep(Duration::from_secs(2));
    }
}
