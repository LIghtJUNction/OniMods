use super::*;
use std::cell::Cell;
use std::io::Write;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use zip::write::SimpleFileOptions;

static NEXT_FIXTURE: AtomicU64 = AtomicU64::new(0);

struct Fixture(PathBuf);

impl Fixture {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!(
            "onim-archive-{}-{}",
            std::process::id(),
            NEXT_FIXTURE.fetch_add(1, Ordering::Relaxed)
        ));
        fs::create_dir(&path).unwrap();
        Self(path)
    }

    fn zip(&self, files: &[(&str, &[u8])]) -> PathBuf {
        let path = self.0.join("package.zip");
        let mut zip = zip::ZipWriter::new(fs::File::create(&path).unwrap());
        let options =
            SimpleFileOptions::default().compression_method(zip::CompressionMethod::Stored);
        for (name, bytes) in files {
            if name.ends_with('/') {
                zip.add_directory(*name, options.unix_permissions(0o750))
                    .unwrap();
            } else {
                zip.start_file(*name, options.unix_permissions(0o644))
                    .unwrap();
                zip.write_all(bytes).unwrap();
            }
        }
        zip.finish().unwrap();
        path
    }

    fn source(&self) -> PathBuf {
        let path = self.0.join("source");
        fs::create_dir(&path).unwrap();
        fs::create_dir(path.join("empty")).unwrap();
        fs::create_dir(path.join("目录")).unwrap();
        fs::write(path.join("Alpha.dll"), "new").unwrap();
        fs::write(
            path.join("mod_info.yaml"),
            "minimumSupportedBuild: 744825\n",
        )
        .unwrap();
        fs::write(path.join("mod.yaml"), "staticID: test.Alpha\n").unwrap();
        fs::write(path.join("目录/说明.txt"), "unicode content").unwrap();
        path
    }

    fn installed(&self) -> PathBuf {
        let path = self.0.join("Local/Alpha");
        fs::create_dir_all(&path).unwrap();
        fs::write(path.join("Alpha.dll"), "old").unwrap();
        fs::write(path.join("obsolete"), "old-only").unwrap();
        path
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

#[test]
fn zip_metadata_and_installation_preserve_unicode_and_empty_directories() {
    let f = Fixture::new();
    let zip = f.zip(&[
        ("./Alpha.dll", b"new"),
        ("./mod_info.yaml", b"minimumSupportedBuild: 744825\n"),
        ("./mod.yaml", b"staticID: 'test.Alpha'\n"),
        ("empty/", b""),
        ("目录/说明.txt", b"unicode content"),
        ("目录/", b""),
    ]);
    assert_eq!(read_static_id_from_zip(&zip).unwrap(), "test.Alpha");
    assert_eq!(file_names(&zip).unwrap().len(), 4);
    let dest = f.installed();
    let activated = Cell::new(false);
    install(&zip, &dest, "Alpha", || {
        activated.set(true);
        Ok(())
    })
    .unwrap();
    assert!(activated.get());
    assert_eq!(fs::read(dest.join("Alpha.dll")).unwrap(), b"new");
    assert_eq!(
        fs::read(dest.join("目录/说明.txt")).unwrap(),
        b"unicode content"
    );
    assert!(dest.join("empty").is_dir());
    assert!(!dest.join("obsolete").exists());
    assert!(!dest.parent().unwrap().join(".Alpha.onim-install").exists());
}

#[test]
fn unsafe_zip_names_are_rejected_before_any_extraction() {
    let f = Fixture::new();
    let staging = f.0.join("staging");
    fs::create_dir(&staging).unwrap();
    for name in [
        "../escape",
        "nested/../escape",
        "/absolute",
        "C:/drive",
        "C:relative",
        r"C:\drive",
        r"\\server\share",
        r"nested\file",
        "file:stream",
        "file.",
        "file ",
        "CON",
        "AUX.txt",
        "com1.dll",
        "LPT9",
        "COM¹.txt",
        "a?.txt",
    ] {
        let zip = f.zip(&[("mod.yaml", b"staticID: valid\n"), (name, b"bad")]);
        assert!(read_mod_yaml_from_zip(&zip).is_err(), "{name}");
        assert!(unzip(&zip, &staging).is_err(), "{name}");
        assert_eq!(fs::read_dir(&staging).unwrap().count(), 0, "{name}");
        assert!(!f.0.join("escape").exists());
    }
}

#[test]
fn normalized_aliases_and_file_directory_conflicts_are_rejected() {
    let f = Fixture::new();
    for files in [
        vec![("A/x.txt", &b"one"[..]), ("a/y.txt", &b"two"[..])],
        vec![("A/", &b""[..]), ("a/y.txt", &b"two"[..])],
        vec![("A/X/a", &b"one"[..]), ("A/x/b", &b"two"[..])],
        vec![("mod.yaml", &b"one"[..]), ("./mod.yaml", &b"two"[..])],
        vec![("A.dll", &b"one"[..]), ("a.dll", &b"two"[..])],
        vec![("a//b", &b"one"[..]), ("a/b", &b"two"[..])],
        vec![("parent", &b"file"[..]), ("parent/child", &b"child"[..])],
        vec![("parent/", &b""[..]), ("parent", &b"file"[..])],
    ] {
        let zip = f.zip(&files);
        assert!(file_names(&zip).is_err(), "{files:?}");
    }
}

#[test]
fn duplicate_central_names_and_disagreeing_local_headers_are_rejected() {
    let f = Fixture::new();
    let zip = f.zip(&[("a.txt", b"one"), ("b.txt", b"two")]);
    let original = fs::read(&zip).unwrap();
    let positions: Vec<_> = original
        .windows(5)
        .enumerate()
        .filter_map(|(index, bytes)| (bytes == b"b.txt").then_some(index))
        .collect();
    assert_eq!(positions.len(), 2, "local header and central directory");
    for replace_both in [false, true] {
        let mut bytes = original.clone();
        for index in positions.iter().take(if replace_both { 2 } else { 1 }) {
            bytes[*index..*index + 5].copy_from_slice(b"a.txt");
        }
        fs::write(&zip, bytes).unwrap();
        let error = file_names(&zip).unwrap_err();
        assert!(format!("{error:#}").contains("文件头"), "{error:#}");
    }
}

#[test]
fn zip_symlinks_are_rejected_including_in_tree_targets() {
    let f = Fixture::new();
    for target in ["../outside", "Alpha.dll"] {
        let path = f.0.join("links.zip");
        let mut zip = zip::ZipWriter::new(fs::File::create(&path).unwrap());
        let options = SimpleFileOptions::default();
        zip.start_file("mod.yaml", options).unwrap();
        zip.write_all(b"staticID: test.Alpha\n").unwrap();
        zip.add_symlink("link", target, options).unwrap();
        zip.finish().unwrap();
        let error = read_mod_yaml_from_zip(&path).unwrap_err();
        assert!(format!("{error:#}").contains("符号链接"), "{error:#}");
    }
}

#[test]
fn metadata_requires_one_regular_utf8_file() {
    let f = Fixture::new();
    for files in [
        vec![("other.txt", &b"no metadata"[..])],
        vec![("mod.yaml/", &b""[..])],
        vec![("mod.yaml", &b"one"[..]), ("nested/mod.yaml", &b"two"[..])],
        vec![("mod.yaml", &[0xff, 0xfe][..])],
    ] {
        let zip = f.zip(&files);
        assert!(read_mod_yaml_from_zip(&zip).is_err(), "{files:?}");
    }
}

#[test]
fn corrupt_content_preserves_the_old_install_and_cleans_partial_staging() {
    let f = Fixture::new();
    let zip = f.zip(&[
        ("Alpha.dll", b"new"),
        ("mod_info.yaml", b"minimumSupportedBuild: 744825\n"),
        ("mod.yaml", b"staticID: test.Alpha\n"),
        ("late.bin", b"unique content to corrupt"),
    ]);
    let mut bytes = fs::read(&zip).unwrap();
    let index = bytes
        .windows(b"unique content to corrupt".len())
        .position(|bytes| bytes == b"unique content to corrupt")
        .unwrap();
    bytes[index] ^= 1;
    fs::write(&zip, bytes).unwrap();
    let dest = f.installed();
    let activated = Cell::new(false);
    let error = install(&zip, &dest, "Alpha", || {
        activated.set(true);
        Ok(())
    })
    .unwrap_err();
    assert!(format!("{error:#}").contains("CRC"), "{error:#}");
    assert!(!activated.get());
    assert_eq!(fs::read(dest.join("Alpha.dll")).unwrap(), b"old");
    assert!(dest.join("obsolete").exists());
    assert!(!dest.parent().unwrap().join(".Alpha.onim-install").exists());
}

#[test]
fn directory_install_uses_the_same_activation_rollback_then_replaces_atomically() {
    let f = Fixture::new();
    let source = f.source();
    let dest = f.installed();
    install_directory(&source, &dest, "Alpha", || {
        anyhow::bail!("activation rejected")
    })
    .unwrap_err();
    assert_eq!(fs::read(dest.join("Alpha.dll")).unwrap(), b"old");
    assert!(dest.join("obsolete").exists());
    assert!(!dest.parent().unwrap().join(".Alpha.onim-install").exists());
    install_directory(&source, &dest, "Alpha", || Ok(())).unwrap();
    assert_eq!(fs::read(dest.join("Alpha.dll")).unwrap(), b"new");
    assert!(dest.join("empty").is_dir());
    assert_eq!(
        fs::read(dest.join("目录/说明.txt")).unwrap(),
        b"unicode content"
    );
    assert!(!dest.join("obsolete").exists());
    assert_eq!(fs::read(source.join("Alpha.dll")).unwrap(), b"new");
}

#[test]
fn directory_install_cannot_recurse_into_its_own_staging() {
    let f = Fixture::new();
    let source = f.source();
    let dest = source.join("installed");
    let error = install_directory(&source, &dest, "Alpha", || Ok(())).unwrap_err();
    assert!(format!("{error:#}").contains("来源目录"), "{error:#}");
    assert!(!dest.exists());
    assert!(!source.join(".installed.onim-install").exists());
}

#[cfg(unix)]
#[test]
fn directory_links_and_nonportable_names_leave_old_payload_untouched() {
    use std::os::unix::fs::symlink;
    let f = Fixture::new();
    let source = f.source();
    let dest = f.installed();
    let outside = f.0.join("outside");
    fs::write(&outside, "untouched").unwrap();
    symlink(&outside, source.join("link")).unwrap();
    assert!(install_directory(&source, &dest, "Alpha", || Ok(())).is_err());
    fs::remove_file(source.join("link")).unwrap();
    fs::write(source.join("CON"), "ambiguous").unwrap();
    assert!(install_directory(&source, &dest, "Alpha", || Ok(())).is_err());
    assert_eq!(fs::read(dest.join("Alpha.dll")).unwrap(), b"old");
    assert_eq!(fs::read(&outside).unwrap(), b"untouched");
    fs::remove_file(source.join("CON")).unwrap();
    let alias = f.0.join("source-link");
    symlink(&source, &alias).unwrap();
    assert!(install_directory(&alias, &dest, "Alpha", || Ok(())).is_err());
}

#[cfg(unix)]
#[test]
fn zip_and_directory_install_preserve_executable_and_directory_permissions() {
    use std::os::unix::fs::PermissionsExt;
    let f = Fixture::new();
    let path = f.0.join("permissions.zip");
    let mut zip = zip::ZipWriter::new(fs::File::create(&path).unwrap());
    let options = SimpleFileOptions::default();
    zip.start_file("Alpha.dll", options.unix_permissions(0o644))
        .unwrap();
    zip.write_all(b"dll").unwrap();
    zip.start_file("mod_info.yaml", options.unix_permissions(0o644))
        .unwrap();
    zip.write_all(b"metadata").unwrap();
    zip.start_file("helper", options.unix_permissions(0o751))
        .unwrap();
    zip.write_all(b"executable").unwrap();
    zip.add_directory("empty/", options.unix_permissions(0o750))
        .unwrap();
    zip.finish().unwrap();
    let zipped = f.0.join("zipped");
    install(&path, &zipped, "Alpha", || Ok(())).unwrap();
    let copied = f.0.join("copied");
    install_directory(&zipped, &copied, "Alpha", || Ok(())).unwrap();
    for dest in [&zipped, &copied] {
        assert_eq!(
            fs::metadata(dest.join("helper"))
                .unwrap()
                .permissions()
                .mode()
                & 0o777,
            0o751
        );
        assert_eq!(
            fs::metadata(dest.join("empty"))
                .unwrap()
                .permissions()
                .mode()
                & 0o777,
            0o750
        );
        assert!(dest.join("empty").is_dir());
    }
}

#[cfg(unix)]
#[test]
fn extraction_refuses_an_existing_symlink_destination() {
    use std::os::unix::fs::symlink;
    let f = Fixture::new();
    let zip = f.zip(&[("payload", b"new")]);
    let outside = f.0.join("outside");
    fs::create_dir(&outside).unwrap();
    let alias = f.0.join("alias");
    symlink(&outside, &alias).unwrap();
    assert!(unzip(&zip, &alias).is_err());
    assert_eq!(fs::read_dir(outside).unwrap().count(), 0);
}

#[test]
fn unicode_directory_aliases_never_silently_merge_on_the_host_filesystem() {
    let f = Fixture::new();
    let probe = f.0.join("probe");
    fs::create_dir(&probe).unwrap();
    fs::create_dir(probe.join("é")).unwrap();
    let distinct = match fs::create_dir(probe.join("e\u{301}")) {
        Ok(()) => true,
        Err(error) if error.kind() == ErrorKind::AlreadyExists => false,
        Err(error) => panic!("cannot determine filesystem normalization: {error}"),
    };
    fs::remove_dir_all(probe).unwrap();
    let zip = f.zip(&[
        ("Alpha.dll", b"new"),
        ("mod_info.yaml", b"metadata"),
        ("é/first", b"first"),
        ("e\u{301}/second", b"second"),
    ]);
    let dest = f.installed();
    let result = install(&zip, &dest, "Alpha", || Ok(()));
    if distinct {
        result.unwrap();
        assert_eq!(fs::read(dest.join("é/first")).unwrap(), b"first");
        assert_eq!(fs::read(dest.join("e\u{301}/second")).unwrap(), b"second");
    } else {
        let error = result.unwrap_err();
        assert!(format!("{error:#}").contains("文件系统别名"), "{error:#}");
        assert_eq!(fs::read(dest.join("Alpha.dll")).unwrap(), b"old");
    }
    assert!(!dest.parent().unwrap().join(".Alpha.onim-install").exists());
}
