"""Bind memory samples to the native library actually mapped by the game."""
import hashlib
import os
from pathlib import Path

from verify_instances import digest, mapped_native, require


def audit_core(pid, profile, preparation, official_library=None, proc=Path('/proc')):
    mode = preparation.get('requested_simulation_core')
    require(mode in ('rust', 'official'), 'Preparation must explicitly select the simulation core')
    if mode == 'rust':
        actual = mapped_native(pid, profile, preparation['native_sha256'], proc)
        return dict(core=mode, native_sha256=actual)

    require(official_library is not None, 'Official baseline requires the pinned official library')
    official_library = official_library.resolve()
    # This is the installed reference used by the conformance and timing runs.
    expected = '85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d'
    return audit_official(pid, official_library, expected, proc)


def audit_official(pid, library, expected, proc=Path('/proc')):
    rows = (proc / str(pid) / 'maps').read_text().splitlines()
    require(not any('liboni_sim_core.so' in row for row in rows),
            'Official baseline has a mapped Rust core')
    mapped = set()
    for row in rows:
        fields = row.split(maxsplit=5)
        if len(fields) != 6 or 'libSimDLL.so' not in fields[5]:
            continue
        require(not fields[5].endswith(' (deleted)'), 'Mapped official library was deleted')
        path = Path(fields[5])
        require(path.name == 'libSimDLL.so' and path.resolve() == library.resolve(),
                'Official library is outside the selected installation')
        major, minor = (int(part, 16) for part in fields[3].split(':'))
        with path.open('rb') as stream:
            before = os.fstat(stream.fileno())
            actual = hashlib.file_digest(stream, 'sha256').hexdigest()
            after = os.fstat(stream.fileno())
        require(before.st_ino == int(fields[4]) and before.st_dev == os.makedev(major, minor),
                'Official mapping inode/device differs from its file')
        fields_to_check = ('st_ino', 'st_dev', 'st_size', 'st_mtime_ns', 'st_ctime_ns')
        require(all(getattr(before, k) == getattr(after, k) for k in fields_to_check),
                'Official library changed while hashing')
        require(actual == expected, 'Mapped official library differs from the pinned reference')
        mapped.add(str(path))
    require(len(mapped) == 1, 'Require exactly one mapped official library')
    require(digest(library) == expected, 'Official reference changed during audit')
    return dict(core='official', native_sha256=expected)
