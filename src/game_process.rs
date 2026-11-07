use anyhow::{Context, Result, bail};
#[cfg(target_os = "linux")]
use std::path::Path;
#[cfg(any(target_os = "macos", target_os = "windows"))]
use std::process::Command;

const GAME_EXECUTABLES: &[&str] = &[
    "OxygenNotIncluded",
    "OxygenNotIncluded.x86_64",
    "OxygenNotIncluded.exe",
];

/// Stop mod-list edits unless the game is confirmed closed.
///
/// # Errors
/// Returns an error when ONI is running, or when the process list cannot be read.
pub fn ensure_stopped() -> Result<()> {
    if detect().context("无法确认 ONI 是否正在运行；已停止修改 Mod 清单")? {
        bail!("ONI 正在运行；退出游戏后再安装或更改 Mod 清单");
    }
    Ok(())
}

/// After a dev install, an unreadable process list is treated as running so the
/// operator restarts the game instead of loading a stale assembly.
#[allow(dead_code)]
pub fn running_or_unknown() -> bool {
    detect().unwrap_or(true)
}

fn detect() -> Result<bool> {
    #[cfg(target_os = "linux")]
    {
        detect_linux()
    }
    #[cfg(target_os = "macos")]
    {
        detect_pgrep()
    }
    #[cfg(target_os = "windows")]
    {
        detect_windows()
    }
    #[cfg(not(any(target_os = "linux", target_os = "macos", target_os = "windows")))]
    {
        bail!("当前系统没有 ONI 进程检查")
    }
}

#[cfg(target_os = "linux")]
fn detect_linux() -> Result<bool> {
    let processes = std::fs::read_dir("/proc").context("无法读取 /proc")?;
    for entry in processes {
        let entry = entry.context("读取 /proc 条目失败")?;
        if !entry
            .file_name()
            .to_string_lossy()
            .bytes()
            .all(|byte| byte.is_ascii_digit())
        {
            continue;
        }
        if process_is_game(&entry.path()) {
            return Ok(true);
        }
    }
    Ok(false)
}

#[cfg(target_os = "linux")]
fn process_is_game(pid: &Path) -> bool {
    if std::fs::read_link(pid.join("exe"))
        .ok()
        .as_ref()
        .and_then(|path| path.file_name())
        .and_then(|name| name.to_str())
        .is_some_and(is_game_executable)
    {
        return true;
    }
    let Ok(cmdline) = std::fs::read(pid.join("cmdline")) else {
        return false;
    };
    cmdline.split(|byte| *byte == 0).any(|argument| {
        let text = String::from_utf8_lossy(argument);
        text.rsplit(['/', '\\'])
            .next()
            .is_some_and(is_game_executable)
    })
}

#[cfg(target_os = "macos")]
fn detect_pgrep() -> Result<bool> {
    for name in GAME_EXECUTABLES {
        let output = Command::new("pgrep")
            .args(["-x", name])
            .output()
            .with_context(|| format!("无法运行 pgrep 来检查 {name}"))?;
        if output.status.success() {
            return Ok(true);
        }
        if output.status.code() != Some(1) {
            bail!("pgrep 无法确认 {name} 是否正在运行");
        }
    }
    Ok(false)
}

#[cfg(target_os = "windows")]
fn detect_windows() -> Result<bool> {
    let output = Command::new("tasklist")
        .args(["/FI", "IMAGENAME eq OxygenNotIncluded.exe"])
        .output()
        .context("无法运行 tasklist")?;
    if !output.status.success() {
        bail!("tasklist 无法确认 ONI 是否正在运行");
    }
    Ok(String::from_utf8_lossy(&output.stdout).contains("OxygenNotIncluded.exe"))
}

fn is_game_executable(name: &str) -> bool {
    GAME_EXECUTABLES.contains(&name)
}
