//! Explicitly calibrated ideal-gas mapping for OniSim material metadata.
//!
//! Game table values are not assumed to be SI. Callers must supply all unit
//! scales and validate the inferred effective cell volume against real tables.
use serde::{Deserialize, Serialize};

const GAS_CONSTANT: f64 = 8.314_462_618_153_24;

#[derive(Clone, Copy, Debug, Deserialize, Serialize)]
pub struct GasDefaults {
    pub molar_mass: f64,
    pub default_mass: f64,
    pub default_temperature: f64,
    pub default_pressure: f64,
}

#[derive(Clone, Copy, Debug, Deserialize, Serialize)]
pub struct UnitScale {
    pub mass_kg_per_game_unit: f64,
    pub molar_mass_kg_per_mol_per_game_unit: f64,
    pub temperature_kelvin_per_game_unit: f64,
    pub temperature_offset_kelvin: f64,
    pub pressure_pa_per_game_unit: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct GasState {
    pub density_kg_m3: f64,
    pub pressure_pa: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Serialize)]
pub struct VolumeCalibration {
    pub effective_cell_volume_m3: f64,
    pub max_relative_deviation: f64,
    pub samples: usize,
}

fn positive_finite(value: f64) -> bool {
    value.is_finite() && value > 0.0
}

impl UnitScale {
    pub fn validate(self) -> Result<(), String> {
        for (name, value) in [
            ("mass scale", self.mass_kg_per_game_unit),
            ("molar-mass scale", self.molar_mass_kg_per_mol_per_game_unit),
            ("temperature scale", self.temperature_kelvin_per_game_unit),
            ("pressure scale", self.pressure_pa_per_game_unit),
        ] {
            if !positive_finite(value) {
                return Err(format!("{name} must be finite and positive"));
            }
        }
        if !self.temperature_offset_kelvin.is_finite() {
            return Err("temperature offset must be finite".into());
        }
        Ok(())
    }

    fn temperature_kelvin(self, game_value: f64) -> Result<f64, String> {
        let value = game_value * self.temperature_kelvin_per_game_unit
            + self.temperature_offset_kelvin;
        if !positive_finite(value) {
            return Err("converted temperature must be finite and above absolute zero".into());
        }
        Ok(value)
    }
}

/// Infer an effective cell volume from one gas element's default state.
///
/// Formula: V = (m / M) R T / p. Every value is converted using the explicit
/// UnitScale. This is a calibration candidate, not an assumed game constant.
pub fn infer_effective_cell_volume(
    gas: GasDefaults,
    units: UnitScale,
) -> Result<f64, String> {
    units.validate()?;
    if !positive_finite(gas.molar_mass)
        || !positive_finite(gas.default_mass)
        || !gas.default_temperature.is_finite()
        || !positive_finite(gas.default_pressure)
    {
        return Err("gas mass, molar mass and pressure must be finite and positive, and temperature finite".into());
    }
    let mass_kg = gas.default_mass * units.mass_kg_per_game_unit;
    let molar_mass = gas.molar_mass * units.molar_mass_kg_per_mol_per_game_unit;
    let temperature = units.temperature_kelvin(gas.default_temperature)?;
    let pressure = gas.default_pressure * units.pressure_pa_per_game_unit;
    let moles = mass_kg / molar_mass;
    let volume = moles * GAS_CONSTANT * temperature / pressure;
    if !positive_finite(volume) {
        return Err("inferred cell volume is not finite and positive".into());
    }
    Ok(volume)
}

/// Calibrate a shared effective cell volume and reject inconsistent defaults.
pub fn calibrate_effective_cell_volume(
    gases: &[GasDefaults],
    units: UnitScale,
    max_relative_deviation: f64,
) -> Result<VolumeCalibration, String> {
    if gases.is_empty() {
        return Err("at least one gas record is required".into());
    }
    if !max_relative_deviation.is_finite() || max_relative_deviation < 0.0 {
        return Err("relative-deviation tolerance must be finite and nonnegative".into());
    }
    let mut volumes = gases.iter()
        .copied()
        .map(|gas| infer_effective_cell_volume(gas, units))
        .collect::<Result<Vec<_>, _>>()?;
    volumes.sort_by(f64::total_cmp);
    let middle = volumes.len() / 2;
    let median = if volumes.len() % 2 == 0 {
        (volumes[middle - 1] + volumes[middle]) * 0.5
    } else {
        volumes[middle]
    };
    let deviation = volumes.iter()
        .map(|volume| (volume - median).abs() / median)
        .fold(0.0, f64::max);
    if deviation > max_relative_deviation {
        return Err(format!(
            "gas defaults imply inconsistent cell volumes: max relative deviation {deviation:e} exceeds {max_relative_deviation:e}"
        ));
    }
    Ok(VolumeCalibration {
        effective_cell_volume_m3: median,
        max_relative_deviation: deviation,
        samples: volumes.len(),
    })
}

/// Compute ideal-gas mixture density and pressure for one cell.
///
/// Zero total mass returns a vacuum state (zero density and pressure). A
/// pressure-projection caller must explicitly classify or regularize vacuum
/// cells; this function does not invent a density floor.
pub fn ideal_gas_mixture_state(
    masses_game: &[f64],
    molar_masses_game: &[f64],
    temperature_game: f64,
    cell_volume_m3: f64,
    units: UnitScale,
) -> Result<GasState, String> {
    units.validate()?;
    if masses_game.len() != molar_masses_game.len() || masses_game.is_empty() {
        return Err("mass and molar-mass channels must be nonempty and have equal length".into());
    }
    if !positive_finite(cell_volume_m3) {
        return Err("cell volume must be finite and positive".into());
    }
    let temperature = units.temperature_kelvin(temperature_game)?;
    let mut total_mass = 0.0;
    let mut total_moles = 0.0;
    for (&mass_game, &molar_mass_game) in masses_game.iter().zip(molar_masses_game) {
        if !mass_game.is_finite() || mass_game < 0.0 || !positive_finite(molar_mass_game) {
            return Err("species masses must be finite/nonnegative and molar masses finite/positive".into());
        }
        let mass_kg = mass_game * units.mass_kg_per_game_unit;
        let molar_mass = molar_mass_game * units.molar_mass_kg_per_mol_per_game_unit;
        total_mass += mass_kg;
        total_moles += mass_kg / molar_mass;
    }
    if !total_mass.is_finite() || !total_moles.is_finite() {
        return Err("mixture totals overflowed".into());
    }
    let density_kg_m3 = total_mass / cell_volume_m3;
    let pressure_pa = total_moles * GAS_CONSTANT * temperature / cell_volume_m3;
    if !density_kg_m3.is_finite() || !pressure_pa.is_finite() {
        return Err("mixture density or pressure overflowed".into());
    }
    Ok(GasState { density_kg_m3, pressure_pa })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn si_units() -> UnitScale {
        UnitScale {
            mass_kg_per_game_unit: 1.0,
            molar_mass_kg_per_mol_per_game_unit: 0.001,
            temperature_kelvin_per_game_unit: 1.0,
            temperature_offset_kelvin: 0.0,
            pressure_pa_per_game_unit: 1.0,
        }
    }

    fn gas(molar_mass_g: f64, mass_kg: f64) -> GasDefaults {
        GasDefaults {
            molar_mass: molar_mass_g,
            default_mass: mass_kg,
            default_temperature: 300.0,
            default_pressure: mass_kg / (molar_mass_g * 0.001)
                * GAS_CONSTANT * 300.0,
        }
    }

    #[test]
    fn infers_effective_volume_from_default_gas_state() {
        let inferred = infer_effective_cell_volume(gas(32.0, 0.032), si_units()).unwrap();
        assert!((inferred - 1.0).abs() < 1e-12);
    }

    #[test]
    fn calibrates_consistent_default_gas_records() {
        let records = [gas(32.0, 0.032), gas(44.0, 0.044), gas(28.0, 0.028)];
        let calibration = calibrate_effective_cell_volume(&records, si_units(), 1e-10).unwrap();
        assert!((calibration.effective_cell_volume_m3 - 1.0).abs() < 1e-12);
        assert_eq!(calibration.samples, 3);
    }

    #[test]
    fn rejects_inconsistent_default_volume() {
        let mut inconsistent = gas(44.0, 0.044);
        inconsistent.default_pressure *= 2.0;
        let records = [gas(32.0, 0.032), inconsistent];
        assert!(calibrate_effective_cell_volume(&records, si_units(), 1e-3).is_err());
    }

    #[test]
    fn computes_mixture_density_and_partial_pressure_sum() {
        let state = ideal_gas_mixture_state(
            &[0.032, 0.044],
            &[32.0, 44.0],
            300.0,
            1.0,
            si_units(),
        ).unwrap();
        assert!((state.density_kg_m3 - 0.076).abs() < 1e-12);
        assert!((state.pressure_pa - 2.0 * GAS_CONSTANT * 300.0).abs() < 1e-9);
    }

    #[test]
    fn preserves_vacuum_as_explicit_zero_state() {
        let state = ideal_gas_mixture_state(&[0.0, 0.0], &[32.0, 44.0], 300.0, 1.0, si_units()).unwrap();
        assert_eq!(state, GasState { density_kg_m3: 0.0, pressure_pa: 0.0 });
    }

    #[test]
    fn rejects_invalid_units_and_material_values() {
        let mut units = si_units();
        units.pressure_pa_per_game_unit = 0.0;
        assert!(infer_effective_cell_volume(gas(32.0, 0.032), units).is_err());
        assert!(ideal_gas_mixture_state(&[-0.1], &[32.0], 300.0, 1.0, si_units()).is_err());
    }

    #[test]
    fn accepts_negative_temperature_when_converted_above_absolute_zero() {
        let mut units = si_units();
        units.temperature_offset_kelvin = 273.15;
        let mut cold = gas(32.0, 0.032);
        cold.default_temperature = -10.0;
        cold.default_pressure = GAS_CONSTANT * 263.15;
        assert!((infer_effective_cell_volume(cold, units).unwrap() - 1.0).abs() < 1e-12);
        cold.default_temperature = -273.15;
        assert!(infer_effective_cell_volume(cold, units).is_err());
    }

    #[test]
    fn rejects_overflow_in_final_pressure_and_density() {
        assert!(ideal_gas_mixture_state(&[1.0], &[32.0], 300.0, f64::MIN_POSITIVE, si_units()).is_err());
        assert!(ideal_gas_mixture_state(&[1e300], &[32.0], 1e100, 1.0, si_units()).is_err());
    }
}
