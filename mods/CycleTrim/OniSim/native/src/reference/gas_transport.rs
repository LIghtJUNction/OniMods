//! Frozen transport from 23882842, used only as a CPU bitwise and timing oracle.
use super::*;

#[inline(always)]
pub(super) fn transport<const SPARSE: bool>(
    mass_output: &mut [f32],
    thermal_output: &mut [ThermalCell],
    job: &GasBatchJob<'_>,
    source: (&[f32], &[f32]),
    velocity: &[[f32; 2]],
    dt: f32,
    pool: &rayon::ThreadPool,
) {
    let (mass, enthalpy) = source;
    let (width, height, channels) = job.dimensions;
    let count = width * height;
    let rows = width * 2;
    let minimum_chunks = if count < 8192 {
        count.div_ceil(rows)
    } else {
        1
    };
    let environment = crate::float_env::FloatEnv::capture();
    let solid = job.state.solid;
    let flux = |a: f32, b: f32, speed: f32| {
        let cap = 1.0f32.min(0.125 / dt);
        let speed = speed.clamp(-cap, cap);
        let delta = if (a - b).abs() <= 1e-7f32.max(a.max(b) * 1e-5) {
            0.0
        } else {
            a - b
        };
        speed * if speed >= 0.0 { a } else { b } + 2.0f32.min(0.125 / dt) * delta
    };
    pool.install(|| {
        mass_output
            .par_chunks_mut(rows * channels)
            .with_min_len(minimum_chunks)
            .zip(thermal_output.par_chunks_mut(rows))
            .enumerate()
            .for_each(|(chunk, (output, thermal))| {
                let _environment = environment.install();
                let coordinates = cell_coordinates(width, chunk * rows, output.len() / channels);
                for ((output, thermal), (cell, x, y)) in
                    output.chunks_mut(channels).zip(thermal).zip(coordinates)
                {
                    let adjacent = neighbors_at(cell, x, y, width, height);
                    let mut total_mass = 0.0;
                    let mut capacity = 0.0;
                    let mut energy = 0.0;
                    for channel in 0..channels {
                        let slot = cell * channels + channel;
                        // A species absent on both sides of every open face
                        // has exactly zero mass and heat flux. Keep signed
                        // zero and nonzero enthalpy on the ordinary path.
                        if SPARSE
                            && mass[slot].to_bits() == 0
                            && enthalpy[slot].to_bits() == 0
                            && adjacent.iter().flatten().all(|&other| {
                                let other_slot = other * channels + channel;
                                solid[other] != 0
                                    || (mass[other_slot].to_bits() == 0
                                        && enthalpy[other_slot].to_bits() == 0)
                            })
                        {
                            output[channel] = 0.0;
                            continue;
                        }
                        let mut mass_flux = [0.0; 4];
                        let mut heat_flux = [0.0; 4];
                        if solid[cell] == 0 {
                            for (face, other) in adjacent.iter().enumerate() {
                                let Some(other) = other.filter(|&other| solid[other] == 0) else {
                                    continue;
                                };
                                let other_slot = other * channels + channel;
                                let axis = usize::from(face >= 2);
                                let speed = 0.5 * (velocity[cell][axis] + velocity[other][axis])
                                    + if axis == 1 {
                                        job.state.species[channel].vertical_drift
                                    } else {
                                        0.0
                                    };
                                let (a, b) = if face == 0 || face == 2 {
                                    (other_slot, slot)
                                } else {
                                    (slot, other_slot)
                                };
                                mass_flux[face] = flux(mass[a], mass[b], speed);
                                heat_flux[face] = flux(enthalpy[a], enthalpy[b], speed);
                            }
                            output[channel] = mass[slot]
                                - dt * ((mass_flux[1] - mass_flux[0])
                                    + (mass_flux[3] - mass_flux[2]));
                        } else {
                            output[channel] = 0.0;
                        }
                        let next_energy = if solid[cell] == 0 {
                            enthalpy[slot]
                                - dt * ((heat_flux[1] - heat_flux[0])
                                    + (heat_flux[3] - heat_flux[2]))
                        } else {
                            0.0
                        };
                        total_mass += output[channel];
                        capacity +=
                            output[channel] * job.state.species[channel].specific_heat_capacity;
                        energy += next_energy;
                    }
                    *thermal = if total_mass > 0.0 && capacity > 0.0 {
                        ThermalCell {
                            mass: total_mass,
                            heat: capacity / total_mass,
                            temperature: energy / capacity,
                        }
                    } else {
                        ThermalCell {
                            mass: total_mass,
                            heat: 1.0,
                            temperature: 0.0,
                        }
                    };
                }
            })
    });
}
