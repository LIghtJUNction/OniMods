//! Execute the actual archived and candidate disease emitter implementations.
#![allow(dead_code)]

#[rustfmt::skip]
#[path = "../../baseline/disease_emitters.rs"]
mod baseline;
#[path = "disease_helpers.rs"]
mod archived_helpers;
#[path = "../../../native/src/disease_emitters.rs"]
mod candidate;
#[path = "../../../native/src/diamond.rs"]
mod diamond;
#[path = "../../../native/src/disease_fields.rs"]
mod disease_fields;
mod fluid_helpers {
    pub(crate) use crate::archived_helpers::*;
    pub(crate) use crate::disease_fields::add_disease_fields;
}
#[path = "../../../native/src/float_env.rs"]
mod float_env;
#[path = "../../../native/src/parallel.rs"]
mod parallel;
#[rustfmt::skip]
#[path = "../../baseline/numeric.rs"]
mod numeric;
#[rustfmt::skip]
#[path = "../../baseline/status.rs"]
mod status;
#[rustfmt::skip]
#[path = "../../baseline/tables.rs"]
mod tables;
#[rustfmt::skip]
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
    if cfg!(feature = "allocation_stats") && matches!(command, "benchmark" | "benchmark-focused") {
        eprintln!("never time the counting allocator build");
        std::process::exit(2);
    }
    match command {
        "verify" => verify::run(output.expect("verify requires a JSON output path")),
        "benchmark-focused" => {
            benchmark::focused(output.expect("benchmark-focused requires CSV path"))
        }
        "benchmark" => {
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
