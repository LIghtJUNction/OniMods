//! Durable write intents survive lost callbacks and process termination.

use anyhow::{Context, Result, ensure};
use serde::{Deserialize, Serialize};
use std::fs::{self, File, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};

use super::package::{Plan, hash, state_root};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub(super) enum State {
    Intent,
    Complete,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub(super) struct Event {
    pub plan: String,
    pub step: String,
    pub state: State,
    pub item_id: u64,
}

pub(super) struct Journal {
    path: PathBuf,
    plan: String,
}

impl Journal {
    pub(super) fn open(path: &Path, plan: &Plan) -> Result<Self> {
        Ok(Self {
            path: path
                .parent()
                .context("计划缺少父目录")?
                .join("journal.jsonl"),
            plan: plan.sha256.clone(),
        })
    }

    pub(super) fn last(&self, step: &str) -> Result<Option<Event>> {
        let contents = match fs::read_to_string(&self.path) {
            Ok(contents) => contents,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
            Err(error) => return Err(error.into()),
        };
        let mut last = None;
        for line in contents.lines() {
            let event: Event =
                serde_json::from_str(line).context("发布日志不完整，保留现场，拒绝再次写入")?;
            ensure!(event.plan == self.plan, "发布日志属于另一计划");
            if event.step == step {
                last = Some(event);
            }
        }
        Ok(last)
    }

    pub(super) fn begin(&self, step: &str, item_id: u64) -> Result<()> {
        ensure!(
            self.last(step)?.is_none(),
            "{step} 已有写入记录；先使用 --resume 回读，不能重复写入"
        );
        self.append(step, State::Intent, item_id)
    }

    pub(super) fn complete(&self, step: &str, item_id: u64) -> Result<()> {
        ensure!(
            item_id != 0 && item_id != u64::MAX,
            "写入结果缺少有效条目 ID"
        );
        if let Some(event) = self.last(step)?
            && event.state == State::Complete
        {
            ensure!(event.item_id == item_id, "条目 ID 与之前的完成记录不同");
            return Ok(());
        }
        self.append(step, State::Complete, item_id)
    }

    fn append(&self, step: &str, state: State, item_id: u64) -> Result<()> {
        let mut file = OpenOptions::new()
            .create(true)
            .append(true)
            .open(&self.path)?;
        let mut bytes = serde_json::to_vec(&Event {
            plan: self.plan.clone(),
            step: step.into(),
            state,
            item_id,
        })?;
        bytes.push(b'\n');
        file.write_all(&bytes)?;
        file.sync_all()?;
        Ok(())
    }

    pub(super) fn reserve_creation(&self, plan: &Plan) -> Result<()> {
        let directory = state_root()?.join("creations");
        fs::create_dir_all(&directory)?;
        let path = directory.join(format!(
            "{}.json",
            hash(plan.static_id.to_lowercase().as_bytes())
        ));
        let mut file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&path)
            .with_context(|| {
                format!(
                    "此前已经尝试创建 {}；检查 {}，禁止再次创建",
                    plan.name,
                    path.display()
                )
            })?;
        file.write_all(&serde_json::to_vec(&Event {
            plan: plan.sha256.clone(),
            step: "create".into(),
            state: State::Intent,
            item_id: 0,
        })?)?;
        file.sync_all()?;
        Ok(())
    }
}

pub(super) fn lock(plan: &Plan) -> Result<File> {
    let root = state_root()?.join("locks");
    fs::create_dir_all(&root)?;
    let key = if plan.target_id == 0 {
        hash(plan.static_id.to_lowercase().as_bytes())
    } else {
        plan.target_id.to_string()
    };
    let file = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(root.join(key))?;
    file.try_lock()
        .context("相同条目已有发布进程，未发送上传请求")?;
    Ok(file)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_uncertain_write_survives_reopening_and_cannot_be_repeated() {
        let path = std::env::temp_dir().join(format!(
            "onim-journal-{}-{}.jsonl",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        let journal = Journal {
            path: path.clone(),
            plan: "fixture-plan".into(),
        };
        journal.begin("upload", 0).unwrap();
        let reopened = Journal {
            path: path.clone(),
            plan: "fixture-plan".into(),
        };
        assert!(reopened.begin("upload", 0).is_err());
        assert_eq!(
            reopened.last("upload").unwrap().unwrap().state,
            State::Intent
        );
        reopened.complete("upload", 42).unwrap();
        reopened.complete("upload", 42).unwrap();
        assert!(reopened.complete("upload", 99).is_err());
        assert_eq!(fs::read_to_string(&path).unwrap().lines().count(), 2);
        fs::remove_file(path).unwrap();
    }

    #[test]
    fn truncated_intents_are_not_silently_discarded() {
        let path = std::env::temp_dir().join(format!(
            "onim-truncated-journal-{}.jsonl",
            std::process::id()
        ));
        fs::write(&path, "{\"step\":\"upload\"").unwrap();
        let journal = Journal {
            path: path.clone(),
            plan: "fixture".into(),
        };
        assert!(journal.last("upload").is_err());
        fs::remove_file(path).unwrap();
    }
}
