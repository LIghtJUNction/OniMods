#!/usr/bin/env python3
"""Recompute all four frozen matrices from raw logs, without running workloads.

Native parsing retains the earlier independent checks, omitting optional paired
diagnostics. Presentation parsing imports the frozen three-round reviewer. This
script writes only new derived receipts and preserves all earlier results.
"""
import csv
import datetime as dt
import hashlib
import importlib.util
import json
import math
import re
import statistics as st
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('three_round_review', ROOT / 'review-three-chain-rounds.py')
THREE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(THREE)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def close(a, b, tolerance=1e-8):
    assert math.isclose(a, b, rel_tol=1e-12, abs_tol=tolerance), (a, b)


def native_round(name):
    directory = ROOT / name
    inputs = json.loads((directory / 'inputs.json').read_text())
    rows = json.loads((directory / 'samples.json').read_text())
    summary = json.loads((directory / 'summary.json').read_text())
    assert len(rows) == 1292 and inputs['samples_per_side'] == 34
    assert inputs['cpu'] == 0 and inputs['workers'] == 1
    assert len(set(inputs['cases'])) == len(inputs['cases']) == 19
    assert len(inputs['baseline']['files_sha256']) == 142
    for filename, key in [('run_native_chain.py', 'driver_sha256'), ('plan-native-chain.md', 'plan_sha256')]:
        assert digest(directory / filename) == inputs[key], (name, filename)
    by_case = defaultdict(list)
    snapshot_reference = {}
    for row in rows:
        assert row['accepted_sample'] and row['attempt'] == 0
        assert row['timed_major_faults'] == 0
        assert row['warmup_batches'] == 2 and row['measured_batches'] == 4
        assert row['case'] == f"{row['width']}x{row['height']}_{row['scene']}_dt{row['elapsed']:.1f}"
        assert len(row['batches_us']) == 4 and all(x > 0 for x in row['batches_us'])
        close(st.mean(row['batches_us']), row['mean_batch_us'], 5.1e-7)
        assert 0 <= row['block'] < 17 and 0 <= row['position'] < 4
        expected_side = 'baseline' if (row['position'] in (0, 3)) == (row['block'] % 2 == 0) else 'candidate'
        assert row['label'] == expected_side
        log = Path(row['log']).read_text()
        raw_samples = re.findall(r'NATIVE_CHAIN_SAMPLE,([^\r\n]+)', log)
        assert len(raw_samples) == 1
        fields = next(csv.reader(raw_samples))
        assert len(fields) == 11
        scene, width, height, elapsed, warmup, measured, mean, cpu, faults, rss, batches = fields
        assert (scene, int(width), int(height), float(elapsed), int(warmup), int(measured)) == (
            row['scene'], row['width'], row['height'], row['elapsed'], row['warmup_batches'], row['measured_batches'])
        close(float(mean), row['mean_batch_us'])
        assert [float(x) for x in batches.split(';')] == row['batches_us']
        assert int(cpu) == row['process_cpu_ticks'] and int(faults) == row['timed_major_faults']
        assert int(rss.split()[0]) == row['process_peak_rss_kib']
        snapshots = []
        for snapshot in re.findall(r'NATIVE_CHAIN_SNAPSHOT,([^\r\n]+)', log):
            label, pub_bytes, pub_sha, world_bytes, world_sha = next(csv.reader([snapshot]))
            assert re.fullmatch('[0-9a-f]{64}', pub_sha) and re.fullmatch('[0-9a-f]{64}', world_sha)
            snapshots.append(dict(label=label, publication_bytes=int(pub_bytes), publication_sha256=pub_sha,
                                  world_bytes=int(world_bytes), world_sha256=world_sha))
        assert len(snapshots) == 8 and snapshots == row['snapshots']
        assert [x['label'] for x in snapshots] == [
            'bootstrap', 'warmup_0', 'warmup_1', 'timed_0', 'timed_1', 'timed_2', 'timed_3', 'followup']
        assert 'test result: ok. 1 passed; 0 failed' in log
        assert snapshot_reference.setdefault(row['case'], snapshots) == snapshots
        by_case[row['case']].append(row)
    assert set(by_case) == set(inputs['cases'])
    results = []
    for case in inputs['cases']:
        sample_rows = by_case[case]
        assert len(sample_rows) == 68
        assert len({(x['block'], x['position']) for x in sample_rows}) == 68
        declared = next(x for x in summary['cases'] if x['case'] == case)
        variants = {}
        for side in ['baseline', 'candidate']:
            raw = [x['mean_batch_us'] for x in sample_rows if x['label'] == side]
            assert len(raw) == 34
            ordered = sorted(raw)
            values = dict(samples=34, median=st.median(raw), p95=ordered[32],
                          minimum=ordered[0], maximum=ordered[-1])
            for key, value in values.items():
                close(value, declared['wall_us'][side][key])
            assert raw == declared['wall_us'][side]['raw']
            variants[side] = values
        change = (variants['candidate']['median'] / variants['baseline']['median'] - 1) * 100
        limit = -10 if case == '736x304_alternating_idle_dt0.0' else 3
        if 'candidate_scope' not in inputs and case == '736x304_pure_dt0.2':
            limit = -5
        if 'gate_limits_percent' in inputs:
            assert inputs['gate_limits_percent'][case] == limit
        close(change, declared['change_percent'])
        assert declared['maximum_change_percent'] == limit
        assert declared['gate_passed'] == (change <= limit)
        results.append(dict(case=case, variants=variants, change_percent=change,
                            limit_percent=limit, gate_passed=change <= limit))
    assert summary['raw_rows'] == 1292
    assert summary['performance_accepted'] == all(x['gate_passed'] for x in results)
    receipt = dict(directory=name, candidate_scope=inputs.get('candidate_scope', 'initial_combined'),
                   baseline_head=inputs['baseline']['head'], candidate_head=inputs['candidate']['head'],
                   executable_sha256=inputs['executables_sha256'], driver_sha256=inputs['driver_sha256'],
                   plan_sha256=inputs['plan_sha256'], cpu=inputs['cpu'], workers=inputs['workers'],
                   runtime=inputs['runtime'], cpu_ticks_hz=inputs['cpu_clock_ticks_per_second'],
                   source_file_counts={k: len(inputs[k]['files_sha256']) for k in ['baseline', 'candidate']},
                   files_sha256={f: digest(directory / f) for f in ['inputs.json', 'samples.json', 'summary.json']},
                   raw_samples=1292, raw_log_files_verified=1292, invalid_samples=0, retries=0,
                   output_snapshots_equal=True, snapshots_verified=1292 * 8,
                   started_utc=dt.datetime.fromtimestamp(min(x['unix_start'] for x in rows), dt.timezone.utc).isoformat(),
                   ended_utc=dt.datetime.fromtimestamp(max(x['unix_start'] + x['process_duration_seconds'] for x in rows), dt.timezone.utc).isoformat(),
                   performance_accepted=all(x['gate_passed'] for x in results), cases=results)
    return inputs, receipt


def main():
    natives = []
    manifests = []
    for label, directory in [
        ('n1', 'native-chain-benchmark-final'),
        ('n2', 'native-chain-benchmark-retained-final'),
        ('n3', 'native-chain-benchmark-worker-box-final'),
        ('n4', 'native-chain-benchmark-exposed-history-final')]:
        inputs, receipt = native_round(directory)
        receipt['round'] = label
        natives.append(receipt)
        manifests.append(inputs)
    assert all(x['baseline'] == manifests[0]['baseline'] for x in manifests)
    assert all(x['cases'] == manifests[0]['cases'] for x in manifests)
    assert all(x['executables_sha256']['baseline'] == manifests[0]['executables_sha256']['baseline'] for x in manifests)
    presentations = [
        THREE.presentation('p1', 'presentation-benchmark-final.log', 'presentation-benchmark-results.json', 'presentation-run-metadata.json'),
        THREE.presentation('p2', 'presentation-benchmark-r2.log', 'presentation-benchmark-r2-results.json', 'presentation-run-r2-metadata.json'),
        THREE.presentation('p3', 'presentation-benchmark-r3.log', 'presentation-benchmark-r3-results.json', 'presentation-run-r3-metadata.json'),
        THREE.presentation('p4', 'presentation-benchmark-r4.log', 'presentation-benchmark-r4-results.json', 'presentation-run-r4-metadata.json')]
    for index in [1, 2, 3]:
        p = presentations[index]
        native = manifests[index]
        assert p['source_head'] == native['candidate']['head']
        overlap = {k: v for k, v in p['source_sha256'].items() if k in native['candidate']['files_sha256']}
        assert all(v == native['candidate']['files_sha256'][k] for k, v in overlap.items())
        p['matched_native_source_entries'] = len(overlap)
    for index in [2, 3]:
        execution = json.loads((ROOT / f'presentation-benchmark-r{index+1}-execution.json').read_text())
        assert execution['exit_code'] == 0 and execution['round'] == f'r{index+1}'
        # The r3 execution schema predates the explicit log digest; its raw
        # log is still parsed fully and hashed by presentation() above.
        if index == 3:
            assert execution['raw_log_sha256'] == digest(Path(execution['raw_log']))
        presentations[index]['execution'] = execution
    p4 = presentations[3]
    assert p4['execution']['analyzer_exit_code'] == int(not p4['timing_gate_pass'])
    assert p4['execution']['summary_sha256'] == digest(ROOT / 'presentation-benchmark-r4-results.json')
    for name, expected in p4['protocol_sha256'].items():
        assert digest(ROOT / name) == expected, ('p4 protocol', name)
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    summary = dict(reviewed_utc=now, reviewer_sha256=digest(Path(__file__)),
                   presentation_parser_sha256=digest(ROOT / 'review-three-chain-rounds.py'),
                   native_raw_logs_verified=sum(x['raw_log_files_verified'] for x in natives),
                   native_snapshots_verified=sum(x['snapshots_verified'] for x in natives),
                   presentation_raw_samples_verified=sum(x['raw_samples'] for x in presentations),
                   same_native_baseline_executable_and_142_source_entries=True,
                   native=[dict(round=x['round'], source_head=x['candidate_head'], samples=x['raw_samples'],
                                accepted=x['performance_accepted'], failed=[dict(case=c['case'],
                                change_percent=c['change_percent'], limit=c['limit_percent'])
                                for c in x['cases'] if not c['gate_passed']]) for x in natives],
                   presentation=[dict(round=x['round'], source_head=x['source_head'], samples=x['raw_samples'],
                                      accepted=x['timing_gate_pass'], failed=[dict(shape=c['shape'],
                                      scenario=c['scenario'], change_percent=c['change_percent'], limit=c['limit_percent'])
                                      for c in x['results'] if not c['gate_passed']]) for x in presentations],
                   limits=['No new timing, resampling, sample deletion or changed gates.',
                           'No optional paired/CPU diagnostics and no attribution of failures to noise.',
                           'Presentation compares original/gate on the same native; native compares versions against f408.',
                           'No real ONI/Unity, GPU upload or displayed FPS measurements.'])
    for name, receipt in [('native-exposed-history-report-data.json', natives[3]),
                          ('presentation-r4-report-data.json', presentations[3]),
                          ('four-round-review.json', summary)]:
        receipt['reviewed_utc'] = now
        receipt['reviewer_sha256'] = summary['reviewer_sha256']
        (ROOT / name).write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
