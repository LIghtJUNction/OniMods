#![cfg(unix)]

use std::fs;
use std::os::unix::fs::PermissionsExt;
use std::path::PathBuf;
use std::process::{Command, Output};
use std::sync::atomic::{AtomicU64, Ordering};

static NEXT_PROJECT: AtomicU64 = AtomicU64::new(0);

struct Project(PathBuf);

impl Project {
    fn new() -> Self {
        let id = NEXT_PROJECT.fetch_add(1, Ordering::Relaxed);
        let root = std::env::temp_dir().join(format!("onim-build-cli-{}-{id}", std::process::id()));
        fs::create_dir(&root).unwrap();
        let game = root.join("game/OxygenNotIncluded_Data/Managed");
        fs::create_dir_all(&game).unwrap();
        fs::write(game.join("Assembly-CSharp.dll"), []).unwrap();
        fs::write(
            root.join("Directory.Build.props"),
            format!(
                "<Project><PropertyGroup><OniGamePath>{}</OniGamePath></PropertyGroup></Project>",
                root.join("game").display()
            ),
        )
        .unwrap();
        fs::write(
            root.join("onim.toml"),
            "default_mod = 'Alpha'\n[mods.Alpha]\npath = 'mods/Alpha'\n[mods.Beta]\npath = 'mods/Beta'\n",
        )
        .unwrap();
        for name in ["Alpha", "Beta"] {
            fs::create_dir_all(root.join("mods").join(name)).unwrap();
        }
        let bin = root.join("bin");
        fs::create_dir(&bin).unwrap();
        let dotnet = bin.join("dotnet");
        fs::write(
            &dotnet,
            r#"#!/bin/sh
{
    printf 'project=%s\n' "$PWD"
    printf 'arg=%s\n' "$@"
    printf 'server=%s\n' "${MSBUILDUSESERVER-unset}"
    printf 'cli-server=%s\n' "${DOTNET_CLI_USE_MSBUILD_SERVER-unset}"
} >> "$ONIM_BUILD_CAPTURE"
standalone=false
for arg in "$@"; do
    if [ "$arg" = '--disable-build-servers' ]; then standalone=true; fi
done
if [ "$standalone" != true ] || [ "$MSBUILDUSESERVER" != 0 ] || [ "$DOTNET_CLI_USE_MSBUILD_SERVER" != false ]; then
    printf 'fixture: stale MSBuild server would be contacted\n' >&2
    exit 97
fi
exit "${ONIM_DOTNET_EXIT-0}"
"#,
        )
        .unwrap();
        fs::set_permissions(dotnet, fs::Permissions::from_mode(0o755)).unwrap();
        Self(root)
    }

    fn run(&self, args: &[&str], exit_code: &str) -> Output {
        Command::new(env!("CARGO_BIN_EXE_onim"))
            .args(args)
            .current_dir(&self.0)
            .env_remove("ONI_CLI_REPO_ROOT")
            .env("PATH", self.0.join("bin"))
            .env("ONIM_BUILD_CAPTURE", self.0.join("calls.txt"))
            .env("ONIM_DOTNET_EXIT", exit_code)
            .env("MSBUILDUSESERVER", "1")
            .env("DOTNET_CLI_USE_MSBUILD_SERVER", "true")
            .env("XDG_CONFIG_HOME", self.0.join("user-config"))
            .output()
            .unwrap()
    }

    fn calls(&self) -> Vec<String> {
        fs::read_to_string(self.0.join("calls.txt"))
            .unwrap()
            .split("project=")
            .skip(1)
            .map(str::to_owned)
            .collect()
    }
}

impl Drop for Project {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

#[test]
fn debug_build_ignores_an_inherited_msbuild_server() {
    let project = Project::new();
    let output = project.run(&["build"], "0");
    assert!(output.status.success(), "{output:?}");
    let calls = project.calls();
    assert_eq!(calls.len(), 1);
    assert!(calls[0].starts_with(&project.0.join("mods/Alpha").display().to_string()));
    assert!(calls[0].contains("arg=build\n"));
    assert!(!calls[0].contains("arg=Release\n"));
}

#[test]
fn release_build_all_isolates_each_project_from_build_servers() {
    let project = Project::new();
    let output = project.run(&["build", "--release", "--all"], "0");
    assert!(output.status.success(), "{output:?}");
    let calls = project.calls();
    assert_eq!(calls.len(), 2);
    for (call, name) in calls.iter().zip(["Alpha", "Beta"]) {
        assert!(call.starts_with(&project.0.join("mods").join(name).display().to_string()));
        assert!(call.contains("arg=-c\narg=Release\n"));
    }
}

#[test]
fn dev_all_stops_on_a_real_build_failure_without_installing() {
    let project = Project::new();
    let output = project.run(&["dev", "--all"], "23");
    assert!(!output.status.success());
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert!(stderr.contains("dotnet build 失败"), "{stderr}");
    assert!(!stderr.contains("stale MSBuild server"), "{stderr}");
    assert_eq!(project.calls().len(), 1);
    assert!(!project.0.join("user-config").exists());
}
