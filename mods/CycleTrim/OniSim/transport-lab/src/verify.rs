use crate::{
    baseline_flood::FloodScratch as OldFlood,
    baseline_gas, baseline_gas_phase, baseline_thermal,
    candidate_flood::FloodScratch as NewFlood,
    candidate_gas, candidate_gas_phase,
    candidate_thermal::{self, ThermalContactCache},
    events::*,
    fixture::*,
    world::*,
};

fn thermal() -> u64 {
    let mut table = elements(32);
    let mut rng = Rng(0x74a22fa3079);
    let mut a = PhysicsWorld::new(2, 2);
    populate(&mut a, &mut rng, 32, false);
    let mut b = a.clone();
    let mut count = 0;
    // Every insulation pair, including the 254/255 logarithm branch boundary.
    {
        for ea in [1, 2, 3, 5] {
            for eb in [1, 2, 3, 7] {
                for ia in 0..=255 {
                    for ib in 0..=255 {
                        for w in [&mut a, &mut b] {
                            w.read.element[5] = ea;
                            w.read.element[6] = eb;
                            w.read.insulation[5] = ia;
                            w.read.insulation[6] = ib;
                            w.read.temperature[5] = 173.25;
                            w.read.temperature[6] = 900.125;
                            w.read.mass[5] = 0.6;
                            w.read.mass[6] = 42.123;
                            w.write.temperature[5] = 178.25;
                            w.write.temperature[6] = 899.75;
                        }
                        // A fresh probe for every key, then a repeat: test both the actual
                        // cache fill and hit paths rather than only an adaptive bypass.
                        let mut cache = ThermalContactCache::new(&table);
                        for _ in 0..2 {
                            baseline_thermal::update_temperature(&mut a, &table, 5, 6);
                            candidate_thermal::update_temperature(&mut b, &mut cache, 5, 6);
                            bits(
                                a.write.temperature[5],
                                b.write.temperature[5],
                                "all insulation A",
                            );
                            bits(
                                a.write.temperature[6],
                                b.write.temperature[6],
                                "all insulation B",
                            );
                            count += 1;
                        }
                    }
                }
            }
        }
    }
    for scenario in 0..12000 {
        // New table borrow each case exercises invalidation on actual table change.
        if scenario % 7 == 0 {
            let i = rng.below(table.len());
            table.elements[i].record.thermal_conductivity = rng.special();
            table.elements[i].record.specific_heat_capacity = rng.special();
        }
        populate(&mut a, &mut rng, 32, scenario % 2 == 0);
        b = a.clone();
        let mut ea = EventQueues::default();
        let mut eb = EventQueues::default();
        let mut cache = ThermalContactCache::new(&table);
        for _ in 0..12 {
            let x = rng.below(a.read.mass.len());
            let y = rng.below(a.read.mass.len());
            baseline_thermal::update_temperature(&mut a, &table, x, y);
            candidate_thermal::update_temperature(&mut b, &mut cache, x, y);
            baseline_thermal::update_backwall_temperature(&mut a, &table, &mut ea, x);
            candidate_thermal::update_backwall_temperature(&mut b, &mut cache, &mut eb, x);
            count += 2;
        }
        same_world(&a, &b);
        same_events(&ea, &eb);
    }
    count
}
fn gas() -> u64 {
    let table = elements(32);
    let diseases = diseases();
    let mut rng = Rng(0x5a89934de);
    let mut a = PhysicsWorld::new(2, 2);
    let mut count = 0;
    for case in 0..80000 {
        populate(&mut a, &mut rng, 32, case % 2 == 0);
        if case % 3 == 0 {
            a.read.disease_count.fill(0);
        }
        let mut b = a.clone();
        let mut ea = EventQueues::default();
        let mut eb = EventQueues::default();
        for _ in 0..8 {
            let x = rng.below(a.write.mass.len());
            let y = rng.below(a.write.mass.len());
            let el = a.read.element[x];
            let phase = table.elements[el as usize].record.state & 3;
            let coefficient = if case % 2 == 0 { rng.special() } else { 0.2 };
            let fa = baseline_gas::update_pressure(
                &mut a,
                &table,
                &diseases,
                &mut ea,
                x,
                el,
                phase,
                coefficient,
                y,
            );
            let fb = candidate_gas::update_pressure(
                &mut b,
                &table,
                &diseases,
                &mut eb,
                x,
                el,
                phase,
                coefficient,
                y,
            );
            bits(fa, fb, "signed pressure flow");
            count += 1;
        }
        same_world(&a, &b);
        same_events(&ea, &eb);
    }
    // Actual ordered gas pass includes diagonal pressure, displacements and flow slots.
    for case in 0..160 {
        let mut a = PhysicsWorld::new(4 + rng.below(32), 4 + rng.below(24));
        populate(&mut a, &mut rng, 8, false);
        if case % 2 == 0 {
            a.read.disease_count.fill(0);
            a.write.disease_count.fill(0);
        }
        let mut b = a.clone();
        let mut ea = EventQueues::default();
        let mut eb = EventQueues::default();
        for step in 0..30 {
            let region = a.region();
            a.scan_direction = if step % 2 == 0 { 1 } else { -1 };
            b.scan_direction = a.scan_direction;
            baseline_gas_phase::gas_phase(&mut a, &table, &diseases, &mut ea, region);
            candidate_gas_phase::gas_phase(&mut b, &table, &diseases, &mut eb, region);
            same_world(&a, &b);
            same_events(&ea, &eb);
            a.read.copy_from(&a.write);
            b.read.copy_from(&b.write);
            count += 1;
            ea.clear();
            eb.clear();
        }
    }
    count
}
fn mass() -> u64 {
    let table = elements(8);
    let diseases = diseases();
    let mut rng = Rng(0xe76083683);
    let mut count = 0;
    let mut original = PhysicsWorld::new(3, 3);
    populate(&mut original, &mut rng, 8, false);
    let mut old = OldFlood::default();
    let mut new = NewFlood::default();
    for mask in 0..512 {
        for y in 1..=3 {
            for x in 1..=3 {
                let i = y * 5 + x;
                original.write.element[i] = if mask & (1 << ((y - 1) * 3 + x - 1)) != 0 {
                    3
                } else {
                    1
                };
            }
        }
        for start in 0..25 {
            for depth in 0..7 {
                success(old.reachable(&original, &table, start, depth));
                success(new.reachable(&original, &table, start, depth));
                assert_eq!(
                    old.cells, new.cells,
                    "FIFO reach mask={mask}, start={start}, depth={depth}"
                );
                count += 1;
            }
        }
    }
    for case in 0..7000 {
        let width = 1 + rng.below(40);
        let height = 1 + rng.below(40);
        let mut a = PhysicsWorld::new(width, height);
        populate(&mut a, &mut rng, 8, case % 7 == 0);
        let mut b = a.clone();
        let mut ea = EventQueues::default();
        let mut eb = EventQueues::default();
        for step in 0..5 {
            let start = rng.below(a.write.mass.len());
            let depth = [0, 1, 2, 4, 16, 64, 255][rng.below(7)];
            let target = rng.below(5) as u16;
            let request = if case % 7 == 0 {
                rng.special()
            } else {
                [0.0, 0.125, 1.0, 50.0, 1000000.0][rng.below(5)]
            };
            let mut ia = ConsumedMassInfo {
                disease_idx: 255,
                ..Default::default()
            };
            let mut ib = ia;
            if step == 3 && case % 5 == 0 {
                old.force_wrap();
                new.force_wrap();
            }
            success(old.remove_mass(
                &mut a, &table, &diseases, &mut ea, start, depth, request, target, &mut ia,
            ));
            success(new.remove_mass(
                &mut b, &table, &diseases, &mut eb, start, depth, request, target, &mut ib,
            ));
            same_world(&a, &b);
            same_events(&ea, &eb);
            same_info(ia, ib);
            assert_eq!(old.cells, new.cells, "FIFO remove case={case}, step={step}");
            count += 1;
        }
    }
    for (width, height) in [(65535, 2), (2, 65535)] {
        let mut a = PhysicsWorld::new(width, height);
        populate(&mut a, &mut rng, 5, false);
        a.write.element.fill(1);
        for start in [
            0,
            1,
            a.stride - 1,
            a.stride + 1,
            a.write.mass.len() - 1,
            usize::MAX,
        ] {
            for depth in [0, 1, 2, 16, 255] {
                success(old.reachable(&a, &table, start, depth));
                success(new.reachable(&a, &table, start, depth));
                assert_eq!(old.cells, new.cells, "u16 edge");
                count += 1;
            }
        }
    }
    count
}
fn ratio() -> u64 {
    // Every sign/exponent with a spread of fraction bits; includes all exceptional classes.
    let mut checked = 0;
    for se in 0u32..512 {
        for fraction in (0u32..=0x7fffff).step_by(127) {
            let q = f32::from_bits((se << 23) | fraction);
            let f = crate::numeric::smax(crate::numeric::smin(q, 1.0), 0.0);
            assert!(f.is_finite() && f >= 0.0 && f <= 1.0);
            assert_eq!(crate::numeric::cvtt_f32_i32(0.0 * f), 0);
            checked += 1;
        }
    }
    checked
}
pub fn run() {
    let mut all = [0u64; 4];
    for ftz in [false, true] {
        let _guard = if ftz {
            Some(success(crate::float_env::FloatEnvGuard::enter()))
        } else {
            None
        };
        let counts = [thermal(), gas(), mass(), ratio()];
        for i in 0..4 {
            all[i] += counts[i];
        }
        println!("{{\"ftz\":{ftz},\"thermal_calls\":{},\"gas_calls_and_passes\":{},\"mass_searches\":{},\"ratio_patterns\":{},\"passed\":true}}",counts[0],counts[1],counts[2],counts[3]);
    }
    println!("{{\"total_thermal\":{},\"total_gas\":{},\"total_mass\":{},\"total_ratio\":{},\"passed\":true}}",all[0],all[1],all[2],all[3]);
}
