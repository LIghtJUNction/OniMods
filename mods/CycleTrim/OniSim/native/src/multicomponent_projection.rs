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
    if info.use_gpu
        && info.channels <= 2
        && let Some(pool) = crate::parallel::pool(info.width * info.height)
    {
        return apply_two_channels(world, events, staging, info, pool);
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
                let m = if (candidate - previous).abs() <= 1e-7f32.max(previous * 1e-5) {
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
            } else {
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

fn apply_two_channels(
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
        ..
    } = *info;
    let stride = world.stride;
    let public_width = world.width;
    let vacuum = world.vacuum_element;
    let rows = min_y * stride..(min_y + height) * stride;
    let cache_rows = rows.start * GAS_CHANNELS..rows.end * GAS_CHANNELS;
    let first_id = staging.local_elements[0];
    let second_id = if channels == 2 {
        staging.local_elements[1]
    } else {
        vacuum
    };
    let global = [
        world.gas_channel_of_element[first_id as usize],
        world.gas_channel_of_element[second_id as usize],
    ];
    let heat = [
        staging.species[0].specific_heat,
        if channels == 2 {
            staging.species[1].specific_heat
        } else {
            0.0
        },
    ];
    let mass = &staging.mass;
    let temperature = &staging.temperature;
    let next_temperature = &staging.next_temperature;
    let solid = &staging.solid;
    // Dense packing leaves this sparse-mask scratch unused. Retain row-order
    // notifications here while each worker owns disjoint destination rows.
    staging.masks.resize(width * height, 0);
    let environment = crate::float_env::FloatEnv::capture();
    let settled = pool.install(|| {
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
                    for x in 0..width {
                        changed[x] = 0;
                        let local = y * width + x;
                        if solid[local] != 0 {
                            continue;
                        }
                        let column = min_x + x;
                        let cache_at = column * GAS_CHANNELS;
                        cache[cache_at..cache_at + GAS_CHANNELS].fill(0.0);
                        let mut capacity = 0.0;
                        for channel in 0..channels {
                            let slot = x * channels + channel;
                            let previous = mass[local * channels + channel];
                            let candidate = next[slot].max(0.0);
                            let amount =
                                if (candidate - previous).abs() <= 1e-7f32.max(previous * 1e-5) {
                                    previous
                                } else {
                                    settled = false;
                                    candidate
                                };
                            next[slot] = amount;
                            if global[channel] != u8::MAX {
                                cache[cache_at + global[channel] as usize] = amount;
                            }
                            if amount > 0.0 {
                                capacity += amount * heat[channel];
                            }
                        }
                        if capacity > 0.0 {
                            let next = next_temperature[local].max(1.0);
                            if (next - temperature[local]).abs() > 0.001 {
                                temperatures[column] = next;
                                settled = false;
                            }
                        }
                        let empty = (vacuum, 0.0);
                        let first_mass = next[x * channels].max(0.0);
                        let mut first = if first_mass > 0.0 {
                            (first_id, first_mass)
                        } else {
                            empty
                        };
                        let mut second = if channels == 2 && next[x * channels + 1] > 0.0 {
                            (second_id, next[x * channels + 1])
                        } else {
                            empty
                        };
                        if second.1 > first.1
                            || (second.1 > 0.0 && second.1 == first.1 && second.0 < first.0)
                        {
                            std::mem::swap(&mut first, &mut second);
                        }
                        let notify = ids[column] != first.0
                            || (masses[column] - first.1).abs() > 1e-4
                            || mixture_ids[column] != second.0
                            || (mixture_masses[column] - second.1).abs() > 1e-4;
                        ids[column] = first.0;
                        masses[column] = first.1;
                        mixture_ids[column] = second.0;
                        mixture_masses[column] = second.1;
                        if notify {
                            dirty[column] = true;
                            dirty_flags[column] |= 0x1f;
                            changed[x] = 1;
                        }
                    }
                    settled
                },
            )
            .reduce(|| true, |a, b| a && b)
    });
    world.extra_mixtures.retain(|&cell, _| {
        let x = cell % stride;
        let y = cell / stride;
        x < min_x
            || x >= min_x + width
            || y < min_y
            || y >= min_y + height
            || solid[(y - min_y) * width + x - min_x] != 0
    });
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
            apply_two_channels(&mut actual, &mut actual_events, &mut parallel, &info, &pool)
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
