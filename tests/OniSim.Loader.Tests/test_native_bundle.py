import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("native_bundle", ROOT / "scripts/build_onisim_native.py")
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


class NativeBundleTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
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

    def tearDown(self):
        self.temporary.cleanup()

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
