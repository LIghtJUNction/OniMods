//! Alternating paired timings. World restore/checksums are outside each timed call.
//! These are synthetic component passes, not frame rate or whole-game measurements.
use crate::{
    baseline_flood::FloodScratch as OldFlood,
    baseline_gas_phase, baseline_thermal,
    candidate_flood::FloodScratch as NewFlood,
    candidate_gas_phase,
    candidate_thermal::{self, ThermalContactCache},
    events::*,
    fixture::*,
    tables::*,
    world::*,
};
use std::hint::black_box;
use std::time::Instant;

#[derive(Clone)]
struct Case {
    kernel: &'static str,
    size: usize,
    depth: u8,
    pattern: &'static str,
    small: bool,
    walls: bool,
}
impl Case {
    fn name(&self) -> String {
        format!(
            "{}_{}_{}_{}_{}_{}",
            self.kernel, self.size, self.depth, self.pattern, self.small, self.walls
        )
    }
}
fn cases() -> Vec<Case> {
    let mut v = Vec::new();
    for kernel in ["mass_reach", "mass_remove"] {
        for depth in [4, 16, 64] {
            for pattern in ["open", "obstacles"] {
                v.push(Case {
                    kernel,
                    size: 256,
                    depth,
                    pattern,
                    small: false,
                    walls: false,
                });
            }
        }
    }
    for kernel in ["mass_reach", "mass_remove"] {
        v.push(Case {
            kernel,
            size: 1024,
            depth: 16,
            pattern: "open",
            small: false,
            walls: false,
        });
    }
    for pattern in ["open", "obstacles"] {
        v.push(Case {
            kernel: "mass_remove",
            size: 256,
            depth: 64,
            pattern,
            small: true,
            walls: false,
        });
    }
    for pattern in ["uniform", "layered", "random", "partial", "random_partial"] {
        for walls in [false, true] {
            v.push(Case {
                kernel: "thermal",
                size: 128,
                depth: 0,
                pattern,
                small: false,
                walls,
            });
        }
    }
    for pattern in ["uniform", "layered"] {
        v.push(Case {
            kernel: "thermal",
            size: 512,
            depth: 0,
            pattern,
            small: false,
            walls: true,
        });
    }
    for size in [128, 256, 512] {
        for pattern in ["clean_balanced", "clean_flow", "germ_flow", "mixed"] {
            v.push(Case {
                kernel: "gas",
                size,
                depth: 0,
                pattern,
                small: false,
                walls: false,
            });
        }
    }
    v
}
struct Subject {
    world: PhysicsWorld,
    events: EventQueues,
    old: OldFlood,
    new: NewFlood,
    info: ConsumedMassInfo,
}
impl Subject {
    fn new(c: &Case) -> Self {
        let mut w = PhysicsWorld::new(c.size, c.size);
        let mut rng = Rng(0xfa1489a345);
        for i in 0..w.write.mass.len() {
            let (x, y) = (i % w.stride, i / w.stride);
            let border = x == 0 || y == 0 || x > w.width || y > w.height;
            w.write.element[i] = if border {
                3
            } else {
                match c.pattern {
                    "obstacles" if rng.below(100) < 30 => 3,
                    "layered" => 1 + ((y / 16) % 3) as u16,
                    "random" | "random_partial" => 1 + rng.below(31) as u16,
                    "mixed" => {
                        if rng.below(20) == 0 {
                            3
                        } else {
                            [1, 5][rng.below(2)]
                        }
                    }
                    _ => 1,
                }
            };
            w.write.mass[i] = if c.pattern == "clean_balanced" {
                1.0
            } else {
                0.5 + (rng.below(8192) as f32) * 0.000244140625
            };
            w.write.temperature[i] = 200.0 + ((x + y) % 2) as f32 * 400.0;
            w.write.insulation[i] = match c.pattern {
                "partial" => 128,
                "random_partial" => rng.next() as u8,
                _ => 255,
            };
            w.write.disease_idx[i] = if c.pattern == "germ_flow" { 0 } else { 255 };
            w.write.disease_count[i] = if c.pattern == "germ_flow" { 2000 } else { 0 };
            w.backwall_element[i] = if c.walls { 3 } else { 0 };
            w.backwall_mass[i] = if c.walls { 500.0 } else { 0.0 };
            w.backwall_temperature[i] = 350.0;
        }
        let center = (w.height / 2) * w.stride + w.width / 2;
        w.write.element[center] = 1;
        w.read.copy_from(&w.write);
        Self {
            world: w,
            events: EventQueues::default(),
            old: OldFlood::default(),
            new: NewFlood::default(),
            info: ConsumedMassInfo::default(),
        }
    }
    fn reset(&mut self, c: &Case, new: bool) {
        self.events.clear();
        self.info = ConsumedMassInfo {
            disease_idx: 255,
            ..Default::default()
        };
        if c.kernel == "mass_reach" {
            return;
        }
        if c.kernel == "mass_remove" {
            let cells = if new {
                &self.new.cells
            } else {
                &self.old.cells
            };
            for &i in cells {
                let r = &self.world.read;
                let w = &mut self.world.write;
                w.element[i] = r.element[i];
                w.mass[i] = r.mass[i];
                w.temperature[i] = r.temperature[i];
                w.disease_idx[i] = r.disease_idx[i];
                w.disease_count[i] = r.disease_count[i];
                w.disease_infestation[i] = r.disease_infestation[i];
                w.disease_growth[i] = r.disease_growth[i];
                self.world.dirty[i] = false;
                self.world.dirty_flags[i] = 0;
            }
            return;
        }
        self.world.write.copy_from(&self.world.read);
        self.world.backwall_temperature.fill(350.0);
        self.world.flow.fill([0.0; 4]);
        self.world.dirty.fill(false);
        self.world.dirty_flags.fill(0);
        self.events.clear();
        self.info = ConsumedMassInfo {
            disease_idx: 255,
            ..Default::default()
        };
    }
    fn call(&mut self, c: &Case, new: bool, table: &ElementTable, disease: &DiseaseTable) {
        match c.kernel {
            "thermal" => thermal_pass(&mut self.world, &mut self.events, table, new),
            "gas" => {
                let r = self.world.region();
                if new {
                    candidate_gas_phase::gas_phase(
                        &mut self.world,
                        table,
                        disease,
                        &mut self.events,
                        r,
                    );
                } else {
                    baseline_gas_phase::gas_phase(
                        &mut self.world,
                        table,
                        disease,
                        &mut self.events,
                        r,
                    );
                }
            }
            "mass_reach" => {
                let start = (self.world.height / 2) * self.world.stride + self.world.width / 2;
                if new {
                    success(self.new.reachable(&self.world, table, start, c.depth));
                } else {
                    success(self.old.reachable(&self.world, table, start, c.depth));
                }
            }
            "mass_remove" => {
                let start = (self.world.height / 2) * self.world.stride + self.world.width / 2;
                let request = if c.small { 0.125 } else { 1000000000.0 };
                if new {
                    success(self.new.remove_mass(
                        &mut self.world,
                        table,
                        disease,
                        &mut self.events,
                        start,
                        c.depth,
                        request,
                        1,
                        &mut self.info,
                    ));
                } else {
                    success(self.old.remove_mass(
                        &mut self.world,
                        table,
                        disease,
                        &mut self.events,
                        start,
                        c.depth,
                        request,
                        1,
                        &mut self.info,
                    ));
                }
            }
            _ => unreachable!(),
        }
        black_box(&self.world);
        black_box(&self.events);
        black_box(&self.info);
        if new {
            black_box(&self.new.cells);
        } else {
            black_box(&self.old.cells);
        }
    }
    fn sample(
        &mut self,
        c: &Case,
        new: bool,
        table: &ElementTable,
        disease: &DiseaseTable,
        min_ns: u128,
    ) -> (u128, usize) {
        let mut elapsed = 0;
        let mut n = 0;
        while elapsed < min_ns || n < 3 {
            self.reset(c, new);
            let start = Instant::now();
            self.call(c, new, table, disease);
            elapsed += start.elapsed().as_nanos();
            n += 1;
            if n >= 2000000 {
                break;
            }
        }
        (elapsed, n)
    }
}
fn thermal_pass(w: &mut PhysicsWorld, events: &mut EventQueues, table: &ElementTable, new: bool) {
    let mut cache = ThermalContactCache::new(table);
    for y in 1..=w.height {
        for x in 1..=w.width {
            let a = y * w.stride + x;
            if w.read.mass[a] > 0.0 && w.backwall_mass[a] > 0.0 {
                if new {
                    candidate_thermal::update_backwall_temperature(w, &mut cache, events, a);
                } else {
                    baseline_thermal::update_backwall_temperature(w, table, events, a);
                }
            }
            for b in [
                if x < w.width { Some(a + 1) } else { None },
                if y < w.height {
                    Some(a + w.stride)
                } else {
                    None
                },
            ]
            .into_iter()
            .flatten()
            {
                let difference = (w.read.temperature[a] - w.read.temperature[b]).abs();
                let ra = &table.elements[w.read.element[a] as usize].record;
                let rb = &table.elements[w.read.element[b] as usize].record;
                if difference >= 1.0
                    && ra.thermal_conductivity > 0.0
                    && rb.thermal_conductivity > 0.0
                    && (ra.state | rb.state) & 0x10 == 0
                {
                    if new {
                        candidate_thermal::update_temperature(w, &mut cache, a, b);
                    } else {
                        baseline_thermal::update_temperature(w, table, a, b);
                    }
                }
            }
        }
    }
}
pub fn run() {
    let _ftz = success(crate::float_env::FloatEnvGuard::enter());
    let table = elements(32);
    let disease = diseases();
    let filter = std::env::var("ONISIM_BENCH_FILTER").unwrap_or_default();
    println!("case,pair,position,variant,total_ns,iterations,ns_per_call");
    for c in cases()
        .into_iter()
        .filter(|c| filter.is_empty() || c.name().contains(&filter))
    {
        let mut a = Subject::new(&c);
        let mut b = Subject::new(&c);
        // Verify this exact timed workload before timing, including ordered events.
        a.reset(&c, false);
        b.reset(&c, true);
        a.call(&c, false, &table, &disease);
        b.call(&c, true, &table, &disease);
        same_world(&a.world, &b.world);
        same_events(&a.events, &b.events);
        same_info(a.info, b.info);
        if c.kernel.starts_with("mass") {
            assert_eq!(a.old.cells, b.new.cells);
        }
        for _ in 0..2 {
            a.reset(&c, false);
            b.reset(&c, true);
            a.call(&c, false, &table, &disease);
            b.call(&c, true, &table, &disease);
        }
        for pair in 0..8 {
            for position in 0..2 {
                let new = (pair + position) % 2 != 0;
                let target = if new { &mut b } else { &mut a };
                let (ns, n) = target.sample(&c, new, &table, &disease, 100_000_000);
                println!(
                    "{},{pair},{position},{},{ns},{n},{:.3}",
                    c.name(),
                    if new { "candidate" } else { "baseline" },
                    ns as f64 / n as f64
                );
            }
        }
        eprintln!("completed {}", c.name());
    }
}
pub fn allocations() {
    assert!(
        cfg!(feature = "allocation-count"),
        "build with --features allocation-count"
    );
    let table = elements(32);
    let disease = diseases();
    println!(
        "case,variant,stage,calls,requested_bytes,mark_capacity,queue_capacity,result_capacity"
    );
    for c in cases() {
        for new in [false, true] {
            let mut s = Subject::new(&c);
            for stage in ["cold", "warm"] {
                s.reset(&c, new);
                crate::allocation::reset();
                s.call(&c, new, &table, &disease);
                let (calls, bytes) = crate::allocation::read();
                let cap = if new {
                    s.new.capacities()
                } else {
                    s.old.capacities()
                };
                println!(
                    "{},{},{stage},{calls},{bytes},{},{},{}",
                    c.name(),
                    if new { "candidate" } else { "baseline" },
                    cap.0,
                    cap.1,
                    cap.2
                );
            }
        }
    }
}
