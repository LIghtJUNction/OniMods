//! Shared-face global heat exchange when gas transport owns most thermal faces.
use super::*;

pub(super) fn try_step(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    regions: &[u32],
    groups: &mut Vec<u32>,
    dt: f32,
) -> bool {
    let Some(pool) = crate::parallel::pool(world.read.mass.len()) else {
        return false;
    };
    try_step_in_pool(world, elements, regions, groups, dt, pool)
}

fn try_step_in_pool(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    regions: &[u32],
    groups: &mut Vec<u32>,
    dt: f32,
    pool: &rayon::ThreadPool,
) -> bool {
    let count = world.read.mass.len();
    if !dt.is_finite()
        || dt <= 0.0
        || [
            world.read.element.len(),
            world.read.temperature.len(),
            world.read.properties.len(),
            world.mixture_element.len(),
            world.mixture_mass.len(),
            world.write.temperature.len(),
            regions.len(),
        ]
        .iter()
        .any(|&length| length != count)
    {
        return false;
    }
    if elements.elements.iter().any(|e| {
        !e.record.thermal_conductivity.is_finite()
            || !e.record.specific_heat_capacity.is_finite()
            || e.record.thermal_conductivity < 0.0
            || e.record.specific_heat_capacity < 0.0
    }) {
        return false;
    }
    groups.resize(count, 0);
    let environment = crate::float_env::FloatEnv::capture();
    let stats = pool.install(|| {
        groups
            .par_chunks_mut(1024)
            .enumerate()
            .map(|(chunk, groups)| {
                let _environment = environment.install();
                let mut deferred = 0;
                let mut valid = true;
                for (local, group) in groups.iter_mut().enumerate() {
                    let cell = chunk * 1024 + local;
                    let id = world.read.element[cell] as usize;
                    let gas = |id: usize| {
                        elements
                            .elements
                            .get(id)
                            .is_some_and(|e| e.record.state & 3 == 1)
                    };
                    let eligible = regions[cell] != 0
                        && world.read.properties[cell] & 1 == 0
                        && gas(id)
                        && (world.mixture_mass[cell] <= 0.0
                            || gas(world.mixture_element[cell] as usize));
                    *group = if eligible { regions[cell] } else { 0 };
                    deferred += usize::from(eligible);
                    valid &= id < elements.len()
                        && world.read.mass[cell].is_finite()
                        && world.read.temperature[cell].is_finite()
                        && world.mixture_mass[cell].is_finite();
                }
                (deferred, valid)
            })
            .reduce(|| (0, true), |a, b| (a.0 + b.0, a.1 && b.1))
    });
    // A dense solid workload retains the GPU gather. The CPU path avoids a
    // transfer/barrier when the regional gas solver owns most interior faces.
    if stats.0 < count / 2 || !stats.1 {
        return false;
    }
    let temperature = &world.read.temperature;
    let mass = &world.read.mass;
    let ids = &world.read.element;
    let groups: &[u32] = groups;
    let stride = world.stride;
    let environment = crate::float_env::FloatEnv::capture();
    let flux = |a: usize, b: usize| {
        if groups[a] != 0 && groups[a] == groups[b] {
            return 0.0;
        }
        let ma = mass[a];
        let mb = mass[b];
        if ma <= 0.0 || mb <= 0.0 {
            return 0.0;
        }
        let delta = temperature[a] - temperature[b];
        if delta.abs() < 1e-6 {
            return 0.0;
        }
        let a = &elements.elements[ids[a] as usize].record;
        let b = &elements.elements[ids[b] as usize].record;
        let capacity_a = ma * a.specific_heat_capacity;
        let capacity_b = mb * b.specific_heat_capacity;
        let conductivity = 2.0 * (a.thermal_conductivity * b.thermal_conductivity)
            / (a.thermal_conductivity + b.thermal_conductivity + 1e-6);
        let limit = capacity_a.min(capacity_b) * delta.abs() * 0.25;
        (conductivity * delta * dt).max(-limit).min(limit)
    };
    pool.install(|| {
        world
            .write
            .temperature
            .par_chunks_mut(stride)
            .enumerate()
            .for_each(|(y, row)| {
                let _environment = environment.install();
                for (x, output) in row.iter_mut().enumerate() {
                    let cell = y * stride + x;
                    let m = mass[cell];
                    let capacity = m * elements.elements[ids[cell] as usize]
                        .record
                        .specific_heat_capacity;
                    if m <= 0.0 || capacity <= 1e-6 {
                        *output = temperature[cell];
                        continue;
                    }
                    let group = groups[cell];
                    if group != 0
                        && (x == 0 || groups[cell - 1] == group)
                        && (x + 1 == stride || groups[cell + 1] == group)
                        && (y == 0 || groups[cell - stride] == group)
                        && (cell + stride >= count || groups[cell + stride] == group)
                    {
                        *output = temperature[cell].clamp(1.0, 10000.0);
                        continue;
                    }
                    let mut energy = 0.0;
                    if x > 0 {
                        energy += flux(cell - 1, cell);
                    }
                    if x + 1 < stride {
                        energy -= flux(cell, cell + 1);
                    }
                    if y > 0 {
                        energy += flux(cell - stride, cell);
                    }
                    if cell + stride < count {
                        energy -= flux(cell, cell + stride);
                    }
                    *output = (temperature[cell] + energy / capacity).clamp(1.0, 10000.0);
                }
            })
    });
    true
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sparse_face_cpu_heat_matches_gpu_gather_and_energy() {
        let (mut state, active_regions) =
            crate::native_chain_tests::fixture::create(128, 96, "mixed");
        let mut world = state.world.take().unwrap();
        world.regions = active_regions;
        world.initialize_boundary();
        let elements = state.elements.take().unwrap();
        for (cell, temperature) in world.write.temperature.iter_mut().enumerate() {
            *temperature = 300.0 + (cell % 19) as f32 * 5.0;
        }
        world.write.properties[50 * world.stride + 40] = 1;
        world.sync_read_from_write();
        crate::multicomponent_flux::refresh_active_gas_channels(&mut world, &elements);
        let count = world.read.mass.len();
        let regions = vec![1; count];
        let mut groups = Vec::new();
        let pool = rayon::ThreadPoolBuilder::new()
            .num_threads(2)
            .build()
            .unwrap();
        assert!(try_step_in_pool(
            &mut world,
            &elements,
            &regions,
            &mut groups,
            0.2,
            &pool
        ));
        let conductivity: Vec<_> = world
            .read
            .element
            .iter()
            .map(|&id| elements.elements[id as usize].record.thermal_conductivity)
            .collect();
        let heat: Vec<_> = world
            .read
            .element
            .iter()
            .map(|&id| elements.elements[id as usize].record.specific_heat_capacity)
            .collect();
        let mut gpu = onisim_gpu::pollster::block_on(ThermalDiffusionGpu::new(
            world.stride,
            world.padded_height,
        ))
        .unwrap();
        gpu.upload(
            &world.read.temperature,
            &world.read.mass,
            &conductivity,
            &heat,
        )
        .unwrap();
        gpu.upload_deferred_groups(&groups).unwrap();
        let mut expected = vec![0.0; count];
        gpu.step(0.2, 0.25, &mut expected).unwrap();
        for (cell, (&actual, &expected)) in
            world.write.temperature.iter().zip(&expected).enumerate()
        {
            assert!(
                (actual - expected).abs() <= expected.abs() * 1e-6 + 1e-5,
                "thermal cell {cell}: {actual} versus {expected}"
            );
        }
        let energy = |temperature: &[f32]| {
            temperature
                .iter()
                .zip(&world.read.mass)
                .zip(&heat)
                .map(|((&t, &m), &c)| f64::from(t) * f64::from(m) * f64::from(c))
                .sum::<f64>()
        };
        let before = energy(&world.read.temperature);
        assert!((energy(&world.write.temperature) - before).abs() / before < 1e-6);
    }
    #[test]
    fn hybrid_complete_steps_track_gpu_reference_without_species_loss() {
        use crate::simulation_step::{FIXED_DT, PreparedStep, StepSystems};
        for scene in ["regions", "mixed"] {
            let make = || {
                let (mut state, regions) =
                    crate::native_chain_tests::fixture::create(128, 96, scene);
                let mut world = state.world.take().unwrap();
                world.regions = regions;
                world.initialize_boundary();
                world.sync_read_from_write();
                (
                    world,
                    state.elements.take().unwrap(),
                    state.diseases.take().unwrap(),
                )
            };
            let (mut actual, elements, diseases) = make();
            let (mut expected, _, _) = make();
            let mut actual_gpu = GpuSimulation::new(actual.stride, actual.padded_height).unwrap();
            let mut expected_gpu =
                GpuSimulation::new(expected.stride, expected.padded_height).unwrap();
            expected_gpu.thermal_reference = true;
            let mut actual_components = (
                crate::mass_components::MassComponents::default(),
                crate::thermal_components::ThermalComponents::default(),
                crate::radiation::RadiationEmitters::default(),
            );
            let mut expected_components = (
                crate::mass_components::MassComponents::default(),
                crate::thermal_components::ThermalComponents::default(),
                crate::radiation::RadiationEmitters::default(),
            );
            let mut maximum_mass_error = 0.0f64;
            let mut maximum_temperature_error = 0.0f32;
            for tick in 0..20 {
                // Reproduce the previous full-map legacy-cache refresh in the
                // reference, while the production path retains stable mapping.
                crate::multicomponent_flux::refresh_active_gas_channels(&mut expected, &elements);
                for (world, gpu, components) in [
                    (&mut actual, &mut actual_gpu, &mut actual_components),
                    (&mut expected, &mut expected_gpu, &mut expected_components),
                ] {
                    let mut events = crate::events::EventQueues::default();
                    let mut systems = StepSystems {
                        mass: &mut components.0,
                        thermal: &mut components.1,
                        radiation: &mut components.2,
                        gpu: Some(gpu),
                    };
                    PreparedStep::new(world, &elements, &diseases, FIXED_DT)
                        .unwrap()
                        .run(&mut events, &mut systems)
                        .unwrap();
                }
                for cell in 0..actual.write.mass.len() {
                    let mut masses = std::collections::BTreeMap::<u16, (f64, f64)>::new();
                    actual.for_each_cell_species(cell, |id, mass| {
                        masses.entry(id).or_default().0 += f64::from(mass)
                    });
                    expected.for_each_cell_species(cell, |id, mass| {
                        masses.entry(id).or_default().1 += f64::from(mass)
                    });
                    for (id, (a, b)) in masses {
                        maximum_mass_error = maximum_mass_error.max((a - b).abs());
                        assert!(
                            (a - b).abs() <= b.abs().max(1.0) * 20.0 * 1e-5,
                            "{scene} tick {tick} cell {cell} species {id}: {a} vs {b}"
                        );
                    }
                    let a = actual.write.temperature[cell];
                    let b = expected.write.temperature[cell];
                    maximum_temperature_error = maximum_temperature_error.max((a - b).abs());
                    assert!(
                        (a - b).abs() <= 0.005,
                        "{scene} tick {tick} cell {cell} temperature {a} vs {b}"
                    );
                }
            }
            assert!(!actual_gpu.thermal_cpu_groups.is_empty());
            assert!(expected_gpu.thermal_cpu_groups.is_empty());
            eprintln!(
                "HYBRID_REFERENCE_ERROR scene={scene} steps=20 mass_kg={maximum_mass_error:.9} temperature_K={maximum_temperature_error:.9}"
            );
        }
    }
}
