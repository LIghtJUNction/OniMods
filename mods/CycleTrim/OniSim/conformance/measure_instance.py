#!/usr/bin/env python3
"""Measure one owned, paused CycleTrim game through bounded runtime steps."""
import argparse
from datetime import datetime, timezone
import fcntl
import json
import os
from pathlib import Path
import statistics
import subprocess
import sys
import time

from verify_instances import digest, inspect_instance, load_backend, require


def sample(pid):
    root = Path('/proc') / str(pid)
    fields = (root / 'stat').read_text().rsplit(')', 1)[1].split()
    memory = dict(line.split(':', 1) for line in (root / 'status').read_text().splitlines() if ':' in line)
    rollup = dict(line.split(':', 1) for line in (root / 'smaps_rollup').read_text().splitlines() if ':' in line)
    return dict(start_ticks=int(fields[19]),
                cpu_seconds=(int(fields[11]) + int(fields[12])) / os.sysconf('SC_CLK_TCK'),
                major_faults=int(fields[9]), minor_faults=int(fields[7]),
                rss_kib=int(memory['VmRSS'].split()[0]),
                swap_kib=int(memory.get('VmSwap', '0').split()[0]),
                pss_kib=int(rollup['Pss'].split()[0]),
                swap_pss_kib=int(rollup.get('SwapPss', '0').split()[0]))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('harness', 'group-root', 'stage', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--name', required=True)
    parser.add_argument('--samples', type=int, default=5)
    parser.add_argument('--warmup', type=int, default=1)
    parser.add_argument('--seconds', type=float, default=2)
    args = parser.parse_args()
    require(1 <= args.samples <= 20 and 0 <= args.warmup <= 3 and 0 < args.seconds <= 2,
            'Use 1..20 samples, 0..3 warmups, and bounded steps of at most two seconds')
    require(not args.output.exists() and args.output.suffix == '.json', 'Use a fresh JSON output')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    preparation = args.stage / 'preparation.json'
    prepared = json.loads(preparation.read_text())
    backend, scripts, hashes = load_backend(args.harness.resolve())
    hashes.update({n: digest(scripts / n) for n in ('runtime.py', 'runtime_mcp.py', 'install_runtime.py')})
    controller = backend.Controller(args.group_root)
    managed, native = prepared['managed_sha256'], prepared['native_sha256']
    require(prepared.get('prepared') is True and digest(args.group_root / 'group.json') == prepared['group_sha256'],
            'Group differs from its prepared inputs')
    report = dict(schema=1, complete=False, captured_at=datetime.now(timezone.utc).isoformat(),
                  scope='Single real game, guarded speed-1 wall-time steps including control overhead; not FPS or maximum throughput',
                  baseline_comparison=False, whole_game_speedup_established=False,
                  managed_sha256=managed, native_sha256=native, backend_sha256=hashes,
                  samples=[], warmup_samples=[], requested_wall_seconds=args.seconds,
                  host_load_average=list(os.getloadavg()))
    lock = (args.stage / ('cycletrim-step-' + args.name + '.lock')).open('a+')
    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    original = None
    token = None
    try:
        original = inspect_instance(controller, backend, args.name, managed, native)['status']
        require(original.get('gameLoaded') is True and original.get('paused') is True,
                'Require the actual loaded paused test colony')
        paths = controller.paths(args.name)
        config_path = paths['user'] / 'OniMcpConfig.json'
        config_hash = digest(config_path)
        options = json.loads(config_path.read_text())
        require(options.get('Host') == '127.0.0.1' and options.get('AuthEnabled') is True
                and options.get('Port') == prepared['mcp_profile_configs'][args.name]['port'],
                'Require the prepared private authenticated endpoint')
        token = options.get('AuthToken')
        require(isinstance(token, str) and len(token) >= 32, 'Missing private credential')
        report.update(pid=original['pid'], run_nonce=original['runNonce'],
                      save_sha256=digest(Path(original['activeSavePath'])), initial=original)
        environment = dict(os.environ, ONIHARNESS_MCP_TOKEN=token)
        for index in range(args.warmup + args.samples):
            before = controller.read_status(args.name)
            require(all(before.get(k) == original.get(k) for k in ('pid', 'identity', 'runNonce', 'activeSavePath'))
                    and before.get('paused') is True, 'Run/save changed or game was not paused')
            action = paths['control'] / 'action.json'
            action.unlink(missing_ok=True)
            log = args.output.with_name(args.output.stem + f'-{index}.log')
            counters = sample(original['pid'])
            started = time.monotonic()
            command = ['rtk', 'proxy', sys.executable, str(scripts / 'runtime.py'),
                       '--output', str(paths['control']), '--url', f"http://127.0.0.1:{options['Port']}/mcp/",
                       'step', '--seconds', str(args.seconds), '--speed', '1', '--release-native-pause']
            with log.open('w') as stream:
                subprocess.run(command, env=environment, stdout=stream, stderr=subprocess.STDOUT,
                               timeout=120, check=True)
            after_counters = sample(original['pid'])
            elapsed = time.monotonic() - started
            step = json.loads(action.read_text())
            advanced = step.get('simulationSecondsAdvanced', 0)
            require(advanced > 0, 'Require positive native clock progress')
            # Runtime's MCP pause and the companion's telemetry publish separately.
            # Wait for the same paused clock instead of accepting a stale sample.
            deadline = time.monotonic() + 3
            while True:
                after = controller.read_status(args.name)
                require(all(after.get(k) == original.get(k) for k in ('pid', 'identity', 'runNonce', 'activeSavePath')),
                        'Run/save changed during measurement')
                if (after.get('paused') is True and after.get('gameLoaded') is True
                        and after.get('simulationSeconds', 0) - before['simulationSeconds'] == advanced):
                    break
                require(time.monotonic() < deadline, 'No matching native paused clock after the runtime step')
                time.sleep(.05)
            require(counters['start_ticks'] == after_counters['start_ticks'], 'PID was reused')
            row = dict(index=index, elapsed_seconds=elapsed, native_seconds=advanced,
                       cpu_seconds=after_counters['cpu_seconds'] - counters['cpu_seconds'],
                       major_faults=after_counters['major_faults'] - counters['major_faults'],
                       minor_faults=after_counters['minor_faults'] - counters['minor_faults'],
                       final_memory={k: after_counters[k] for k in ('rss_kib', 'pss_kib', 'swap_kib', 'swap_pss_kib')},
                       timings=step.get('timings'), paused_after=True)
            row['cpu_seconds_per_native_second'] = row['cpu_seconds'] / advanced
            report['warmup_samples' if index < args.warmup else 'samples'].append(row)
            args.output.write_text(json.dumps(report, indent=2) + '\n')
        require(config_hash == digest(config_path) and digest(args.group_root / 'group.json') == prepared['group_sha256'],
                'Configuration changed during measurement')
        require(hashes == {n: digest(scripts / n) for n in hashes}, 'Harness source changed during measurement')
        inspect_instance(controller, backend, args.name, managed, native)
        require(report['save_sha256'] == digest(Path(original['activeSavePath'])), 'Save bytes changed')
        report['summary'] = {k: dict(median=statistics.median(r[k] for r in report['samples']),
                                     minimum=min(r[k] for r in report['samples']),
                                     maximum=max(r[k] for r in report['samples']))
                             for k in ('cpu_seconds_per_native_second', 'elapsed_seconds', 'native_seconds', 'major_faults')}
        report['complete'] = True
    except BaseException as error:
        report['error'] = (str(error) or type(error).__name__).replace(token, '[redacted]') if token else str(error)
        if original is not None:
            try:
                controller.command(args.name, 'pause')
                report['cleanup_paused'] = controller.read_status(args.name).get('paused') is True
            except Exception as cleanup:
                report['cleanup_error'] = type(cleanup).__name__
        raise
    finally:
        args.output.write_text(json.dumps(report, indent=2) + '\n')
        lock.close()
    print(json.dumps(dict(complete=report['complete'], summary=report['summary'])))


if __name__ == '__main__':
    main()
