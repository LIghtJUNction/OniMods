use anyhow::{Context, Result, bail};
use serde_json::Value;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

const KEEP_BACKUPS: usize = 5;

pub struct Update {
    pub backup: Option<PathBuf>,
    pub pruned: Option<usize>,
    pub cleanup_error: Option<String>,
}

pub fn write_config(path: &Path, previous: &str, updated: &str) -> Result<Update> {
    let old: Value = serde_json::from_str(previous)?;
    let new: Value = serde_json::from_str(updated)?;
    if fs::read_to_string(path)? != previous {
        bail!("mods.json changed during installation; retry after the other writer finishes");
    }
    if old == new {
        return Ok(finish_update(path, None));
    }

    let filename = path
        .file_name()
        .context("configuration has no file name")?
        .to_string_lossy();
    let nonce = SystemTime::now().duration_since(UNIX_EPOCH)?.as_nanos();
    let backup = path.with_file_name(format!("{filename}.onim-backup-{nonce}"));
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&backup)?;
    output.write_all(previous.as_bytes())?;
    output.sync_all()?;
    fs::set_permissions(&backup, fs::metadata(path)?.permissions())?;

    let pending = path.with_file_name(format!(".{filename}.onim-write-{nonce}"));
    let result = (|| -> Result<()> {
        let mut output = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&pending)?;
        output.write_all(updated.as_bytes())?;
        output.sync_all()?;
        fs::set_permissions(&pending, fs::metadata(path)?.permissions())?;
        if fs::read_to_string(path)? != previous {
            bail!("mods.json changed during installation; retained recovery backup");
        }
        fs::rename(&pending, path).context("replace mods.json atomically")?;
        Ok(())
    })();
    if result.is_err() {
        let _ = fs::remove_file(&pending);
    }
    result?;
    Ok(finish_update(path, Some(backup)))
}

fn finish_update(path: &Path, backup: Option<PathBuf>) -> Update {
    match prune(path) {
        Ok(count) => Update {
            backup,
            pruned: Some(count),
            cleanup_error: None,
        },
        Err(error) => Update {
            backup,
            pruned: None,
            cleanup_error: Some(error.to_string()),
        },
    }
}

fn prune(path: &Path) -> Result<usize> {
    let parent = path.parent().context("configuration has no parent")?;
    let prefix = format!(
        "{}.onim-backup-",
        path.file_name().unwrap().to_string_lossy()
    );
    let mut backups = Vec::new();
    for item in fs::read_dir(parent)? {
        let item = item?;
        if !item.file_type()?.is_file() {
            continue;
        }
        let filename = item.file_name();
        let Some(suffix) = filename
            .to_str()
            .and_then(|name| name.strip_prefix(&prefix))
        else {
            continue;
        };
        if suffix.is_empty() || !suffix.bytes().all(|byte| byte.is_ascii_digit()) {
            continue;
        }
        // Unknown or damaged files are not treated as disposable configuration snapshots.
        let Ok(contents) = fs::read(item.path()) else {
            continue;
        };
        let Ok(value) = serde_json::from_slice::<Value>(&contents) else {
            continue;
        };
        if !value.get("mods").is_some_and(Value::is_array) {
            continue;
        }
        backups.push((item.metadata()?.modified()?, item.path()));
    }
    backups.sort();
    let count = backups.len().saturating_sub(KEEP_BACKUPS);
    for (_, backup) in backups.into_iter().take(count) {
        fs::remove_file(&backup)
            .with_context(|| format!("remove expired onim backup: {}", backup.display()))?;
    }
    Ok(count)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicU64, Ordering};

    static COUNTER: AtomicU64 = AtomicU64::new(0);
    struct Fixture(PathBuf);
    impl Fixture {
        fn new() -> Self {
            let path = std::env::temp_dir().join(format!(
                "onim-backups-{}-{}-{}",
                std::process::id(),
                SystemTime::now()
                    .duration_since(UNIX_EPOCH)
                    .unwrap()
                    .as_nanos(),
                COUNTER.fetch_add(1, Ordering::Relaxed)
            ));
            fs::create_dir(&path).unwrap();
            Self(path)
        }
        fn config(&self) -> PathBuf {
            self.0.join("mods.json")
        }
    }
    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }
    fn config(revision: usize) -> String {
        format!(r#"{{"mods":[],"revision":{revision}}}"#)
    }

    #[test]
    fn unchanged_json_does_not_rewrite_or_back_up() {
        let fixture = Fixture::new();
        let original = "{\n  \"mods\": []\n}\n";
        fs::write(fixture.config(), original).unwrap();
        let result = write_config(&fixture.config(), original, r#"{"mods":[]}"#).unwrap();
        assert!(result.backup.is_none());
        assert_eq!(fs::read_to_string(fixture.config()).unwrap(), original);
        assert_eq!(fs::read_dir(&fixture.0).unwrap().count(), 1);
    }

    #[test]
    fn repeated_changes_keep_only_five_recent_recovery_versions() {
        let fixture = Fixture::new();
        fs::write(fixture.config(), config(0)).unwrap();
        for revision in 1..=12 {
            let result =
                write_config(&fixture.config(), &config(revision - 1), &config(revision)).unwrap();
            assert!(result.backup.unwrap().exists());
        }
        let mut revisions = Vec::new();
        for item in fs::read_dir(&fixture.0).unwrap() {
            let item = item.unwrap();
            if item.path() == fixture.config() {
                continue;
            }
            let value: Value = serde_json::from_slice(&fs::read(item.path()).unwrap()).unwrap();
            revisions.push(value["revision"].as_u64().unwrap());
        }
        revisions.sort();
        assert_eq!(revisions, vec![7, 8, 9, 10, 11]);
        assert_eq!(fs::read_to_string(fixture.config()).unwrap(), config(12));
    }

    #[test]
    fn quick_updates_keep_separate_exact_backups() {
        let fixture = Fixture::new();
        fs::write(fixture.config(), config(0)).unwrap();
        let first = write_config(&fixture.config(), &config(0), &config(1))
            .unwrap()
            .backup
            .unwrap();
        let second = write_config(&fixture.config(), &config(1), &config(2))
            .unwrap()
            .backup
            .unwrap();
        assert_ne!(first, second);
        assert_eq!(fs::read_to_string(first).unwrap(), config(0));
        assert_eq!(fs::read_to_string(second).unwrap(), config(1));
    }

    #[test]
    fn unknown_files_and_concurrent_config_changes_are_preserved() {
        let fixture = Fixture::new();
        fs::write(fixture.config(), config(2)).unwrap();
        let unknown = fixture.0.join("mods.json.onim-backup-manual");
        let invalid = fixture.0.join("mods.json.onim-backup-123");
        fs::write(&unknown, "manual recovery note").unwrap();
        fs::write(&invalid, "damaged JSON").unwrap();
        assert!(write_config(&fixture.config(), &config(0), &config(1)).is_err());
        write_config(&fixture.config(), &config(2), &config(2)).unwrap();
        assert_eq!(fs::read_to_string(fixture.config()).unwrap(), config(2));
        assert_eq!(fs::read_to_string(unknown).unwrap(), "manual recovery note");
        assert_eq!(fs::read_to_string(invalid).unwrap(), "damaged JSON");
    }
}
