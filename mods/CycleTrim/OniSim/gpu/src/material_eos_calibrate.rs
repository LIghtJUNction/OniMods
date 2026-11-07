use onisim_gpu::material_eos::{
    calibrate_effective_cell_volume, infer_effective_cell_volume, GasDefaults, UnitScale,
};
use serde::{Deserialize, Serialize};
use std::{env, fs, process};

#[derive(Deserialize)]
struct GasRecord {
    name: String,
    #[serde(flatten)]
    defaults: GasDefaults,
}

#[derive(Deserialize)]
struct Input {
    source: String,
    source_sha256: String,
    units: UnitScale,
    max_relative_deviation: f64,
    gases: Vec<GasRecord>,
}

#[derive(Serialize)]
struct GasVolume {
    name: String,
    effective_cell_volume_m3: f64,
}

#[derive(Serialize)]
struct Output {
    source: String,
    source_sha256: String,
    effective_cell_volume_m3: f64,
    max_relative_deviation: f64,
    samples: usize,
    gases: Vec<GasVolume>,
}

fn run(path: &str) -> Result<(), String> {
    let bytes = fs::read(path).map_err(|error| format!("cannot read {path}: {error}"))?;
    let input: Input = serde_json::from_slice(&bytes).map_err(|error| format!("invalid calibration JSON: {error}"))?;
    if input.source.trim().is_empty() {
        return Err("source must identify the official element table and game build".into());
    }
    if input.source_sha256.len() != 64 || !input.source_sha256.bytes().all(|b| b.is_ascii_hexdigit()) {
        return Err("source_sha256 must be a 64-character hexadecimal digest".into());
    }
    if input.gases.iter().any(|gas| gas.name.trim().is_empty()) {
        return Err("every gas record must have a nonempty material name".into());
    }
    let volumes = input.gases.iter().map(|gas| {
        Ok(GasVolume {
            name: gas.name.clone(),
            effective_cell_volume_m3: infer_effective_cell_volume(gas.defaults, input.units)?,
        })
    }).collect::<Result<Vec<_>, String>>()?;
    let defaults = input.gases.iter().map(|gas| gas.defaults).collect::<Vec<_>>();
    let calibration = calibrate_effective_cell_volume(
        &defaults,
        input.units,
        input.max_relative_deviation,
    )?;
    let output = Output {
        source: input.source,
        source_sha256: input.source_sha256,
        effective_cell_volume_m3: calibration.effective_cell_volume_m3,
        max_relative_deviation: calibration.max_relative_deviation,
        samples: calibration.samples,
        gases: volumes,
    };
    println!("{}", serde_json::to_string_pretty(&output).map_err(|error| error.to_string())?);
    Ok(())
}

fn main() {
    let args = env::args().collect::<Vec<_>>();
    if args.len() != 2 {
        eprintln!("usage: material-eos-calibrate <official-gas-defaults.json>");
        process::exit(2);
    }
    if let Err(error) = run(&args[1]) {
        eprintln!("calibration failed: {error}");
        process::exit(1);
    }
}
