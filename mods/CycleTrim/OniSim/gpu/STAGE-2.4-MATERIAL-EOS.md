# Stage 2.4 — Gas EOS calibration adapter

## Scope and decision

The pressure-projection solver requires positive cell density. OniSim's element metadata exposes molar mass and default-state mass, temperature and pressure, but the wire values do not declare SI units. This increment adds an explicit-unit ideal-gas calibration utility and tests its equations. It does not yet bind official game metadata or enable gas pressure projection.

## Method

For one gas species, the ideal-gas relation gives an effective cell volume:

`V = (m/M) R T / p`

All game values are converted through a caller-supplied `UnitScale`. The utility refuses zero, negative or non-finite defaults and refuses unspecified/invalid unit scales.

For a mixture:

`rho = sum(m_i) / V`

`p = (R T / V) * sum(m_i / M_i)`

The implementation preserves vacuum as zero density and zero pressure. It does not add a density floor. A Poisson projection cannot consume that vacuum state until the model defines an open/free-surface boundary or a calibrated positive-density regularization policy.

## Implementation

File: `mods/CycleTrim/OniSim/gpu/src/material_eos.rs`

Public operations:
- `infer_effective_cell_volume`: derive a candidate volume from one gas record.
- `calibrate_effective_cell_volume`: compare multiple gas records using a median and maximum relative deviation.
- `ideal_gas_mixture_state`: calculate mixture density and pressure from species masses, molar masses, temperature and volume.

Input fields map to the native element record:
- `molar_mass`
- `default_mass`
- `default_temperature`
- `default_pressure`

The native record's default fields are calibration inputs, not an assertion that those values are the live state of every cell. Explicit unit conversion factors are mandatory.

## Tests

Six synthetic unit tests cover:
1. effective volume inference;
2. consistency across multiple gas defaults;
3. rejection of inconsistent inferred volumes;
4. mixture density and partial-pressure sum;
5. explicit zero-density vacuum;
6. invalid unit scales and negative mass rejection.

These tests validate the equations and input checks using constructed SI-consistent records. They do not use an official game element table.

## Integration gates

1. Export the pinned official element table from the target game build.
2. Record the unit conventions for mass, molar mass, temperature and pressure.
3. Run cross-material effective-volume consistency and publish the raw per-element table.
4. Resolve vacuum and free-surface boundary behavior.
5. Add liquid/solid density data from explicit material metadata or measured calibration.
6. Only then connect material density to the GPU pressure solver and run pressure-specific semantic fixtures.

## Claim-evidence map

- Claim: ideal-gas effective volume can be calculated from default-state mass, molar mass, temperature and pressure. Evidence: implemented equation and synthetic test. Status: mathematically supported.
- Claim: mixture density and pressure can be calculated from per-species mass and molar mass at a known volume. Evidence: implementation and synthetic mixture test. Status: mathematically supported.
- Claim: official OniSim gas records share one effective cell volume. Evidence: official element table has not yet been supplied to the calibration utility. Status: needs data.
- Claim: gas-to-vacuum pressure projection is supported. Evidence: vacuum is deliberately returned as zero density, while current projection requires positive density. Status: not supported.
- Claim: liquid/solid density is mapped. Evidence: no explicit density field is present in ElementRecord. Status: needs material measurements/configuration.

## Adversarial self-review

- Contribution: a small reusable EOS calibration utility and explicit unit contract; no production behavior changed.
- Clarity: effective cell volume, mixture density and mixture pressure are separate quantities.
- Experimental strength: six synthetic tests; no official game table or GPU EOS kernel.
- Evaluation completeness: official gas defaults, liquid/solid material densities and vacuum fixtures remain to be measured.
- Method soundness: ideal-gas assumptions apply to gas states only. Liquid and solid phases require different constitutive data; vacuum requires boundary treatment.
