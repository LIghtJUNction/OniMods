#!/usr/bin/env python3
"""Recompute all three frozen native and presentation rounds from original raw.

No processes under test are executed. Prior review receipts are not overwritten.
Native log parsing reuses the separately retained independent reviewer module.
"""
import csv
import datetime as dt
import hashlib
import importlib.util
import json
import math
import statistics as st
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('previous_review', ROOT / 'review-final-chain-results.py')
PREVIOUS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREVIOUS)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def close(a, b, tolerance=1e-8):
    assert math.isclose(a, b, rel_tol=1e-12, abs_tol=tolerance), (a, b)


def presentation(label, log_name, result_name, metadata_name):
    path = ROOT / log_name
    saved = json.loads((ROOT / result_name).read_text())
    metadata = json.loads((ROOT / metadata_name).read_text())
    groups = defaultdict(list)
    summaries = {}
    runtime = None
    for line in path.read_text().splitlines():
        if line.startswith('runtime='):
            assert runtime is None
            runtime = dict(item.split('=',1) for item in line.split(';'))
        elif line.startswith('summary='):
            fields = dict(item.split('=',1) for item in line.split(';'))
            key = (fields['summary'], fields['scenario'], fields['variant'])
            assert key not in summaries
            summaries[key] = fields
        elif line.startswith('sample,') and not line.startswith('sample,shape,'):
            fields = next(csv.reader([line]))
            assert len(fields) == 14
            _, shape, scenario, variant, *numbers, sha = fields
            assert len(numbers) == 9
            row = dict(zip(['block','position','ticks','iterations','loads','applies','bytes','allocated','revision'], map(int,numbers)))
            row['sha256'] = sha
            groups[(shape,scenario,variant)].append(row)
    shapes = ['96x96','256x384','736x304']
    scenarios = ['render_repeat','zero_prepare','command_two_prepares']
    sides = ['original','revision_gate']
    assert set(groups) == set(summaries) == {(s,c,v) for s in shapes for c in scenarios for v in sides}
    assert runtime == saved['runtime']
    for key,value in dict(blocks='17',iterations='4',warmup_per_sample='4',gpu='0',workers='1',tiered_compilation='0',order='alternating ABBA/BAAB').items():
        assert runtime[key] == value
    frequency = int(runtime['stopwatch_frequency'])
    assert frequency > 0
    results = []
    for shape in shapes:
        width,height = map(int,shape.split('x'))
        for scene in scenarios:
            source = next(x for x in saved['results'] if x['shape']==shape and x['scenario']==scene)
            variants = {}
            positions = set()
            hashes = set()
            for side in sides:
                rows = groups[(shape,scene,side)]
                assert len(rows) == 34
                uploads = 20 if side=='original' or scene=='command_two_prepares' else 0
                for row in rows:
                    assert 0 <= row['block'] < 17 and 0 <= row['position'] < 4
                    key=(row['block'],row['position']); assert key not in positions; positions.add(key)
                    assert ((row['position'] in (0,3)) == (row['block']%2==0)) == (side=='original')
                    assert row['ticks']>0 and row['iterations']==4 and row['revision']>0
                    assert row['loads']==row['applies']==uploads
                    assert row['bytes']==(width*height*21*4 if uploads else 0)
                    assert row['allocated']==0
                    assert len(row['sha256'])==64; hashes.add(row['sha256'])
                ticks=sorted(row['ticks'] for row in rows)
                median=st.median(ticks); p95=ticks[32]
                result=dict(sample_count=34,median_sample_ticks=median,median_ms=median*1000/frequency/4,
                            p95_ms=p95*1000/frequency/4,minimum_ms=ticks[0]*1000/frequency/4,
                            maximum_ms=ticks[-1]*1000/frequency/4,loads_per_sample=uploads,
                            applies_per_sample=uploads,input_bytes_per_sample=rows[0]['bytes'],allocated_bytes=0)
                declared=summaries[(shape,scene,side)]; assert int(declared['samples'])==34
                close(result['median_sample_ticks'],source['variants'][side]['median_sample_ticks'])
                close(result['median_ms'],source['variants'][side]['median_update_ms'])
                close(result['p95_ms'],source['variants'][side]['p95_sample_mean_update_ms'])
                for key,printed in [('median_ms','median_ms'),('p95_ms','p95_sample_mean_ms'),('minimum_ms','min_ms'),('maximum_ms','max_ms')]:
                    close(result[key],float(declared[printed]),5.1e-7)
                variants[side]=result
            assert len(positions)==68 and hashes=={source['sha256']}
            ratio=variants['revision_gate']['median_sample_ticks']/variants['original']['median_sample_ticks']
            limit=3 if scene=='command_two_prepares' else (-10 if shape!='96x96' else None)
            passed=limit is None or ratio<=1+limit/100
            close(ratio,source['candidate_over_original']);close((1-ratio)*100,source['median_reduction_percent'])
            assert passed==source['timing_gate_pass']
            results.append(dict(shape=shape,scenario=scene,variants=variants,change_percent=(ratio-1)*100,
                                limit_percent=limit,gate_passed=passed,sha256=source['sha256']))
    assert sum(map(len,groups.values()))==saved['sample_count']==612
    assert saved['correctness_gate_pass'] and saved['timing_gate_pass']==all(r['gate_passed'] for r in results)
    return dict(round=label,source_head=metadata['git_head'],artifacts=metadata['artifacts'],
                protocol_sha256=metadata['protocol_sha256'],source_sha256=metadata['source_sha256'],
                environment=metadata['environment'],runtime=runtime,raw_samples=612,
                all_counts_loads_bytes_zero_allocations_sha_verified=True,correctness_gate_pass=True,
                timing_gate_pass=all(r['gate_passed'] for r in results),results=results,
                files_sha256={name:digest(ROOT/name) for name in [log_name,result_name,metadata_name]})


def main():
    natives=[]; manifests=[]
    for label,directory in [('n1','native-chain-benchmark-final'),('n2','native-chain-benchmark-retained-final'),('n3','native-chain-benchmark-worker-box-final')]:
        inputs,receipt=PREVIOUS.native_round(directory)
        receipt['round']=label
        for case in receipt['cases']:
            case.pop('paired_blocks')  # No additional paired diagnostics for this round.
        for name,key in [('run_native_chain.py','driver_sha256'),('plan-native-chain.md','plan_sha256')]:
            file=ROOT/directory/name
            assert digest(file)==inputs[key],(directory,name)
        natives.append(receipt);manifests.append(inputs)
    assert all(x['baseline']==manifests[0]['baseline'] for x in manifests)
    assert all(x['cases']==manifests[0]['cases'] for x in manifests)
    assert all(x['executables_sha256']['baseline']==manifests[0]['executables_sha256']['baseline'] for x in manifests)
    presentations=[presentation('p1','presentation-benchmark-final.log','presentation-benchmark-results.json','presentation-run-metadata.json'),
                   presentation('p2','presentation-benchmark-r2.log','presentation-benchmark-r2-results.json','presentation-run-r2-metadata.json'),
                   presentation('p3','presentation-benchmark-r3.log','presentation-benchmark-r3-results.json','presentation-run-r3-metadata.json')]
    for index in [1,2]:
        p=presentations[index]; native=manifests[index]
        assert p['source_head']==native['candidate']['head']
        overlap={k:v for k,v in p['source_sha256'].items() if k in native['candidate']['files_sha256']}
        assert all(v==native['candidate']['files_sha256'][k] for k,v in overlap.items())
        p['matched_native_source_entries']=len(overlap)
    execution=json.loads((ROOT/'presentation-benchmark-r3-execution.json').read_text())
    assert execution['exit_code']==0 and execution['round']=='r3'
    presentations[2]['execution']=execution
    now=dt.datetime.now(dt.timezone.utc).isoformat()
    summary=dict(reviewed_utc=now,reviewer_sha256=digest(Path(__file__)),native_parser_sha256=digest(ROOT/'review-final-chain-results.py'),
                 native_raw_logs_verified=sum(x['raw_log_files_verified'] for x in natives),
                 presentation_raw_samples_verified=sum(x['raw_samples'] for x in presentations),
                 same_native_baseline_executable_and_142_source_entries=True,
                 native=[dict(round=x['round'],source_head=x['candidate_head'],samples=x['raw_samples'],accepted=x['performance_accepted'],
                              failed=[dict(case=c['case'],change_percent=c['change_percent'],limit=c['limit_percent']) for c in x['cases'] if not c['gate_passed']]) for x in natives],
                 presentation=[dict(round=x['round'],source_head=x['source_head'],samples=x['raw_samples'],accepted=x['timing_gate_pass'],
                                    failed=[dict(shape=c['shape'],scenario=c['scenario'],change_percent=c['change_percent'],limit=c['limit_percent']) for c in x['results'] if not c['gate_passed']]) for x in presentations],
                 limits=['No new timing, resampling, sample deletion or changed gates.',
                         'No causal attribution of round-to-round variation or retention decision.',
                         'Presentation is same-native original/gate ablation; native is version comparison against f408.',
                         'No actual ONI, Unity driver or displayed FPS measurements.'])
    for name,receipt in [('native-worker-box-report-data.json',natives[2]),('presentation-r3-report-data.json',presentations[2]),('three-round-review.json',summary)]:
        receipt['reviewed_utc']=now
        receipt['reviewer_sha256']=summary['reviewer_sha256']
        (ROOT/name).write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(summary,ensure_ascii=False,indent=2))


if __name__=='__main__':
    main()
