#![allow(dead_code)]
mod allocation;
#[path = "generated/baseline_mass_components_flood.rs"]
mod baseline_flood;
#[path = "generated/baseline_gas.rs"]
mod baseline_gas;
#[path = "generated/baseline_gas_phase.rs"]
mod baseline_gas_phase;
#[path = "generated/baseline_thermal.rs"]
mod baseline_thermal;
mod benchmark;
#[path = "generated/candidate_mass_components_flood.rs"]
mod candidate_flood;
#[path = "generated/candidate_gas.rs"]
mod candidate_gas;
#[path = "generated/candidate_gas_phase.rs"]
mod candidate_gas_phase;
#[path = "generated/candidate_thermal.rs"]
mod candidate_thermal;
#[path = "../baseline/event_records.rs"]
mod event_records;
mod events;
mod fixture;
#[path = "../baseline/float_env.rs"]
mod float_env;
#[path = "../baseline/fluid_helpers.rs"]
mod fluid_helpers;
#[path = "../baseline/numeric.rs"]
mod numeric;
#[path = "../baseline/status.rs"]
mod status;
#[path = "../baseline/tables.rs"]
mod tables;
mod verify;
mod world;
#[path = "../baseline/world_cells.rs"]
mod world_cells;

fn allocation(_: std::collections::TryReserveError) -> status::Error {
    status::Error::new(
        status::Status::ResourceLimit,
        "could not allocate mass-component storage",
    )
}

fn main() {
    let mode = std::env::args()
        .nth(1)
        .unwrap_or_else(|| "verify".to_owned());
    match mode.as_str() {
        "verify" => verify::run(),
        "bench" => benchmark::run(),
        "alloc" => benchmark::allocations(),
        _ => panic!("usage: onisim-transport-lab [verify|bench|alloc]"),
    }
}
