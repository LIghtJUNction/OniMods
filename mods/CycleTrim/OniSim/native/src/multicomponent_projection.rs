//! Publish a completed gas tensor into authoritative species and display fields.
use super::*;
use rayon::prelude::*;

pub(super) fn apply(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    staging: &mut GasStaging,
    info: &staging::GasRegion,
) -> bool {
    if let Some(pool) = crate::parallel::pool(info.width * info.height) {
        return apply_parallel(world, events, staging, info, pool);
    }
    apply_serial(world, elements, events, staging, info)
}

fn apply_serial(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    staging: &mut GasStaging,
    info: &staging::GasRegion,
) -> bool {
    let staging::GasRegion {
        min_x,
        min_y,
        width,
        height,
        channels,
        use_gpu,
        ..
    } = *info;
    let used_gpu = use_gpu;
    let packed_gpu = false;
    let GasStaging {
        local_elements,
        mass,
        temperature,
        solid,
        species,
        next_mass,
        projected,
        next_enthalpy,
        next_temperature,
        ..
    } = staging;
    let mut settled = true;
    let mut components = Vec::with_capacity(if channels > 2 { channels } else { 0 });
    for y in 0..height {
        for x in 0..width {
            let local = y * width + x;
            if solid[local] != 0 {
                continue;
            }
            let cell = (min_y + y) * world.stride + (min_x + x);
            let start = cell * GAS_CHANNELS;
            world.species_mass[start..start + GAS_CHANNELS].fill(0.0);
            if packed_gpu {
                let result = projected[local];
                let first = result.first as usize;
                let second = result.second as usize;
                let first_global = world.gas_channel_of_element[local_elements[first] as usize];
                let second_global = world.gas_channel_of_element[local_elements[second] as usize];
                if first_global != u8::MAX {
                    world.species_mass[start + first_global as usize] += result.first_mass;
                }
                if second_global != u8::MAX {
                    world.species_mass[start + second_global as usize] += result.second_mass;
                }
                if result.first_mass > 0.0 {
                    world.write.temperature[cell] = result.enthalpy.max(1.0);
                    project_gases(
                        world,
                        events,
                        cell,
                        local_elements[first],
                        result.first_mass,
                        local_elements[second],
                        result.second_mass,
                    );
                } else if world.write.mass[cell] > 0.0
                    && elements.elements[world.write.element[cell] as usize]
                        .record
                        .state
                        & 3
                        == 1
                {
                    project_gases(
                        world,
                        events,
                        cell,
                        world.vacuum_element,
                        0.0,
                        world.vacuum_element,
                        0.0,
                    );
                }
                continue;
            }
            let mut heat = 0.0f32;
            let mut heat_capacity = 0.0f32;
            for channel in 0..channels {
                let slot = local * channels + channel;
                let previous = mass[slot];
                let candidate = next_mass[slot].max(0.0);
                // Relative tolerance must not freeze trace gas or resurrect a zero result.
                let m = if (candidate - previous).abs() <= previous * 1e-5 {
                    previous
                } else {
                    settled = false;
                    candidate
                };
                next_mass[slot] = m;
                let h = if used_gpu {
                    0.0
                } else {
                    next_enthalpy[local * channels + channel].max(0.0)
                };
                let global = world.gas_channel_of_element[local_elements[channel] as usize];
                if global != u8::MAX {
                    world.species_mass[start + global as usize] = m;
                }
                if m > 0.0 {
                    heat += h;
                    heat_capacity += m * species[channel].specific_heat;
                }
            }
            if heat_capacity > 0.0 {
                let next = if used_gpu {
                    next_temperature[local].max(1.0)
                } else {
                    (heat / heat_capacity).max(1.0)
                };
                if (next - temperature[local]).abs() > 0.001 {
                    world.write.temperature[cell] = next;
                    settled = false;
                }
            }
            // Keep all local species, including those outside the legacy dense
            // channel cache. Dominance only chooses the display adapter slots.
            let empty = (world.vacuum_element, 0.0);
            let (first, second) = if channels <= 2 {
                let first_mass = next_mass[local * channels].max(0.0);
                let mut first = if first_mass > 0.0 {
                    (local_elements[0], first_mass)
                } else {
                    empty
                };
                let mut second = if channels == 2 && next_mass[local * channels + 1] > 0.0 {
                    (local_elements[1], next_mass[local * channels + 1])
                } else {
                    empty
                };
                if second.1 > first.1
                    || (second.1 > 0.0 && second.1 == first.1 && second.0 < first.0)
                {
                    std::mem::swap(&mut first, &mut second);
                }
                (first, second)
            } else {
                components.clear();
                components.extend((0..channels).filter_map(|channel| {
                    let amount = next_mass[local * channels + channel].max(0.0);
                    (amount > 0.0).then_some((local_elements[channel], amount))
                }));
                components.sort_unstable_by(|a, b| b.1.total_cmp(&a.1).then(a.0.cmp(&b.0)));
                (
                    components.first().copied().unwrap_or(empty),
                    components.get(1).copied().unwrap_or(empty),
                )
            };
            if components.len() > 2 {
                let extra = world.extra_mixtures.entry(cell).or_default();
                extra.clear();
                extra.extend_from_slice(&components[2..]);
            } else if !world.extra_mixtures.is_empty() {
                world.extra_mixtures.remove(&cell);
            }
            let public_cell = ((min_y + y - 1) * world.width + min_x + x - 1) as i32;
            project_gases_at(
                world,
                events,
                cell,
                Some(public_cell),
                first.0,
                first.1,
                second.0,
                second.1,
            );
        }
    }
    settled
}

fn apply_parallel(
    world: &mut PhysicsWorld,
    events: &mut EventQueues,
    staging: &mut GasStaging,
    info: &staging::GasRegion,
    pool: &rayon::ThreadPool,
) -> bool {
    let staging::GasRegion {
        min_x,
        min_y,
        width,
        height,
        channels,
        use_gpu,
        ..
    } = *info;
    let stride = world.stride;
    let public_width = world.width;
    let vacuum = world.vacuum_element;
    let rows = min_y * stride..(min_y + height) * stride;
    let cache_rows = rows.start * GAS_CHANNELS..rows.end * GAS_CHANNELS;
    let channel_meta: Vec<(u8, f32, u16)> = (0..channels)
        .map(|channel| {
            let element_id = staging.local_elements[channel];
            (
                world.gas_channel_of_element[element_id as usize],
                staging.species[channel].specific_heat,
                element_id,
            )
        })
        .collect();
    let mass = &staging.mass;
    let temperature = &staging.temperature;
    let next_temperature = &staging.next_temperature;
    let next_enthalpy = &staging.next_enthalpy;
    let solid = &staging.solid;
    // Dense packing leaves this sparse-mask scratch unused. Retain row-order
    // notifications here while each worker owns disjoint destination rows.
    staging.masks.resize(width * height, 0);
    let environment = crate::float_env::FloatEnv::capture();
    let (settled, extras) = pool.install(|| {
        (
            world.write.element[rows.clone()].par_chunks_exact_mut(stride),
            world.write.mass[rows.clone()].par_chunks_exact_mut(stride),
            world.write.temperature[rows.clone()].par_chunks_exact_mut(stride),
            world.mixture_element[rows.clone()].par_chunks_exact_mut(stride),
            world.mixture_mass[rows.clone()].par_chunks_exact_mut(stride),
            world.species_mass[cache_rows].par_chunks_exact_mut(stride * GAS_CHANNELS),
            world.dirty[rows.clone()].par_chunks_exact_mut(stride),
            world.dirty_flags[rows].par_chunks_exact_mut(stride),
            staging.next_mass.par_chunks_exact_mut(width * channels),
            staging.masks.par_chunks_exact_mut(width),
        )
            .into_par_iter()
            .enumerate()
            .map(
                |(
                    y,
                    (
                        ids,
                        masses,
                        temperatures,
                        mixture_ids,
                        mixture_masses,
                        cache,
                        dirty,
                        dirty_flags,
                        next,
                        changed,
                    ),
                )| {
                    let _environment = environment.install();
                    let mut settled = true;
                    let mut row_extras: Vec<(usize, Vec<(u16, f32)>)> = Vec::new();
                    let mut components: Vec<(u16, f32)> = Vec::new();
                    let row_cell_base = (min_y + y) * stride + min_x;
                    for x in 0..width {
                        changed[x] = 0;
                        let local = y * width + x;
                        if solid[local] != 0 {
                            continue;
                        }
                        let column = min_x + x;
                        let cache_at = column * GAS_CHANNELS;
                        if ids[column] != vacuum
                            || masses[column] > 0.0
                            || mixture_masses[column] > 0.0
                        {
                            cache[cache_at..cache_at + GAS_CHANNELS].fill(0.0);
                        }
                        let mut heat = 0.0f32;
                        let mut capacity = 0.0f32;
                        let empty = (vacuum, 0.0);
                        let mut first = empty;
                        let mut second = empty;
                        let mut nonzero = 0usize;
                        for (channel, &(global_ch, specific_heat, element_id)) in
                            channel_meta.iter().enumerate()
                        {
                            let slot = x * channels + channel;
                            let previous = mass[local * channels + channel];
                            let raw_next = next[slot];
                            let candidate = raw_next.max(0.0);
                            if previous == 0.0 && candidate == 0.0 {
                                if raw_next != 0.0 {
                                    next[slot] = 0.0;
                                }
                                continue;
                            }
                            let amount =
                                if (candidate - previous).abs() <= previous * 1e-5 {
                                    previous
                                } else {
                                    settled = false;
                                    candidate
                                };
                            if raw_next != amount {
                                next[slot] = amount;
                            }
                            if global_ch != u8::MAX && amount > 0.0 {
                                cache[cache_at + global_ch as usize] = amount;
                            }
                            if amount > 0.0 {
                                if !use_gpu {
                                    heat += next_enthalpy[local * channels + channel].max(0.0);
                                }
                                capacity += amount * specific_heat;
                                nonzero += 1;
                                if nonzero == 1 {
                                    first = (element_id, amount);
                                } else if nonzero == 2 {
                                    second = (element_id, amount);
                                }
                            }
                        }
                        if capacity > 0.0 {
                            let next_temp = if use_gpu {
                                next_temperature[local].max(1.0)
                            } else {
                                (heat / capacity).max(1.0)
                            };
                            if (next_temp - temperature[local]).abs() > 0.001 {
                                temperatures[column] = next_temp;
                                settled = false;
                            }
                        }
                        if nonzero == 2 {
                            if second.1 > first.1
                                || (second.1 > 0.0 && second.1 == first.1 && second.0 < first.0)
                            {
                                std::mem::swap(&mut first, &mut second);
                            }
                        } else if nonzero > 2 {
                            components.clear();
                            for (channel, &(_, _, element_id)) in channel_meta.iter().enumerate() {
                                let amount = next[x * channels + channel];
                                if amount > 0.0 {
                                    components.push((element_id, amount));
                                }
                            }
                            components.sort_unstable_by(|a, b| {
                                b.1.total_cmp(&a.1).then(a.0.cmp(&b.0))
                            });
                            first = components[0];
                            second = components[1];
                            row_extras.push((row_cell_base + x, components[2..].to_vec()));
                        }
                        let (primary_element, primary_mass, mix_element, mix_mass) =
                            if first.1 == 0.0 {
                                if second.1 > 0.0 {
                                    (second.0, second.1, vacuum, 0.0)
                                } else {
                                    (vacuum, 0.0, vacuum, 0.0)
                                }
                            } else {
                                (first.0, first.1, second.0, second.1)
                            };
                        let mix_element = if mix_mass > 0.0 { mix_element } else { vacuum };
                        let notify = ids[column] != primary_element
                            || (masses[column] - primary_mass).abs() > 1e-4
                            || mixture_ids[column] != mix_element
                            || (mixture_masses[column] - mix_mass).abs() > 1e-4;
                        ids[column] = primary_element;
                        masses[column] = primary_mass;
                        mixture_ids[column] = mix_element;
                        mixture_masses[column] = mix_mass;
                        if notify {
                            dirty[column] = true;
                            dirty_flags[column] |= 0x1f;
                            changed[x] = 1;
                        }
                    }
                    (settled, row_extras)
                },
            )
            .reduce(
                || (true, Vec::new()),
                |(s1, mut e1), (s2, mut e2)| {
                    if e1.is_empty() {
                        (s1 && s2, e2)
                    } else if e2.is_empty() {
                        (s1 && s2, e1)
                    } else {
                        e1.append(&mut e2);
                        (s1 && s2, e1)
                    }
                },
            )
    });
    if !world.extra_mixtures.is_empty() {
        world.extra_mixtures.retain(|&cell, _| {
            let x = cell % stride;
            let y = cell / stride;
            x < min_x
                || x >= min_x + width
                || y < min_y
                || y >= min_y + height
                || solid[(y - min_y) * width + x - min_x] != 0
        });
    }
    for (cell, extra) in extras {
        world.extra_mixtures.insert(cell, extra);
    }
    for (y, row) in staging.masks.chunks_exact(width).enumerate() {
        let base = (min_y + y - 1) * public_width + min_x - 1;
        for (x, &changed) in row.iter().enumerate() {
            if changed != 0 {
                events.substance_change((base + x) as i32);
            }
        }
    }
    settled
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn trace_gas_changes_and_depletion_survive_serial_and_parallel_projection() {
        let pool = rayon::ThreadPoolBuilder::new().num_threads(2).build().unwrap();
        for (previous, candidate) in [(1e-12_f32, 0.0_f32), (0.0, 1e-12), (1e-12, 5e-13)] {
            for parallel in [false, true] {
                let (mut state, regions) = crate::native_chain_tests::fixture::create(96, 96, "mixed");
                let mut world = state.world.take().unwrap();
                let elements = state.elements.take().unwrap();
                let mut staging = GasStaging::default();
                let info = staging.prepare(&mut world, &elements, regions[0], true, 0).unwrap();
                assert_eq!(info.channels, 2);
                let local = staging.solid.iter().position(|&solid| solid == 0).unwrap();
                let cell = (info.min_y + local / info.width) * world.stride + info.min_x + local % info.width;
                staging.mass.fill(previous);
                staging.next_mass.resize(staging.mass.len(), candidate);
                staging.next_mass.fill(candidate);
                staging.next_temperature.clone_from(&staging.temperature);
                let mut events = EventQueues::default();
                if parallel {
                    apply_parallel(&mut world, &mut events, &mut staging, &info, &pool);
                } else {
                    apply_serial(&mut world, &elements, &mut events, &mut staging, &info);
                }
                assert_eq!(staging.next_mass[local * info.channels].to_bits(), candidate.to_bits());
                assert_eq!(world.write.mass[cell].to_bits(), candidate.to_bits());
                if candidate == 0.0 {
                    assert_eq!(world.write.element[cell], world.vacuum_element);
                    assert_eq!(world.mixture_mass[cell], 0.0);
                }
            }
        }
    }

    #[test]
    fn parallel_projection_matches_serial_rows_events_and_blocked_mixtures() {
        let make = || {
            let (mut state, regions) = crate::native_chain_tests::fixture::create(96, 96, "mixed");
            let mut world = state.world.take().unwrap();
            world
                .extra_mixtures
                .insert(50 * world.stride + 40, vec![(3, 0.25)]);
            world
                .extra_mixtures
                .insert(3 * world.stride + 3, vec![(3, 0.25)]);
            let elements = state.elements.take().unwrap();
            let mut staging = GasStaging::default();
            let info = staging
                .prepare(&mut world, &elements, regions[0], true, 0)
                .unwrap();
            assert_eq!(info.channels, 2);
            staging.next_mass.clone_from(&staging.mass);
            for (index, mass) in staging.next_mass.iter_mut().enumerate() {
                *mass *= if index % 5 == 0 { 1.000001 } else { 0.75 };
            }
            staging.next_temperature.clone_from(&staging.temperature);
            for (index, temperature) in staging.next_temperature.iter_mut().enumerate() {
                *temperature += if index % 7 == 0 { 0.0001 } else { 0.5 };
            }
            (world, elements, staging, info)
        };
        let (mut expected, elements, mut serial, info) = make();
        let (mut actual, _, mut parallel, _) = make();
        let mut expected_events = EventQueues::default();
        let mut actual_events = EventQueues::default();
        let settled = apply_serial(
            &mut expected,
            &elements,
            &mut expected_events,
            &mut serial,
            &info,
        );
        let pool = rayon::ThreadPoolBuilder::new()
            .num_threads(2)
            .build()
            .unwrap();
        assert_eq!(
            settled,
            apply_parallel(&mut actual, &mut actual_events, &mut parallel, &info, &pool)
        );
        assert_eq!(
            crate::native_chain_tests::snapshot::world(&actual).0,
            crate::native_chain_tests::snapshot::world(&expected).0
        );
        assert_eq!(
            crate::native_chain_tests::snapshot::events(&actual_events).0,
            crate::native_chain_tests::snapshot::events(&expected_events).0
        );
    }

    #[test]
    fn parallel_projection_matches_serial_for_many_species_and_extra_mixtures() {
        let make = |use_gpu: bool| {
            let mut descriptions = vec![(0x2d39_bf75_u32, 0), (0xa936_0b34, 0), (0x6d95_058c, 3)];
            descriptions.extend((0..8).map(|id| (101 + id as u32, 1)));
            let mut bytes = (descriptions.len() as i32).to_le_bytes().to_vec();
            for (index, (hash, phase)) in descriptions.into_iter().enumerate() {
                let mut raw = [0u8; 164];
                raw[..4].copy_from_slice(&hash.to_le_bytes());
                raw[8..10].copy_from_slice(&(index as u16).to_le_bytes());
                raw[10] = phase;
                raw[12..16].copy_from_slice(&(1.0f32 + index as f32 * 0.1).to_le_bytes());
                bytes.extend_from_slice(&raw);
            }
            bytes.resize(bytes.len() + 11 * 4, 0);
            let elements = ElementTable::parse(&bytes).unwrap();
            let mut world = PhysicsWorld::new(96, 64, 7, &elements).unwrap();
            for y in 1..=64 {
                for x in 1..=96 {
                    let cell = y * world.stride + x;
                    if (x + y) % 13 == 0 {
                        world.write.element[cell] = 2;
                        world.write.properties[cell] = 1;
                        world.write.mass[cell] = 100.0;
                    } else {
                        let primary = 3 + ((x + y) % 8) as u16;
                        let secondary = 3 + ((x + y + 1) % 8) as u16;
                        world.write.element[cell] = primary;
                        world.write.mass[cell] = 1.0 + (x % 5) as f32 * 0.2;
                        world.mixture_element[cell] = secondary;
                        world.mixture_mass[cell] = 0.5 + (y % 4) as f32 * 0.1;
                        if (x * y) % 7 == 0 {
                            world.extra_mixtures.insert(cell, vec![(3 + ((x + 3) % 8) as u16, 0.25)]);
                        }
                    }
                    world.write.temperature[cell] = 290.0 + (x % 17) as f32;
                }
            }
            let region = Region { min_x: 1, min_y: 1, max_x: 97, max_y: 65, ..Default::default() };
            let mut staging = GasStaging::default();
            let info = staging.prepare(&mut world, &elements, region, use_gpu, 0).unwrap();
            assert_eq!(info.channels, 8);
            staging.next_mass.resize(staging.mass.len(), 0.0);
            for (index, &m) in staging.mass.iter().enumerate() {
                staging.next_mass[index] = m * if index % 5 == 0 { 1.000001 } else { 0.8 };
            }
            if use_gpu {
                staging.next_temperature.clone_from(&staging.temperature);
                for (index, temperature) in staging.next_temperature.iter_mut().enumerate() {
                    *temperature += if index % 7 == 0 { 0.0001 } else { 0.75 };
                }
            } else {
                staging.next_enthalpy.clone_from(&staging.enthalpy);
                for h in staging.next_enthalpy.iter_mut() {
                    *h *= 1.01;
                }
            }
            (world, elements, staging, info)
        };
        let pool = rayon::ThreadPoolBuilder::new().num_threads(4).build().unwrap();
        for use_gpu in [false, true] {
            let (mut expected, elements, mut serial, info) = make(use_gpu);
            let (mut actual, _, mut parallel, _) = make(use_gpu);
            let mut expected_events = EventQueues::default();
            let mut actual_events = EventQueues::default();
            assert_eq!(
                apply_serial(&mut expected, &elements, &mut expected_events, &mut serial, &info),
                apply_parallel(&mut actual, &mut actual_events, &mut parallel, &info, &pool)
            );
            assert_eq!(
                crate::native_chain_tests::snapshot::world(&actual).0,
                crate::native_chain_tests::snapshot::world(&expected).0
            );
            assert_eq!(
                crate::native_chain_tests::snapshot::events(&actual_events).0,
                crate::native_chain_tests::snapshot::events(&expected_events).0
            );
        }
    }

    #[test]
    #[ignore = "manual multi-species projection ABBA benchmark"]
    fn benchmark_multichannel_projection() {
        let mut descriptions = vec![(0x2d39_bf75_u32, 0), (0xa936_0b34, 0), (0x6d95_058c, 3)];
        descriptions.extend((0..8).map(|id| (101 + id as u32, 1)));
        let mut bytes = (descriptions.len() as i32).to_le_bytes().to_vec();
        for (index, (hash, phase)) in descriptions.into_iter().enumerate() {
            let mut raw = [0u8; 164];
            raw[..4].copy_from_slice(&hash.to_le_bytes());
            raw[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            raw[10] = phase;
            raw[12..16].copy_from_slice(&(1.0f32 + index as f32 * 0.1).to_le_bytes());
            bytes.extend_from_slice(&raw);
        }
        bytes.resize(bytes.len() + 11 * 4, 0);
        let elements = ElementTable::parse(&bytes).unwrap();
        let make = || {
            let mut world = PhysicsWorld::new(512, 448, 7, &elements).unwrap();
            for y in 1..=448 {
                for x in 1..=512 {
                    let cell = y * world.stride + x;
                    let primary = 3 + ((x + y) % 8) as u16;
                    let secondary = 3 + ((x + y + 1) % 8) as u16;
                    world.write.element[cell] = primary;
                    world.write.mass[cell] = 1.0;
                    world.mixture_element[cell] = secondary;
                    world.mixture_mass[cell] = 0.4;
                    world.write.temperature[cell] = 295.0;
                }
            }
            let region = Region { min_x: 1, min_y: 1, max_x: 513, max_y: 449, ..Default::default() };
            let mut staging = GasStaging::default();
            let info = staging.prepare(&mut world, &elements, region, true, 0).unwrap();
            staging.next_mass.clone_from(&staging.mass);
            for (index, m) in staging.next_mass.iter_mut().enumerate() {
                *m *= 0.9;
                if index % 16 == 0 { *m += 0.05; }
            }
            staging.next_temperature.clone_from(&staging.temperature);
            for t in &mut staging.next_temperature { *t += 0.2; }
            (world, staging, info)
        };
        let pool = rayon::ThreadPoolBuilder::new().num_threads(8).build().unwrap();
        let mut serial_ms = Vec::new();
        let mut parallel_ms = Vec::new();
        for iter in 0..40 {
            for is_parallel in if iter % 2 == 0 { [false, true] } else { [true, false] } {
                let (mut world, mut staging, info) = make();
                let mut events = EventQueues::default();
                let t0 = std::time::Instant::now();
                if is_parallel {
                    apply_parallel(&mut world, &mut events, &mut staging, &info, &pool);
                } else {
                    apply_serial(&mut world, &elements, &mut events, &mut staging, &info);
                }
                let dt = t0.elapsed().as_secs_f64() * 1000.0;
                if iter >= 8 {
                    if is_parallel { parallel_ms.push(dt); } else { serial_ms.push(dt); }
                }
            }
        }
        serial_ms.sort_by(f64::total_cmp);
        parallel_ms.sort_by(f64::total_cmp);
        println!("PROJECTION_8CH_ABBA,serial_median_ms={:.3},parallel_median_ms={:.3},speedup={:.2}x",
            serial_ms[serial_ms.len() / 2], parallel_ms[parallel_ms.len() / 2],
            serial_ms[serial_ms.len() / 2] / parallel_ms[parallel_ms.len() / 2]);
    }
}
