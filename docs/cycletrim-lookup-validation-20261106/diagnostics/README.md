# Development diagnostics, excluded from formal results

These logs retain the unsuccessful initial design, the allocation failures,
the instrumented investigation, and the final harness smoke. They are not used
in either formal matrix's performance summary.

| Record | Purpose |
| --- | --- |
| `benchmark-smoke.log` | Initial implementation, before the first-item bypass and inline first answer |
| `benchmark-v2-smoke.log` | Revised production implementation; candidate measurement observed 160 B |
| `benchmark-v2-loop-smoke.log` | Shared non-inlined loop; original baseline measurement observed 160 B |
| `benchmark-v2-no-tier-smoke.log` | Development control with tiering disabled |
| `benchmark-final-smoke.log` | Final declared warmup protocol; all warm samples passed the 0 B gate |
| `smi-allocation-phase-default.log` | Instrumented phase accounting under the inherited/default runtime settings |
| `smi-allocation-phase-no-osr.log` | Same diagnostic with the explicit OSR override recorded in `receipt.json` |

See [receipt.json](receipt.json) for the executed diagnostic commands, explicit
environment changes, requested CPU affinity, unrecorded environment details and
the limits of the allocation diagnosis. The original failed smoke samples did
not capture allocation stacks; the specific allocated object type is unknown.

Formal results are exclusively in [../save](../save/) and
[../state-machine](../state-machine/), with six processes per benchmark. The
initial implementation and diagnostic source snapshots are retained in the
external evidence archive; they are not production inputs.
