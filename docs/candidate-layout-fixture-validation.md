# Candidate layout fixture cardinality regression

The experimental benchmark at `90c382f` accepts any key count from 1 to 65536,
but its `(i * 31) % keys` fixture visits only `keys / gcd(31, keys)` distinct
keys. At 31, 62, 961 and 992 requested keys, it actually visits 1, 2, 31 and
32 keys respectively. Both layouts can agree on a checksum despite measuring
the wrong workload. The supplied 64/4096/16384-key measurements are unaffected
by this particular defect; their input order remains unchanged by this fix.

Use stride 31 for the original coprime cases and `keys - 1` for multiples of
31. The latter is coprime with every positive `keys` in that branch. Compute
the product in `long` so large accepted key counts cannot overflow. Every key
now occurs four times. The production candidate-buffer implementation and
experimental/default-off rollout are unchanged.

## Offline validation

Run the existing .NET 10 host suite:

```sh
dotnet run --project benchmarks/CycleTrim.BrainBenchmarks -c Release
```

A regression with the original generator fails with:
`FAIL Candidate fixture requested 31 keys but generated 1`.
With the corrected generator, all 43 existing test-report groups pass, including
fixture counts at 1/2/30/31/62/257/961/992/65536 and both production helper layouts.
The dictionary-based winner oracle is independent of `FetchCandidateBuffer`;
it checks exact key/identity matches and rejects duplicated output keys. Both
layouts also exercise first-wins NaN ties, colliding key hashes and empty reuse;
the compact layout retains its insertion-order assertion.

For each layout, smoke-test the measurement handshake at
1/31/62/961/992/64/4096/16384/65536 keys with 10 timed iterations:

```sh
printf 'GO\nREPORT\n' | dotnet benchmarks/CycleTrim.BrainBenchmarks/bin/Release/net10.0/CycleTrim.BrainBenchmarks.dll --candidate-layout-benchmark baseline 31 10
```

Replace `baseline` with `compact` for its pair. All 18 runs report exactly the
requested winner counts and matching paired checksums. CLI runs now also check
fixture cardinality and winners before READY and validate final winners after
REPORT, outside the measured loop/counter window. These smoke runs establish
fixture/helper correctness only: their timings are not a performance comparison.
No native Mono execution, PMU capture, gameplay test, or game speedup is claimed.
