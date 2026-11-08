import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("native_bundle", ROOT / "scripts/build_onisim_native.py")
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


class NativeBundleTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "bundle"
        self.root.mkdir()
        self.source = Path(self.temporary.name) / "source"
        for relative, contents in {
            "rust-toolchain.toml": '[toolchain]\nchannel = "1.99.1"\n',
            "native/Cargo.toml": '[dependencies]\nonisim-gpu = { path = "../gpu" }\n',
            "native/Cargo.lock": "version = 4\n",
            "native/src/lib.rs": "pub fn simulation() {}\n",
            "gpu/Cargo.toml": '[package]\nname = "onisim-gpu"\n',
            "gpu/src/lib.rs": 'const SHADER: &str = include_str!("flow.wgsl");\n',
            "gpu/src/flow.wgsl": "@compute @workgroup_size(1) fn main() {}\n",
        }.items():
            path = self.source / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(contents.encode())
        patcher = mock.patch.object(bundle, "CORE", self.source)
        patcher.start()
        self.addCleanup(patcher.stop)
        headers = {
            "linux-x64": b"\x7fELF\x02\x01" + bytes(12) + struct.pack("<H", 62),
            "macos-x64": b"\xcf\xfa\xed\xfe" + struct.pack("<I", 0x1000007),
            "windows-x64": b"MZ" + bytes(58) + struct.pack("<I", 64) + b"PE\0\0\x64\x86",
        }
        for target, (_, library) in bundle.TARGETS.items():
            directory = self.root / target
            directory.mkdir()
            (directory / library).write_bytes(headers[target])
            (directory / "build.json").write_text(json.dumps({
                "target": target, "sourceSha256": bundle.source_digest(),
                "binarySha256": hashlib.sha256(headers[target]).hexdigest(),
            }))

    def validate(self):
        with contextlib.redirect_stdout(io.StringIO()):
            bundle.validate_bundle(self.root)

    def test_all_three_matching_platforms(self):
        self.validate()

    def test_missing_platform_refused(self):
        (self.root / "windows-x64/oni_sim_core.dll").unlink()
        with self.assertRaises(ValueError):
            self.validate()

    def test_wrong_architecture_refused_even_with_matching_checksum(self):
        path = self.root / "macos-x64/liboni_sim_core.dylib"
        data = b"\xcf\xfa\xed\xfe" + struct.pack("<I", 0x100000c)
        path.write_bytes(data)
        receipt = self.root / "macos-x64/build.json"
        value = json.loads(receipt.read_text())
        value["binarySha256"] = hashlib.sha256(data).hexdigest()
        receipt.write_text(json.dumps(value))
        with self.assertRaises(ValueError):
            self.validate()

    def test_stale_source_refused(self):
        path = self.root / "linux-x64/build.json"
        value = json.loads(path.read_text())
        value["sourceSha256"] = "0" * 64
        path.write_text(json.dumps(value))
        with self.assertRaises(ValueError):
            self.validate()

    def test_changed_path_dependency_inputs_refuse_old_bundle(self):
        for relative in ("gpu/Cargo.toml", "gpu/src/lib.rs", "gpu/src/flow.wgsl"):
            with self.subTest(input=relative):
                path = self.source / relative
                original = path.read_bytes()
                try:
                    path.write_bytes(original + b"\n// changed input\n")
                    with self.assertRaisesRegex(ValueError, "does not match current source"):
                        self.validate()
                finally:
                    path.write_bytes(original)

    def test_added_or_removed_shader_refuses_old_bundle(self):
        added = self.source / "gpu/src/another.wgsl"
        added.write_bytes(b"@compute @workgroup_size(1) fn another() {}\n")
        with self.assertRaisesRegex(ValueError, "does not match current source"):
            self.validate()
        added.unlink()
        (self.source / "gpu/src/flow.wgsl").unlink()
        with self.assertRaisesRegex(ValueError, "does not match current source"):
            self.validate()

    def test_checkout_line_endings_preserve_source_identity(self):
        for path in self.source.rglob("*"):
            if path.is_file():
                path.write_bytes(path.read_bytes().replace(b"\n", b"\r\n"))
        self.validate()

    def test_changed_binary_refused(self):
        with (self.root / "windows-x64/oni_sim_core.dll").open("ab") as output:
            output.write(b"changed")
        with self.assertRaises(ValueError):
            self.validate()

    def test_unexpected_file_refused(self):
        (self.root / "private.txt").write_text("must not be packaged")
        with self.assertRaises(ValueError):
            self.validate()


if __name__ == "__main__":
    unittest.main()
