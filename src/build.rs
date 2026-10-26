use anyhow::{Context, Result};
use std::env;
use std::fs;
use std::io::ErrorKind;
use std::path::{Path, PathBuf};
use std::process::Command;

use crate::config::{Config, SelectedMod};

pub fn run(cfg: &Config, selected: &SelectedMod, release: bool) -> Result<()> {
    let repo_root = match env::var_os("ONI_CLI_REPO_ROOT") {
        Some(path) => PathBuf::from(path),
        None => env::current_dir().context("读取当前目录失败")?,
    };

    let mod_project = selected.config.project_abs(&repo_root);
    let assembly_name = selected.assembly_name(&repo_root);
    crate::config::validate_mod_directory_name(&assembly_name)?;
    let dist = cfg.dist_dir(&repo_root);
    let transaction = PackageBuild::begin(&dist, &assembly_name)?;
    println!("📦 构建 Mod: {} ({})", selected.name, mod_project.display());

    let mut cmd = Command::new("dotnet");
    cmd.args(["build", "--disable-build-servers"])
        .env("DOTNET_CLI_USE_MSBUILD_SERVER", "false")
        .env("MSBUILDUSESERVER", "0")
        .current_dir(&mod_project);

    if release {
        cmd.args(["-c", "Release"]);
        println!("   模式: Release");
    } else {
        println!("   模式: Debug");
    }

    let result = (|| {
        let status = cmd
            .status()
            .context("执行 dotnet build 失败，请确认已安装 .NET SDK")?;
        if !status.success() {
            anyhow::bail!("dotnet build 失败");
        }
        let metadata = fs::symlink_metadata(&transaction.zip).with_context(|| {
            format!(
                "dotnet build 已成功，但没有生成本次构建产物：{}",
                transaction.zip.display()
            )
        })?;
        if !metadata.file_type().is_file() {
            anyhow::bail!(
                "本次构建产物不是普通 ZIP 文件：{}",
                transaction.zip.display()
            );
        }
        Ok(())
    })();
    if let Err(error) = result {
        return Err(transaction.rollback(error));
    }
    transaction.commit();

    // 显示产物
    let zip = dist.join(format!("{}.zip", assembly_name));
    let src = dist.join(format!("{}-src.tar.gz", assembly_name));

    println!("✅ 构建成功！");
    if zip.exists() {
        println!("   📁 {}", zip.display());
    }
    if src.exists() {
        println!("   📁 {}", src.display());
    }

    Ok(())
}

// The directory is both an exclusive per-output lock and crash-recovery storage.
// No Drop cleanup: an interrupted transaction must remain visible for inspection.
struct PackageBuild {
    directory: PathBuf,
    zip: PathBuf,
    previous: PathBuf,
    partial: PathBuf,
    had_previous: bool,
}

impl PackageBuild {
    fn begin(dist: &Path, assembly_name: &str) -> Result<Self> {
        fs::create_dir_all(dist)
            .with_context(|| format!("创建构建产物目录失败：{}", dist.display()))?;
        let directory = dist.join(format!(".{assembly_name}.onim-build"));
        fs::create_dir(&directory).with_context(|| {
            format!(
                "无法独占构建事务目录：{}；已有目录可能属于正在运行或中断的构建，请检查保留内容，不会自动删除",
                directory.display()
            )
        })?;
        let mut transaction = Self {
            zip: dist.join(format!("{assembly_name}.zip")),
            previous: directory.join("previous.zip"),
            partial: directory.join("partial.zip"),
            directory,
            had_previous: false,
        };
        let result = (|| {
            match fs::symlink_metadata(&transaction.zip) {
                Ok(metadata) => {
                    if !metadata.file_type().is_file() {
                        anyhow::bail!(
                            "已有产物不是普通文件，保留原路径：{}",
                            transaction.zip.display()
                        );
                    }
                    fs::rename(&transaction.zip, &transaction.previous).with_context(|| {
                        format!("隔离旧构建产物失败，原路径：{}", transaction.zip.display())
                    })?;
                    transaction.had_previous = true;
                }
                Err(error) if error.kind() == ErrorKind::NotFound => {}
                Err(error) => {
                    return Err(error).with_context(|| {
                        format!(
                            "读取已有构建产物失败，原路径：{}",
                            transaction.zip.display()
                        )
                    });
                }
            }
            Ok(())
        })();
        if let Err(error) = result {
            return match fs::remove_dir(&transaction.directory) {
                Ok(()) => Err(error),
                Err(cleanup) => Err(error.context(format!(
                    "构建尚未开始；事务目录保留在 {}：{}",
                    transaction.directory.display(),
                    cleanup
                ))),
            };
        }
        Ok(transaction)
    }

    fn rollback(self, error: anyhow::Error) -> anyhow::Error {
        let mut recovery = Vec::new();
        let mut output_clear = false;
        match fs::symlink_metadata(&self.zip) {
            Ok(_) => match fs::rename(&self.zip, &self.partial) {
                Ok(()) => {
                    output_clear = true;
                    if let Err(cleanup) = remove_partial_package(&self.partial) {
                        recovery.push(format!(
                            "本次部分产物保留在 {}：{}",
                            self.partial.display(),
                            cleanup
                        ));
                    }
                }
                Err(cleanup) => recovery.push(format!(
                    "无法隔离本次部分产物，保留在 {}：{}",
                    self.zip.display(),
                    cleanup
                )),
            },
            Err(read) if read.kind() == ErrorKind::NotFound => output_clear = true,
            Err(read) => recovery.push(format!(
                "无法检查本次产物路径 {}：{}",
                self.zip.display(),
                read
            )),
        }
        if self.had_previous {
            if output_clear {
                match fs::rename(&self.previous, &self.zip) {
                    Ok(()) => recovery.push(format!("已恢复旧构建产物：{}", self.zip.display())),
                    Err(restore) => recovery.push(format!(
                        "恢复旧包到 {} 失败；旧包保留在 {}：{}",
                        self.zip.display(),
                        self.previous.display(),
                        restore
                    )),
                }
            } else {
                recovery.push(format!("旧构建产物保留在 {}", self.previous.display()));
            }
        }
        if let Err(cleanup) = fs::remove_dir(&self.directory) {
            recovery.push(format!(
                "构建事务保留在 {}：{}",
                self.directory.display(),
                cleanup
            ));
        }
        if recovery.is_empty() {
            error
        } else {
            error.context(recovery.join("\n"))
        }
    }

    fn commit(self) {
        if self.had_previous
            && let Err(error) = fs::remove_file(&self.previous)
        {
            eprintln!(
                "⚠️ 新构建产物已生成，但旧包清理失败，旧包保留在 {}，事务目录保留在 {}：{}",
                self.previous.display(),
                self.directory.display(),
                error
            );
            return;
        }
        if let Err(error) = fs::remove_dir(&self.directory) {
            eprintln!(
                "⚠️ 新构建产物已生成，但事务目录清理失败，保留在 {}：{}",
                self.directory.display(),
                error
            );
        }
    }
}

fn remove_partial_package(path: &Path) -> std::io::Result<()> {
    let metadata = fs::symlink_metadata(path)?;
    if metadata.file_type().is_dir() {
        // Do not recursively delete unexpected builder output or unknown files.
        fs::remove_dir(path)
    } else {
        fs::remove_file(path)
    }
}
