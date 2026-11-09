#!/usr/bin/env python3
"""Independent read-only recalculation of the two frozen final matrices.

Reads retained raw logs and manifests; never invokes a benchmark or edits them.
Writes only derived review receipts next to those retained inputs.
"""
import csv
import datetime as dt
import hashlib
import json
import math
import re
import statistics as st
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def close(a, b, tolerance=1e-8):
    assert math.isclose(a, b, rel_tol=1e-12, abs_tol=tolerance), (a, b)


def stats(values):
    values = sorted(values)
    return dict(samples=len(values), median=st.median(values),
                p95=values[math.ceil(.95 * len(values)) - 1],
                minimum=min(values), maximum=max(values))


def paired(rows, first, second, value_key):
    blocks = defaultdict(lambda: defaultdict(list))
    for row in rows:
        blocks[row['block']][row['label']].append(row[value_key])
    values = []
    for block in range(17):
        assert len(blocks[block][first]) == len(blocks[block][second]) == 2
        values.append((st.mean(blocks[block][second]) / st.mean(blocks[block][first]) - 1) * 100)
    return dict(block_count=17, median_percent=st.median(values),
                minimum_percent=min(values), maximum_percent=max(values),
                slower_blocks=sum(x > 0 for x in values),
                above_three_percent_blocks=sum(x > 3 for x in values),
                raw_percent=values)


def native_round(directory):
    directory = ROOT / directory
    inputs = json.loads((directory / 'inputs.json').read_text())
    rows = json.loads((directory / 'samples.json').read_text())
    summary = json.loads((directory / 'summary.json').read_text())
    assert len(rows) == 1292 and inputs['samples_per_side'] == 34
    assert inputs['cpu'] == 0 and inputs['workers'] == 1
    assert len(set(inputs['cases'])) == len(inputs['cases']) == 19
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
        expected = ('baseline' if (row['position'] in (0, 3)) == (row['block'] % 2 == 0) else 'candidate')
        assert row['label'] == expected
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
        raw_snapshots = []
        for snapshot in re.findall(r'NATIVE_CHAIN_SNAPSHOT,([^\r\n]+)', log):
            label, pub_bytes, pub_sha, world_bytes, world_sha = next(csv.reader([snapshot]))
            raw_snapshots.append(dict(label=label, publication_bytes=int(pub_bytes),
                                      publication_sha256=pub_sha, world_bytes=int(world_bytes), world_sha256=world_sha))
        assert len(raw_snapshots) == 8 and raw_snapshots == row['snapshots']
        assert [s['label'] for s in raw_snapshots] == [
            'bootstrap', 'warmup_0', 'warmup_1', 'timed_0', 'timed_1', 'timed_2', 'timed_3', 'followup']
        assert 'test result: ok. 1 passed; 0 failed' in log
        previous = snapshot_reference.setdefault(row['case'], raw_snapshots)
        assert previous == raw_snapshots
        by_case[row['case']].append(row)
    assert set(by_case) == set(inputs['cases'])
    results = []
    for case in inputs['cases']:
        sample_rows = by_case[case]
        assert len(sample_rows) == 68
        assert len({(r['block'], r['position']) for r in sample_rows}) == 68
        source = next(x for x in summary['cases'] if x['case'] == case)
        variants = {}
        for side in ['baseline', 'candidate']:
            values = [r['mean_batch_us'] for r in sample_rows if r['label'] == side]
            assert len(values) == 34
            result = stats(values)
            for key in result:
                close(result[key], source['wall_us'][side][key])
            assert source['wall_us'][side]['raw'] == values
            variants[side] = result
        change = (variants['candidate']['median'] / variants['baseline']['median'] - 1) * 100
        limit = -10 if case == '736x304_alternating_idle_dt0.0' else 3
        if 'candidate_scope' not in inputs and case == '736x304_pure_dt0.2':
            limit = -5
        if 'gate_limits_percent' in inputs:
            assert inputs['gate_limits_percent'][case] == limit
        close(change, source['change_percent'])
        assert source['maximum_change_percent'] == limit
        assert source['gate_passed'] == (change <= limit)
        results.append(dict(case=case, variants=variants, change_percent=change,
                            limit_percent=limit, gate_passed=change <= limit,
                            paired_blocks=paired(sample_rows, 'baseline', 'candidate', 'mean_batch_us')))
    assert summary['raw_rows'] == len(rows)
    assert summary['performance_accepted'] == all(x['gate_passed'] for x in results)
    receipt = dict(directory=directory.name, candidate_scope=inputs.get('candidate_scope', 'initial_combined'),
                   baseline_head=inputs['baseline']['head'], candidate_head=inputs['candidate']['head'],
                   executable_sha256=inputs['executables_sha256'], driver_sha256=inputs['driver_sha256'],
                   plan_sha256=inputs['plan_sha256'], cpu=inputs['cpu'], workers=inputs['workers'],
                   runtime=inputs['runtime'], cpu_ticks_hz=inputs['cpu_clock_ticks_per_second'],
                   source_file_counts={k:len(inputs[k]['files_sha256']) for k in ['baseline','candidate']},
                   files_sha256={name:digest(directory / name) for name in ['inputs.json','samples.json','summary.json']},
                   raw_samples=len(rows), raw_log_files_verified=len(rows), invalid_samples=0, retries=0,
                   output_snapshots_equal=True, snapshots_verified=len(rows)*8,
                   started_utc=dt.datetime.fromtimestamp(min(r['unix_start'] for r in rows), dt.timezone.utc).isoformat(),
                   ended_utc=dt.datetime.fromtimestamp(max(r['unix_start']+r['process_duration_seconds'] for r in rows), dt.timezone.utc).isoformat(),
                   performance_accepted=all(x['gate_passed'] for x in results), cases=results)
    return inputs, receipt


def presentation_round():
    path = ROOT / 'presentation-benchmark-r2.log'
    expected = json.loads((ROOT / 'presentation-benchmark-r2-results.json').read_text())
    metadata = json.loads((ROOT / 'presentation-run-r2-metadata.json').read_text())
    groups = defaultdict(list)
    reported = {}
    runtime = None
    for line in path.read_text().splitlines():
        if line.startswith('runtime='):
            assert runtime is None
            runtime = dict(item.split('=', 1) for item in line.split(';'))
        elif line.startswith('summary='):
            values = dict(item.split('=', 1) for item in line.split(';'))
            key = (values['summary'], values['scenario'], values['variant'])
            assert key not in reported
            reported[key] = values
        elif line.startswith('sample,') and not line.startswith('sample,shape,'):
            fields = next(csv.reader([line]))
            assert len(fields) == 14
            _, shape, scenario, variant, block, position, ticks, iterations, loads, applies, loaded, allocated, revision, sha = fields
            groups[(shape, scenario, variant)].append(dict(
                block=int(block), position=int(position), ticks=int(ticks), iterations=int(iterations),
                loads=int(loads), applies=int(applies), loaded_bytes=int(loaded), allocated=int(allocated),
                revision=int(revision), sha256=sha, label=variant))
    assert runtime == expected['runtime']
    assert runtime['tiered_compilation'] == '0' and runtime['gpu'] == '0' and runtime['workers'] == '1'
    assert runtime['blocks'] == '17' and runtime['iterations'] == '4' and runtime['warmup_per_sample'] == '4'
    assert runtime['order'] == 'alternating ABBA/BAAB'
    frequency = int(runtime['stopwatch_frequency'])
    assert frequency > 0
    shapes = ['96x96', '256x384', '736x304']
    scenarios = ['render_repeat', 'zero_prepare', 'command_two_prepares']
    variants = ['original', 'revision_gate']
    assert set(groups) == set(reported) == {(w,s,v) for w in shapes for s in scenarios for v in variants}
    results = []
    for shape in shapes:
        width, height = map(int, shape.split('x'))
        for scenario in scenarios:
            source = next(r for r in expected['results'] if r['shape'] == shape and r['scenario'] == scenario)
            output = dict(shape=shape, scenario=scenario, variants={})
            combined = []
            for side in variants:
                rows = groups[(shape, scenario, side)]
                combined.extend(rows)
                assert len(rows) == 34
                upload_count = 20 if side == 'original' or scenario == 'command_two_prepares' else 0
                for row in rows:
                    assert 0 <= row['block'] < 17 and 0 <= row['position'] < 4
                    expected_side = ('original' if (row['position'] in (0,3)) == (row['block'] % 2 == 0) else 'revision_gate')
                    assert side == expected_side
                    assert row['iterations'] == 4 and row['ticks'] > 0 and row['revision'] > 0
                    assert row['loads'] == row['applies'] == upload_count
                    assert row['loaded_bytes'] == (width*height*21*4 if upload_count else 0)
                    assert row['allocated'] == 0
                    assert re.fullmatch('[0-9a-f]{64}', row['sha256'])
                ticks = stats([r['ticks'] for r in rows])
                values = dict(sample_count=34, median_sample_ticks=ticks['median'],
                              median_ms=ticks['median']*1000/frequency/4,
                              p95_ms=ticks['p95']*1000/frequency/4,
                              minimum_ms=ticks['minimum']*1000/frequency/4,
                              maximum_ms=ticks['maximum']*1000/frequency/4,
                              loads_per_sample=upload_count, applies_per_sample=upload_count,
                              input_bytes_per_sample=rows[0]['loaded_bytes'], measuring_thread_allocated_bytes=0)
                saved = source['variants'][side]
                close(values['median_sample_ticks'], saved['median_sample_ticks'])
                close(values['median_ms'], saved['median_update_ms'])
                close(values['p95_ms'], saved['p95_sample_mean_update_ms'])
                declared = reported[(shape,scenario,side)]
                assert int(declared['samples']) == 34
                for raw_key, display_key in [('median_ms','median_ms'),('p95_ms','p95_sample_mean_ms'),
                                             ('minimum_ms','min_ms'),('maximum_ms','max_ms')]:
                    close(values[raw_key], float(declared[display_key]), 5.1e-7)
                output['variants'][side] = values
            assert len({(r['block'],r['position']) for r in combined}) == 68
            digests = {r['sha256'] for r in combined}
            assert len(digests) == 1 and next(iter(digests)) == source['sha256']
            ratio = output['variants']['revision_gate']['median_sample_ticks'] / output['variants']['original']['median_sample_ticks']
            limit = 3 if scenario == 'command_two_prepares' else (-10 if shape != '96x96' else None)
            change = (ratio-1)*100
            passed = limit is None or ratio <= 1+limit/100
            close(ratio, source['candidate_over_original'])
            close(-change, source['median_reduction_percent'])
            assert source['timing_gate_pass'] == passed
            output.update(change_percent=change, limit_percent=limit, gate_passed=passed,
                          sha256=next(iter(digests)), paired_blocks=paired(combined,'original','revision_gate','ticks'))
            results.append(output)
    assert expected['sample_count'] == sum(map(len, groups.values())) == 612
    assert expected['correctness_gate_pass'] and expected['timing_gate_pass'] == all(r['gate_passed'] for r in results)
    return dict(raw_log=path.name, source_head=metadata['git_head'], artifacts=metadata['artifacts'],
                protocol_sha256=metadata['protocol_sha256'], environment=metadata['environment'], runtime=runtime,
                raw_samples=612, all_counts_loads_bytes_allocations_sha_verified=True,
                correctness_gate_pass=True, timing_gate_pass=all(r['gate_passed'] for r in results),
                source_sha256=metadata['source_sha256'],
                files_sha256={name:digest(ROOT / name) for name in [path.name,'presentation-benchmark-r2-results.json','presentation-run-r2-metadata.json']},
                results=results)


def main():
    first_inputs, first = native_round('native-chain-benchmark-final')
    final_inputs, final = native_round('native-chain-benchmark-retained-final')
    assert first_inputs['executables_sha256']['baseline'] == final_inputs['executables_sha256']['baseline']
    assert first_inputs['baseline'] == final_inputs['baseline']
    assert first_inputs['cases'] == final_inputs['cases']
    for filename in ['fluid_pressure.rs', 'simulation_step_fluid.rs']:
        key = 'mods/CycleTrim/OniSim/native/src/' + filename
        assert final_inputs['baseline']['files_sha256'][key] == final_inputs['candidate']['files_sha256'][key]
    paired_diagnostics = json.loads((ROOT / 'native-chain-benchmark-retained-final/paired-diagnostics.json').read_text())
    for case in final['cases']:
        other = next(x for x in paired_diagnostics['cases'] if x['case'] == case['case'])
        assert len(other['paired_block_raw_change_percent']) == 17
        for a, b in zip(case['paired_blocks']['raw_percent'], other['paired_block_raw_change_percent']):
            close(a, b)
    comparison = []
    for before, after in zip(first['cases'], final['cases']):
        assert before['case'] == after['case']
        b, a = before['variants']['baseline'], after['variants']['baseline']
        comparison.append(dict(case=before['case'], first_baseline=b, retained_baseline=a,
                               baseline_median_change_percent=(a['median']/b['median']-1)*100,
                               baseline_p95_change_percent=(a['p95']/b['p95']-1)*100,
                               first_paired_blocks=before['paired_blocks'], retained_paired_blocks=after['paired_blocks']))
    final['same_baseline_comparison'] = comparison
    final['first_round_runtime'] = first['runtime']
    final['same_baseline_executable_and_source_manifest'] = True
    final['removed_liquid_optimization_files_equal_baseline'] = True
    final['all_19_paired_raw_distributions_match_other_reviewer'] = True
    final['paired_estimator'] = 'Per block: mean(two candidate samples) / mean(two baseline samples) - 1; descriptive only, never replaces the frozen ratio-of-34-medians gate.'
    final['limitations'] = [
        'Same baseline executable changed absolute measurements between sessions, but neither session is invalidated by this observation.',
        'Initial host load averages are point-in-time aggregate indicators, not a continuous measurement of CPU0 availability.',
        'No CPU frequency, scheduler runqueue, host contention or thread waiting trace was recorded here; no causal noise attribution is established.',
        'Process CPU ticks have 100 Hz resolution and a broader boundary than the wall sample.',
        'All original samples and unchanged gates remain in the result; no retries, removals or new timing occurred in this review.'
    ]
    presentation = presentation_round()
    shared = {k:v for k,v in presentation['source_sha256'].items() if k in final_inputs['candidate']['files_sha256']}
    assert all(final_inputs['candidate']['files_sha256'][k] == v for k,v in shared.items())
    assert presentation['source_head'] == final['candidate_head']
    presentation['matched_native_source_manifest_entries'] = len(shared)
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    for filename, receipt in [('native-retained-report-data.json',final),('presentation-r2-report-data.json',presentation)]:
        receipt['reviewed_utc'] = now
        receipt['reviewer_script_sha256'] = digest(Path(__file__))
        (ROOT / filename).write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(dict(native_samples=final['raw_samples'], native_accepted=final['performance_accepted'],
                          native_failed=[(x['case'],x['change_percent']) for x in final['cases'] if not x['gate_passed']],
                          presentation_samples=presentation['raw_samples'], presentation_accepted=presentation['timing_gate_pass'],
                          same_baseline_manifest=True, matched_source_entries=len(shared),reviewed_utc=now), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
