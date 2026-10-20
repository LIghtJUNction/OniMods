//! Execute the actual archived and candidate disease emitter implementations.
#![allow(dead_code)]

#[path = "../../baseline/disease_emitters.rs"]
mod baseline;
#[path = "../../../native/src/disease_emitters.rs"]
mod candidate;
#[path = "../../../native/src/diamond.rs"]
mod diamond;
#[path = "disease_helpers.rs"]
mod fluid_helpers;
#[path = "../../baseline/numeric.rs"]
mod numeric;
#[path = "../../baseline/status.rs"]
mod status;
#[path = "../../baseline/tables.rs"]
mod tables;
#[path = "../../baseline/world_cells.rs"]
mod world_cells;

#[cfg(feature = "allocation_stats")]
mod allocation;
mod benchmark;
mod shared;
mod verify;
mod world;

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let command = args.get(1).map(String::as_str).unwrap_or("help");
    let output = args.get(2).map(String::as_str);
    match command {
        "verify" => verify::run(output.expect("verify requires a JSON output path")),
        "benchmark" => {
            assert!(!cfg!(feature = "allocation_stats"), "never time the counting allocator build");
            benchmark::run(output.expect("benchmark requires a CSV output path"));
        }
        #[cfg(feature = "allocation_stats")]
        "allocations" => allocation::run(output.expect("allocations requires a CSV output path")),
        _ => {
            eprintln!("usage: onisim-diamond-validation verify|benchmark|allocations OUTPUT");
            std::process::exit(2);
        }
    }
}
