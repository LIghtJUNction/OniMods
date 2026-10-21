#!/usr/bin/env python3
"""Request a read-only snapshot from one verified, paused test process."""
import argparse
from datetime import datetime, timezone
import fcntl
import json
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent))
from verify_instances import digest, load_backend, native_clock, require
from memory_probe.build import verify_receipt
from memory_probe.core_identity import audit_core


def validate_observation(data, request, status):
    require(data.get('complete') is True and not data.get('error'), 'Memory probe did not complete')
    require(type(data.get('request')) is int and type(data.get('pid')) is int, 'Invalid request/process numbers')
    for key in ('request', 'pid', 'identity', 'runNonce'):
        require(data.get(key) == request[key], 'Memory observation crosses request/run identity: ' + key)
    require(data.get('paused') is True and status.get('paused') is True
            and data.get('activeSavePath') == status.get('activeSavePath'), 'Paused save differs')
    require(native_clock(data.get('simulationSeconds')) == native_clock(status.get('simulationSeconds')),
            'Memory observation has a different native clock')
    for key in ('unityAllocatedBytes', 'unityReservedBytes', 'unityUnusedReservedBytes',
                'monoUsedBytes', 'monoHeapBytes', 'managedReportedBytes'):
        require(type(data.get(key)) is int and data[key] >= 0, 'Invalid memory counter: ' + key)
    memory = data.get('processMemoryKiB', {})
    for key in ('Rss', 'Pss', 'Swap', 'SwapPss', 'Anonymous'):
        require(type(memory.get(key)) is int and memory[key] >= 0, 'Invalid process counter: ' + key)
    counts = data.get('gcCollections')
    generation = data.get('gcMaxGeneration')
    require(type(generation) is int and 0 <= generation <= 8
            and isinstance(counts, list) and len(counts) == generation + 1
            and all(type(n) is int and n >= 0 for n in counts), 'Invalid GC counts')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('harness', 'group-root', 'build-receipt', 'preparation', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--official-library', type=Path,
                        help='Installed pinned libSimDLL.so; required for an official baseline')
    parser.add_argument('--name', required=True)
    args = parser.parse_args()
    require(not args.output.exists() and args.output.suffix == '.json'
            and not args.output.resolve().is_relative_to(args.group_root.resolve()),
            'Use a fresh JSON receipt outside the instance group')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    result = dict(schema=1, passed=False, scope='One read-only paused-game memory snapshot; not a speedup or memory-saving result')
    try:
        build = verify_receipt(args.build_receipt)
        preparation_hash = digest(args.preparation)
        preparation = json.loads(args.preparation.read_text())
        require(digest(args.group_root / 'group.json') == preparation['group_sha256'],
                'Instance group differs from its preparation receipt')
        backend, _, _ = load_backend(args.harness.resolve())
        controller = backend.Controller(args.group_root)
        status = controller.read_status(args.name)
        require(status.get('gameLoaded') is True and status.get('paused') is True, 'Require a loaded paused game')
        identity = backend.ProcessIdentity.capture(status['pid'])
        controller.verify_hashes(args.name, status)
        paths = controller.paths(args.name)
        receipt = json.loads((paths['control'] / 'instance-assemblies.json').read_text())
        require(all(receipt.get(k) == status.get(k) for k in ('pid', 'identity', 'runNonce')), 'Assembly receipt crosses runs')
        loaded = [a for a in receipt['assemblies'] if Path(a.get('location', '')).name == 'CycleTrim.MemoryProbe.dll']
        require(len(loaded) == 1 and loaded[0].get('sha256') == build['probe_sha256'], 'Requested memory probe is not loaded')
        library = Path(loaded[0]['location'])
        require(library.resolve().is_relative_to(paths['user'].resolve()) and digest(library) == build['probe_sha256'],
                'Loaded memory probe differs from the verified private payload')
        managed = [a for a in receipt['assemblies'] if Path(a.get('location', '')).name == 'CycleTrim.dll']
        require(len(managed) == 1 and not managed[0].get('error'), 'Require one loaded CycleTrim assembly')
        managed_path = Path(managed[0]['location'])
        require(managed_path.resolve().is_relative_to(paths['user'].resolve())
                and managed[0].get('sha256') == preparation['managed_sha256']
                and digest(managed_path) == preparation['managed_sha256'], 'Loaded CycleTrim differs from preparation')
        core = audit_core(status['pid'], paths['user'], preparation, args.official_library)
        with (paths['control'] / 'memory-capture.lock').open('a+') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            request_path = paths['control'] / 'memory-probe-request.json'
            previous = json.loads(request_path.read_text()) if request_path.exists() else {}
            same = all(previous.get(k) == status.get(k) for k in ('pid', 'identity', 'runNonce'))
            sequence = previous.get('request', 0) + 1 if same else 1
            require(type(sequence) is int and 0 < sequence <= 2147483647, 'Request counter exhausted')
            request = {k: status[k] for k in ('pid', 'identity', 'runNonce')}
            request['request'] = sequence
            temporary = request_path.with_suffix('.tmp-capture')
            requested_at = datetime.now(timezone.utc)
            temporary.write_text(json.dumps(request) + '\n')
            temporary.replace(request_path)
            deadline = time.monotonic() + 10
            while True:
                current = controller.read_status(args.name)
                require(all(current.get(k) == status.get(k) for k in ('pid', 'identity', 'runNonce', 'activeSavePath', 'simulationSeconds'))
                        and current.get('paused') is True, 'Process/save/paused clock changed during capture')
                response = paths['control'] / 'memory-probe.json'
                if response.exists():
                    data = json.loads(response.read_text())
                    if data.get('request') == sequence and data.get('runNonce') == status['runNonce']:
                        validate_observation(data, request, current)
                        result['observation'] = data
                        break
                    if (data.get('complete') is False and data.get('pid') == status['pid']
                            and datetime.fromisoformat(data['capturedAt'].replace('Z', '+00:00')) >= requested_at):
                        raise RuntimeError('Native probe refused: ' + str(data.get('error')))
                require(time.monotonic() < deadline, 'Memory probe response timed out')
                time.sleep(.1)
            require(backend.ProcessIdentity.capture(status['pid']) == identity, 'PID was reused')
            require(audit_core(status['pid'], paths['user'], preparation, args.official_library) == core,
                    'Simulation core changed during capture')
            require(digest(args.preparation) == preparation_hash
                    and digest(managed_path) == preparation['managed_sha256'], 'Prepared payload changed during capture')
            verify_receipt(args.build_receipt)
            result.update(passed=True, probe_sha256=build['probe_sha256'], capture_script_sha256=digest(Path(__file__)),
                          preparation_sha256=preparation_hash, managed_sha256=preparation['managed_sha256'],
                          simulation_core=core)
    except Exception as error:
        result['error'] = str(error)
        raise
    finally:
        args.output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({'passed': True, 'output': str(args.output)}))


if __name__ == '__main__':
    main()
