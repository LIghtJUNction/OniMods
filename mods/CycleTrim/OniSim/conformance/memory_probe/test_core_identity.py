import hashlib
import os
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from memory_probe.core_identity import audit_core, audit_official


class CoreIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.library = self.root / 'game with spaces' / 'libSimDLL.so'
        self.library.parent.mkdir()
        self.library.write_bytes(b'local test bytes, not a game library')
        self.expected = hashlib.sha256(self.library.read_bytes()).hexdigest()
        self.proc = self.root / 'proc'
        (self.proc / '123').mkdir(parents=True)
        self.maps = self.proc / '123' / 'maps'
        stat = self.library.stat()
        self.row = f'1000-2000 r-xp 00000000 {os.major(stat.st_dev):x}:{os.minor(stat.st_dev):x} {stat.st_ino} {self.library}\n'
        self.maps.write_text(self.row)

    def audit(self):
        return audit_official(123, self.library, self.expected, self.proc)

    def test_mapped_reference_accepts_spaces_and_multiple_segments(self):
        self.maps.write_text(self.row + self.row.replace('1000-2000', '3000-4000'))
        self.assertEqual(self.audit(), dict(core='official', native_sha256=self.expected))

    def test_rust_mapping_rejects_official_baseline_even_if_deleted(self):
        for suffix in ('', ' (deleted)'):
            self.maps.write_text(self.row + '5000-6000 r-xp 0 00:00 1 /private/liboni_sim_core.so' + suffix + '\n')
            with self.subTest(suffix=suffix), self.assertRaisesRegex(RuntimeError, 'mapped Rust'):
                self.audit()

    def test_missing_deleted_or_foreign_official_library_is_rejected(self):
        for text in ('', self.row.rstrip() + ' (deleted)\n', self.row.replace(str(self.library), '/other/libSimDLL.so')):
            self.maps.write_text(text)
            with self.subTest(text=text), self.assertRaises(RuntimeError):
                self.audit()

    def test_mapped_inode_must_match_file(self):
        parts = self.row.split(maxsplit=5)
        parts[4] = str(self.library.stat().st_ino + 1)
        self.maps.write_text(' '.join(parts))
        with self.assertRaisesRegex(RuntimeError, 'inode/device'):
            self.audit()

    def test_replaced_bytes_are_rejected(self):
        self.library.write_bytes(b'changed')
        with self.assertRaisesRegex(RuntimeError, 'pinned reference'):
            self.audit()

    def test_unknown_core_and_unpinned_official_are_rejected(self):
        for preparation in ({}, {'requested_simulation_core': 'typo'}, {'requested_simulation_core': 'official'}):
            with self.subTest(preparation=preparation), self.assertRaises(RuntimeError):
                audit_core(123, self.root, preparation, proc=self.proc)
        with self.assertRaisesRegex(RuntimeError, 'pinned reference'):
            audit_core(123, self.root, {'requested_simulation_core': 'official'}, self.library, self.proc)

    def test_rust_requires_actual_private_mapping_and_candidate_hash(self):
        native = self.library.parent / 'liboni_sim_core.so'
        native.write_bytes(b'candidate fixture')
        stat = native.stat()
        self.maps.write_text(f'1000-2000 r-xp 0 {os.major(stat.st_dev):x}:{os.minor(stat.st_dev):x} {stat.st_ino} {native}\n')
        preparation = dict(requested_simulation_core='rust', native_sha256=hashlib.sha256(native.read_bytes()).hexdigest())
        self.assertEqual(audit_core(123, self.root, preparation, proc=self.proc)['core'], 'rust')
        with self.assertRaisesRegex(RuntimeError, 'outside'):
            audit_core(123, self.proc, preparation, proc=self.proc)
        with self.assertRaisesRegex(RuntimeError, 'hash'):
            audit_core(123, self.root, dict(preparation, native_sha256='0' * 64), proc=self.proc)


if __name__ == '__main__':
    unittest.main()
