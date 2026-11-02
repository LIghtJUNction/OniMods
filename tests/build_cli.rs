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
if [ "$ONIM_PACKAGE_KIND" != missing ]; then
    /bin/mkdir -p ../../dist
    name=${PWD##*/}
    printf '%s' "$ONIM_PACKAGE_KIND" > "../../dist/$name.zip"
fi
exit "${ONIM_DOTNET_EXIT-0}"
"#,
        )
        .unwrap();
        fs::set_permissions(dotnet, fs::Permissions::from_mode(0o755)).unwrap();
        let unzip = bin.join("unzip");
        fs::write(
            &unzip,
            r#"#!/bin/sh
archive=$2
dest=$4
name=${archive##*/}
name=${name%.zip}
/bin/mkdir -p "$dest"
if [ "$ONIM_PACKAGE_KIND" != no-dll ]; then
    /bin/cp "$archive" "$dest/$name.dll"
fi
if [ "$ONIM_PACKAGE_KIND" != no-metadata ]; then
    printf 'minimumSupportedBuild: 744825\n' > "$dest/mod_info.yaml"
    printf 'staticID: test.%s\n' "$name" > "$dest/mod.yaml"
fi
exit "${ONIM_UNZIP_EXIT-0}"
"#,
        )
        .unwrap();
        fs::set_permissions(unzip, fs::Permissions::from_mode(0o755)).unwrap();
        Self(root)
    }

    fn run(&self, args: &[&str], exit_code: &str) -> Output {
        self.run_package(args, exit_code, "new-package", "0")
    }

    fn run_package(
        &self,
        args: &[&str],
        exit_code: &str,
        package: &str,
        unzip_exit: &str,
    ) -> Output {
        Command::new(env!("CARGO_BIN_EXE_onim"))
            .args(args)
            .current_dir(&self.0)
            .env_remove("ONI_CLI_REPO_ROOT")
            .env("PATH", self.0.join("bin"))
            .env("ONIM_BUILD_CAPTURE", self.0.join("calls.txt"))
            .env("ONIM_DOTNET_EXIT", exit_code)
            .env("ONIM_PACKAGE_KIND", package)
            .env("ONIM_UNZIP_EXIT", unzip_exit)
            .env("HOME", self.0.join("home"))
            .env("MSBUILDUSESERVER", "1")
            .env("DOTNET_CLI_USE_MSBUILD_SERVER", "true")
            .env("XDG_CONFIG_HOME", self.0.join("user-config"))
            .output()
            .unwrap()
    }

    fn mods(&self) -> PathBuf {
        self.0
            .join("home/.config/unity3d/Klei/Oxygen Not Included/mods")
    }

    fn installed(&self, scope: &str) -> PathBuf {
        let path = self.mods().join(scope).join("Alpha");
        fs::create_dir_all(&path).unwrap();
        fs::write(path.join("Alpha.dll"), "old-package").unwrap();
        path
    }

    fn stale_package(&self) -> PathBuf {
        fs::create_dir_all(self.0.join("dist")).unwrap();
        let zip = self.0.join("dist/Alpha.zip");
        fs::write(&zip, "stale-package").unwrap();
        zip
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

#[test]
fn install_refuses_a_dev_shadow_before_building_or_overwriting_local() {
    let project = Project::new();
    let shadow = project
        .0
        .join("home/.config/unity3d/Klei/Oxygen Not Included/mods/Dev/Alpha");
    fs::create_dir_all(&shadow).unwrap();
    fs::write(shadow.join("OniOnline.dll"), "old-dev-copy").unwrap();

    let output = project.run(&["install"], "0");
    assert!(!output.status.success(), "{output:?}");
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert!(stderr.contains("同名 Dev 模组"), "{stderr}");
    assert!(!project.0.join("calls.txt").exists());
    assert!(
        !project
            .0
            .join("home/.config/unity3d/Klei/Oxygen Not Included/mods/Local/Alpha")
            .exists()
    );
    assert_eq!(
        fs::read_to_string(shadow.join("OniOnline.dll")).unwrap(),
        "old-dev-copy"
    );
}

#[test]
fn successful_build_without_a_new_package_cannot_install_a_stale_archive() {
    for (command, scope) in [("install", "Local"), ("dev", "Dev")] {
        let project = Project::new();
        let installed = project.installed(scope);
        let zip = project.stale_package();
        let output = project.run_package(&[command], "0", "missing", "0");
        assert!(!output.status.success(), "{command}: {output:?}");
        assert_eq!(
            fs::read_to_string(installed.join("Alpha.dll")).unwrap(),
            "old-package"
        );
        assert_eq!(fs::read_to_string(zip).unwrap(), "stale-package");
    }
}

#[test]
fn failed_build_restores_the_previous_package_instead_of_leaving_partial_output() {
    let project = Project::new();
    let zip = project.stale_package();
    let output = project.run_package(&["build"], "23", "partial-package", "0");
    assert!(!output.status.success(), "{output:?}");
    assert_eq!(fs::read_to_string(zip).unwrap(), "stale-package");
    assert!(!project.0.join("dist/.Alpha.onim-build").exists());
}

#[test]
fn extraction_failure_preserves_local_dev_and_legacy_payloads() {
    for (command, scope) in [("install", "Local"), ("dev", "Dev")] {
        let project = Project::new();
        let installed = project.installed(scope);
        let legacy = (command == "dev").then(|| project.installed("dev"));
        let output = project.run_package(&[command], "0", "new-package", "9");
        assert!(!output.status.success(), "{command}: {output:?}");
        assert_eq!(
            fs::read_to_string(installed.join("Alpha.dll")).unwrap(),
            "old-package"
        );
        if let Some(legacy) = legacy {
            assert_eq!(
                fs::read_to_string(legacy.join("Alpha.dll")).unwrap(),
                "old-package"
            );
        }
        assert!(
            !project
                .mods()
                .join(scope)
                .join(".Alpha.onim-install")
                .exists()
        );
    }
}

#[test]
fn incomplete_package_cannot_replace_a_working_installation() {
    for missing in ["no-metadata", "no-dll"] {
        let project = Project::new();
        let installed = project.installed("Local");
        let output = project.run_package(&["install"], "0", missing, "0");
        assert!(!output.status.success(), "{missing}: {output:?}");
        assert_eq!(
            fs::read_to_string(installed.join("Alpha.dll")).unwrap(),
            "old-package"
        );
    }
}

#[test]
fn failed_dev_enablement_rolls_back_payload_and_preserves_configuration() {
    let project = Project::new();
    let installed = project.installed("Dev");
    let legacy = project.installed("dev");
    let config = project.mods().join("mods.json");
    fs::write(&config, "invalid-config-retained").unwrap();
    let output = project.run(&["dev"], "0");
    assert!(!output.status.success(), "{output:?}");
    assert_eq!(
        fs::read_to_string(installed.join("Alpha.dll")).unwrap(),
        "old-package"
    );
    assert_eq!(
        fs::read_to_string(legacy.join("Alpha.dll")).unwrap(),
        "old-package"
    );
    assert_eq!(
        fs::read_to_string(config).unwrap(),
        "invalid-config-retained"
    );
}

#[test]
fn successful_replacement_uses_the_new_package_and_removes_obsolete_files() {
    for (command, scope) in [("install", "Local"), ("dev", "Dev")] {
        let project = Project::new();
        let installed = project.installed(scope);
        fs::write(installed.join("obsolete.dll"), "obsolete").unwrap();
        let legacy = (command == "dev").then(|| project.installed("dev"));
        let output = project.run(&[command], "0");
        assert!(output.status.success(), "{command}: {output:?}");
        assert_eq!(
            fs::read_to_string(installed.join("Alpha.dll")).unwrap(),
            "new-package"
        );
        assert!(!installed.join("obsolete.dll").exists());
        assert!(
            !project
                .mods()
                .join(scope)
                .join(".Alpha.onim-install")
                .exists()
        );
        if let Some(legacy) = legacy {
            assert!(!legacy.exists());
        }
    }
}

#[test]
fn an_interrupted_installation_is_retained_for_recovery() {
    let project = Project::new();
    let installed = project.installed("Local");
    let transaction = project.mods().join("Local/.Alpha.onim-install");
    fs::create_dir(&transaction).unwrap();
    fs::write(transaction.join("recovery.txt"), "keep-me").unwrap();
    let output = project.run(&["install"], "0");
    assert!(!output.status.success(), "{output:?}");
    assert_eq!(
        fs::read_to_string(installed.join("Alpha.dll")).unwrap(),
        "old-package"
    );
    assert_eq!(
        fs::read_to_string(transaction.join("recovery.txt")).unwrap(),
        "keep-me"
    );
}

#[test]
fn an_existing_build_transaction_is_not_overwritten_or_removed() {
    let project = Project::new();
    let zip = project.stale_package();
    let transaction = project.0.join("dist/.Alpha.onim-build");
    fs::create_dir(&transaction).unwrap();
    fs::write(transaction.join("previous.zip"), "recovery-package").unwrap();
    let output = project.run(&["build"], "0");
    assert!(!output.status.success(), "{output:?}");
    assert!(!project.0.join("calls.txt").exists());
    assert_eq!(fs::read_to_string(zip).unwrap(), "stale-package");
    assert_eq!(
        fs::read_to_string(transaction.join("previous.zip")).unwrap(),
        "recovery-package"
    );
}

#[test]
fn failed_first_installation_leaves_no_partial_payload() {
    for (command, scope) in [("install", "Local"), ("dev", "Dev")] {
        let project = Project::new();
        let output = project.run_package(&[command], "0", "new-package", "9");
        assert!(!output.status.success(), "{command}: {output:?}");
        assert!(!project.mods().join(scope).join("Alpha").exists());
        assert!(!project.mods().join("mods.json").exists());
    }
}

#[test]
fn a_legacy_directory_alias_cannot_delete_the_new_dev_installation() {
    let project = Project::new();
    let installed = project.installed("Dev");
    std::os::unix::fs::symlink(project.mods().join("Dev"), project.mods().join("dev")).unwrap();
    let output = project.run(&["dev"], "0");
    assert!(output.status.success(), "{output:?}");
    assert_eq!(
        fs::read_to_string(installed.join("Alpha.dll")).unwrap(),
        "new-package"
    );
}
