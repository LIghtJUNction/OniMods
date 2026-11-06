"""Probe CLIs must issue receipts only for a fresh build of all current sources."""
from dataclasses import replace
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

from memory_probe.build import PROBE as MEMORY_PROBE
from texture_probe.build import PROBE as TEXTURE_PROBE


PROBES = (TEXTURE_PROBE, MEMORY_PROBE)


class ProbeBuildTests(unittest.TestCase):
    def fixture(self, descriptor):
        temporary = tempfile.TemporaryDirectory(prefix="cycletrim-probe-")
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        source = root / descriptor.root.name
        shutil.copytree(descriptor.root, source,
                        ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
        shutil.copy2(descriptor.root.parent / "probe_build.py", root / "probe_build.py")
        managed = root / "managed"
        managed.mkdir()
        (managed / "Assembly-CSharp.dll").write_bytes(b"inert reference; never loaded")
        tools = root / "tools"
        tools.mkdir()
        compiler = tools / "dotnet"
        compiler.write_text(f"#!{sys.executable}\n" + '''
from pathlib import Path
import os
import sys

assert sys.argv[1] == "build"
project = Path(sys.argv[2])
output = Path(next(arg.split("=", 1)[1] for arg in sys.argv if arg.startswith("-p:OutputPath=")))
mode = os.environ["PROBE_TEST_MODE"]
if mode == "fail":
    raise SystemExit(9)
if mode == "missing":
    raise SystemExit(0)
output.mkdir(parents=True, exist_ok=True)
(output / ("CycleTrim." + project.stem + ".dll")).write_bytes(b"authored compiler artifact")
if mode == "change-source":
    with project.with_suffix(".cs").open("a") as source:
        source.write("\\n// changed during compilation\\n")
if mode == "change-builder":
    with (project.parent.parent / "probe_build.py").open("a") as source:
        source.write("\\n# changed during compilation\\n")
''')
        compiler.chmod(0o700)
        environment = dict(os.environ, PATH=str(tools) + os.pathsep + os.environ["PATH"])
        return replace(descriptor, root=source), managed, root / "output", environment

    def invoke(self, fixture, mode="ok"):
        probe, managed, output, environment = fixture
        return subprocess.run([sys.executable, str(probe.root / "build.py"),
                               "--managed-path", str(managed), "--output", str(output)],
                              cwd=output.parent, env=dict(environment, PROBE_TEST_MODE=mode),
                              capture_output=True, text=True, timeout=10)

    def test_both_clis_bind_probe_and_shared_sources_to_the_build(self):
        for descriptor in PROBES:
            with self.subTest(probe=descriptor.name):
                fixture = self.fixture(descriptor)
                probe, managed, output, _ = fixture
                result = self.invoke(fixture)
                self.assertEqual(result.returncode, 0, result.stderr)
                data = probe.verify_receipt(output / "receipt.json")
                self.assertEqual(json.loads(result.stdout), data)
                expected = {f"{probe.name}Probe.cs", f"{probe.name}Probe.csproj",
                            "mod.yaml", "mod_info.yaml", "build.py", "../probe_build.py"}
                if probe.name == "Memory":
                    expected.update(("MemoryRequest.cs", "MemoryRequestTests.cs"))
                self.assertEqual(set(data["source_sha256"]), expected)
                self.assertEqual(data["reference_managed_sha256"],
                                 hashlib.sha256((managed / "Assembly-CSharp.dll").read_bytes()).hexdigest())
                self.assertEqual(Path(data["output_dll"]).name, f"CycleTrim.{probe.name}Probe.dll")

    def test_receipts_reject_stale_sources_binaries_and_failed_builds(self):
        for descriptor in PROBES:
            fixture = self.fixture(descriptor)
            probe, _, output, _ = fixture
            self.assertEqual(self.invoke(fixture).returncode, 0)
            receipt = output / "receipt.json"
            data = probe.verify_receipt(receipt)
            for name in data["source_sha256"]:
                with self.subTest(probe=probe.name, source=name):
                    path = probe.root / name
                    original = path.read_bytes()
                    path.write_bytes(original + b"\nchanged source")
                    with self.assertRaisesRegex(RuntimeError, "source changed"):
                        probe.verify_receipt(receipt)
                    path.write_bytes(original)
            binary = Path(data["output_dll"])
            original = binary.read_bytes()
            binary.write_bytes(b"stale binary")
            with self.assertRaisesRegex(RuntimeError, "DLL differs"):
                probe.verify_receipt(receipt)
            binary.unlink()
            with self.assertRaisesRegex(RuntimeError, "DLL differs"):
                probe.verify_receipt(receipt)
            binary.write_bytes(original)
            receipt.write_text(json.dumps({**data, "build_exit_code": 1}))
            with self.assertRaisesRegex(RuntimeError, "did not succeed"):
                probe.verify_receipt(receipt)
            incomplete = dict(data["source_sha256"])
            incomplete.pop("../probe_build.py")
            receipt.write_text(json.dumps({**data, "source_sha256": incomplete}))
            with self.assertRaisesRegex(RuntimeError, "source changed"):
                probe.verify_receipt(receipt)

    def test_failed_or_mutated_rebuild_never_leaves_a_successful_receipt(self):
        for descriptor in PROBES:
            for mode in ("fail", "missing", "change-source", "change-builder"):
                with self.subTest(probe=descriptor.name, mode=mode):
                    fixture = self.fixture(descriptor)
                    probe, _, output, _ = fixture
                    self.assertEqual(self.invoke(fixture).returncode, 0)
                    data = probe.verify_receipt(output / "receipt.json")
                    result = self.invoke(fixture, mode)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn("no receipt issued", result.stderr)
                    self.assertFalse((output / "receipt.json").exists())
                    if mode in ("fail", "missing"):
                        self.assertFalse(Path(data["output_dll"]).exists())


if __name__ == "__main__":
    unittest.main()
