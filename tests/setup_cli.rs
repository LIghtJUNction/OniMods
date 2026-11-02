use std::fs;
use std::io::Write;
use std::path::PathBuf;
use std::process::{Command, Output, Stdio};
use std::sync::atomic::{AtomicU64, Ordering};

static NEXT_PROJECT: AtomicU64 = AtomicU64::new(0);

struct Project(PathBuf);

impl Project {
    fn new() -> Self {
        let id = NEXT_PROJECT.fetch_add(1, Ordering::Relaxed);
        let root = std::env::temp_dir().join(format!("onim-setup-cli-{}-{id}", std::process::id()));
        fs::create_dir(&root).expect("create isolated project");
        Self(root)
    }

    fn setup(&self, path: &std::ffi::OsStr, input: Option<&str>) -> Output {
        let mut command = Command::new(env!("CARGO_BIN_EXE_onim"));
        command
            .arg("setup")
            .current_dir(&self.0)
            .env("PATH", path)
            .env("ONIM_STEAM_ROOT", self.0.join("steam"))
            .stdin(if input.is_some() {
                Stdio::piped()
            } else {
                Stdio::null()
            })
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        let mut child = command.spawn().expect("start setup CLI");
        if let Some(input) = input {
            child
                .stdin
                .take()
                .unwrap()
                .write_all(input.as_bytes())
                .unwrap();
        }
        child.wait_with_output().expect("finish setup CLI")
    }
}

impl Drop for Project {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

const CUSTOM_CONFIG: &str = "# Player configuration\ndefault_mod = 'OniOnline'\n\n[mods.OniOnline]\npath = 'mods/OniOnline'\n\n[mods.CycleTrim]\npath = 'mods/CycleTrim'\n";
const CUSTOM_PROPS: &str = "<Project>\n  <PropertyGroup><CustomProperty>keep</CustomProperty></PropertyGroup>\n  <Target Name='CustomTarget' />\n</Project>\n";

#[test]
fn repeated_setup_preserves_existing_project_without_prompts_or_tools() {
    let project = Project::new();
    fs::write(project.0.join("onim.toml"), CUSTOM_CONFIG).unwrap();
    fs::write(project.0.join("Directory.Build.props"), CUSTOM_PROPS).unwrap();

    for _ in 0..2 {
        let output = project.setup(std::ffi::OsStr::new(""), None);
        assert!(output.status.success(), "{output:?}");
        let stdout = String::from_utf8_lossy(&output.stdout);
        assert!(!stdout.contains("路径是否正确"), "{stdout}");
        assert!(!stdout.contains("请输入缺氧"), "{stdout}");
        assert_eq!(
            fs::read_to_string(project.0.join("onim.toml")).unwrap(),
            CUSTOM_CONFIG
        );
        assert_eq!(
            fs::read_to_string(project.0.join("Directory.Build.props")).unwrap(),
            CUSTOM_PROPS
        );
    }
}

#[test]
fn existing_props_only_gets_a_missing_mod_configuration() {
    let project = Project::new();
    fs::write(project.0.join("Directory.Build.props"), CUSTOM_PROPS).unwrap();
    let output = project.setup(std::ffi::OsStr::new(""), None);
    assert!(output.status.success(), "{output:?}");
    assert_eq!(
        fs::read_to_string(project.0.join("Directory.Build.props")).unwrap(),
        CUSTOM_PROPS
    );
    let config: toml::Value =
        toml::from_str(&fs::read_to_string(project.0.join("onim.toml")).unwrap()).unwrap();
    assert!(config["mods"]["OniModTemplate"]["path"].is_str());
}

#[test]
fn setup_rejects_a_directory_without_modifying_either_path() {
    let project = Project::new();
    fs::create_dir(project.0.join("Directory.Build.props")).unwrap();
    fs::write(project.0.join("Directory.Build.props/sentinel"), "keep").unwrap();
    let output = project.setup(std::ffi::OsStr::new(""), None);
    assert!(!output.status.success());
    assert!(!project.0.join("onim.toml").exists());
    assert_eq!(
        fs::read_to_string(project.0.join("Directory.Build.props/sentinel")).unwrap(),
        "keep"
    );
}

#[cfg(unix)]
#[test]
fn setup_creates_only_missing_files_with_a_valid_game_fixture() {
    use std::os::unix::fs::{PermissionsExt, symlink};

    for existing_config in [false, true] {
        let project = Project::new();
        let game = project.0.join("steam/steamapps/common/OxygenNotIncluded");
        let managed = if cfg!(target_os = "macos") {
            game.join("OxygenNotIncluded.app/Contents/Resources/Data/Managed")
        } else {
            game.join("OxygenNotIncluded_Data/Managed")
        };
        fs::create_dir_all(&managed).unwrap();
        for name in [
            "Assembly-CSharp.dll",
            "0Harmony.dll",
            "UnityEngine.CoreModule.dll",
        ] {
            fs::write(managed.join(name), []).unwrap();
        }
        let config_path = project.0.join("onim.toml");
        if existing_config {
            fs::write(&config_path, CUSTOM_CONFIG).unwrap();
        }

        let bin = project.0.join("bin");
        fs::create_dir(&bin).unwrap();
        symlink("/bin/sh", bin.join("sh")).unwrap();
        // Setup only probes these command names; no game or SDK code is executed.
        for name in ["dotnet", "tar", "unzip"] {
            let stub = bin.join(name);
            fs::write(&stub, "#!/bin/sh\nexit 0\n").unwrap();
            fs::set_permissions(&stub, fs::Permissions::from_mode(0o755)).unwrap();
        }
        let output = project.setup(bin.as_os_str(), Some("Y\n"));
        assert!(output.status.success(), "{output:?}");
        if existing_config {
            assert_eq!(fs::read_to_string(config_path).unwrap(), CUSTOM_CONFIG);
        }
        let props = fs::read_to_string(project.0.join("Directory.Build.props")).unwrap();
        assert!(
            props.contains(&game.to_string_lossy().to_string()),
            "{props}"
        );
        let output = project.setup(std::ffi::OsStr::new(""), None);
        assert!(output.status.success(), "{output:?}");
        assert_eq!(
            fs::read_to_string(project.0.join("Directory.Build.props")).unwrap(),
            props
        );
    }
}

#[cfg(unix)]
#[test]
fn setup_preserves_existing_symlinks_and_their_targets() {
    use std::os::unix::fs::symlink;

    let project = Project::new();
    fs::write(project.0.join("custom.toml"), CUSTOM_CONFIG).unwrap();
    fs::write(project.0.join("custom.props"), CUSTOM_PROPS).unwrap();
    symlink("custom.toml", project.0.join("onim.toml")).unwrap();
    symlink("custom.props", project.0.join("Directory.Build.props")).unwrap();
    let output = project.setup(std::ffi::OsStr::new(""), None);
    assert!(output.status.success(), "{output:?}");
    assert_eq!(
        fs::read_link(project.0.join("onim.toml")).unwrap(),
        PathBuf::from("custom.toml")
    );
    assert_eq!(
        fs::read_to_string(project.0.join("custom.toml")).unwrap(),
        CUSTOM_CONFIG
    );
    assert_eq!(
        fs::read_to_string(project.0.join("custom.props")).unwrap(),
        CUSTOM_PROPS
    );
}
