//! Parallel host implementation of the resident gas tensor chain. Work remains
//! on the host when its bounded tensors fit the CPU working set, eliminating
//! transfers and the GPU completion barrier without dropping any species.
use super::*;

#[cfg(test)]
#[path = "reference/gas_transport.rs"]
mod transport_reference;

#[derive(Clone, Copy, Default)]
struct ThermalCell {
    mass: f32,
    heat: f32,
    temperature: f32,
}

#[derive(Default)]
pub(crate) struct Workspace {
    density: Vec<f32>,
    source_enthalpy: Vec<f32>,
    diffused_mass: Vec<f32>,
    diffused_enthalpy: Vec<f32>,
    velocity: Vec<[f32; 2]>,
    conductivity: Vec<f32>,
    thermal: Vec<ThermalCell>,
    pub(crate) mass_output: Vec<f32>,
    pub(crate) temperature_output: Vec<f32>,
    sparse_transport: bool,
    #[cfg(test)]
    reference_transport: bool,
}

// Chunk boundaries are complete rows. Advance x/y explicitly so the hot
// stencil loops do not divide each cell index by the runtime grid width.
fn cell_coordinates(width: usize, start: usize, count: usize)
    -> impl Iterator<Item = (usize, usize, usize)> {
    let first_y = start / width;
    (0..count / width).flat_map(move |row| {
        (0..width).map(move |x| (start + row * width + x, x, first_y + row))
    })
}

fn neighbors_at(
    cell: usize,
    x: usize,
    y: usize,
    width: usize,
    height: usize,
) -> [Option<usize>; 4] {
    [
        if x > 0 { Some(cell - 1) } else { None },
        if x + 1 < width { Some(cell + 1) } else { None },
        if y > 0 { Some(cell - width) } else { None },
        if y + 1 < height {
            Some(cell + width)
        } else {
            None
        },
    ]
}

fn profile_cpu_chain(started: Option<std::time::Instant>, workspaces: &[Workspace]) {
    if let Some(started) = started {
        let worlds = workspaces.len();
        let sparse_worlds = workspaces.iter().filter(|workspace| workspace.sparse_transport).count();
        eprintln!(
            "[CycleTrim OniSim] parallel CPU gas chain worlds={worlds} sparse_worlds={sparse_worlds} wall={:.3}ms",
            started.elapsed().as_secs_f64() * 1000.0
        );
    }
}

impl GpuSimulation {
    pub(crate) fn coroutine_cpu_gas_enabled(&self) -> bool {
        #[cfg(test)]
        if crate::native_chain_tests::BENCHMARK_INLINE_COROUTINES.load(std::sync::atomic::Ordering::SeqCst) { return false; }
        #[cfg(test)]
        if self.thermal_reference { return false; }
        crate::parallel::pool(crate::parallel::MIN_PARALLEL_CELLS).is_some()
    }

    pub(crate) async fn cpu_work_async<T, F>(&mut self, work: F) -> Result<T, String>
    where T: Send + 'static,
          F: FnOnce(&mut Vec<Workspace>) -> (T, bool) + Send + 'static,
    {
        let pool = crate::parallel::pool(crate::parallel::MIN_PARALLEL_CELLS)
            .ok_or("CPU simulation workers unavailable")?;
        #[cfg(test)]
        crate::native_chain_tests::BENCHMARK_COROUTINE_JOBS.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        let profiling = std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some();
        let submitted = std::time::Instant::now();
        let mut spaces = std::mem::take(&mut self.gas_cpu);
        let environment = crate::float_env::FloatEnv::capture();
        let (sender, receiver) = tokio::sync::oneshot::channel();
        pool.spawn_fifo(move || {
            let started = std::time::Instant::now();
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                let _environment = environment.install();
                work(&mut spaces)
            }));
            let _ = sender.send((spaces, result, started, std::time::Instant::now()));
        });
        let (spaces, result, started, finished) = receiver.await.map_err(|_| "CPU gas coroutine disconnected")?;
        self.gas_cpu = spaces;
        if profiling {
            eprintln!("[CycleTrim OniSim] CPU coroutine queue={:.3}ms work={:.3}ms resume={:.3}ms",
                (started-submitted).as_secs_f64()*1000.0, (finished-started).as_secs_f64()*1000.0,
                finished.elapsed().as_secs_f64()*1000.0);
        }
        let (value, completed) = result.map_err(|_| "CPU gas coroutine panicked")?;
        self.gas_cpu_seen |= completed;
        Ok(value)
    }

    pub(super) fn try_cpu_gas(
        &mut self,
        jobs: &mut [GasBatchJob<'_>],
        diffuse: bool,
        dt: f32,
    ) -> Result<bool, String> {
        let started = std::env::var_os("CYCLETRIM_GPU_PROFILE")
            .is_some()
            .then(std::time::Instant::now);
        if !self.compute_cpu_gas(jobs, diffuse, dt)? {
            return Ok(false);
        }
        // Borrowed output APIs retain their atomic all-world commit.
        for (workspace, job) in self.gas_cpu.iter().zip(jobs.iter_mut()) {
            job.mass_output.copy_from_slice(&workspace.mass_output);
            job.temperature_output
                .copy_from_slice(&workspace.temperature_output);
        }
        profile_cpu_chain(started, &self.gas_cpu);
        Ok(true)
    }

    pub(crate) fn step_gas_owned(
        &mut self,
        width: usize,
        height: usize,
        channels: usize,
        state: &State<'_>,
        temperature: &[f32],
        conductivity: &[f32],
        diffuse: bool,
        dt: f32,
        mass_output: &mut Vec<f32>,
        temperature_output: &mut Vec<f32>,
    ) -> Result<(), String> {
        let started = std::env::var_os("CYCLETRIM_GPU_PROFILE")
            .is_some()
            .then(std::time::Instant::now);
        let completed_on_cpu = {
            let jobs = [GasBatchJob {
                dimensions: (width, height, channels),
                state: State {
                    mass: state.mass,
                    enthalpy: state.enthalpy,
                    velocity_x: state.velocity_x,
                    velocity_y: state.velocity_y,
                    solid: state.solid,
                    species: state.species,
                },
                temperature,
                conductivity,
                mass_output: mass_output.as_mut_slice(),
                temperature_output: temperature_output.as_mut_slice(),
            }];
            self.compute_cpu_gas(&jobs, diffuse, dt)?
        };
        if completed_on_cpu {
            // Staging owns these private buffers. Exchange them only after
            // validation and computation succeed, avoiding two full copies.
            std::mem::swap(mass_output, &mut self.gas_cpu[0].mass_output);
            std::mem::swap(temperature_output, &mut self.gas_cpu[0].temperature_output);
            profile_cpu_chain(started, &self.gas_cpu);
            Ok(())
        } else {
            self.step_gas(
                width,
                height,
                channels,
                state,
                temperature,
                conductivity,
                diffuse,
                dt,
                mass_output,
                temperature_output,
            )
        }
    }

    fn compute_cpu_gas(
        &mut self,
        jobs: &[GasBatchJob<'_>],
        diffuse: bool,
        dt: f32,
    ) -> Result<bool, String> {
        #[cfg(test)]
        if self.thermal_reference {
            return Ok(false);
        }
        let completed = Self::compute_cpu_gas_in(&mut self.gas_cpu, jobs, diffuse, dt)?;
        self.gas_cpu_seen |= completed && !jobs.is_empty();
        Ok(completed)
    }

    pub(crate) fn compute_cpu_gas_in(workspaces: &mut Vec<Workspace>, jobs: &[GasBatchJob<'_>], diffuse: bool, dt: f32) -> Result<bool, String> {
        let work: usize = jobs.iter().map(|job| job.state.mass.len()).sum();
        let Some(pool) = crate::parallel::pool(work.max(crate::parallel::MIN_PARALLEL_CELLS))
        else {
            return Ok(false);
        };
        // Selection is based on tensor working-set size, with every species
        // participating in the same equations on either backend.
        if jobs.iter().any(|job| job.state.mass.len() > 1024 * 1024) {
            return Ok(false);
        }
        workspaces.resize_with(jobs.len(), Workspace::default);
        let environment = crate::float_env::FloatEnv::capture();
        let results: Vec<_> = pool.install(|| {
            workspaces
                .par_iter_mut()
                .zip(jobs.par_iter())
                .map(|(workspace, job)| {
                    let _environment = environment.install();
                    workspace.step(job, diffuse, dt, pool)
                })
                .collect()
        });
        for result in results {
            result?;
        }
        Ok(true)
    }
}

#[inline(always)]
fn transport<const SPARSE: bool>(
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
                    if solid[cell] != 0 {
                        output.fill(0.0);
                        *thermal = ThermalCell { mass: 0.0, heat: 1.0, temperature: 0.0 };
                        continue;
                    }
                    // Geometry and face velocities are shared by every species.
                    let adjacent = neighbors_at(cell, x, y, width, height)
                        .map(|other| other.filter(|&other| solid[other] == 0));
                    let faces: [Option<(usize, f32)>; 4] = std::array::from_fn(|face| {
                        adjacent[face].map(|other| {
                            let axis = usize::from(face >= 2);
                            (other * channels, 0.5 * (velocity[cell][axis] + velocity[other][axis]))
                        })
                    });
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
                            && faces.iter().flatten().all(|&(base, _)| {
                                let other_slot = base + channel;
                                mass[other_slot].to_bits() == 0
                                    && enthalpy[other_slot].to_bits() == 0
                            })
                        {
                            output[channel] = 0.0;
                            continue;
                        }
                        let mut mass_flux = [0.0; 4];
                        let mut heat_flux = [0.0; 4];
                        for (face, data) in faces.iter().enumerate() {
                            let Some((base, base_speed)) = *data else {
                                continue;
                            };
                            let other_slot = base + channel;
                            let speed = base_speed
                                + if face >= 2 {
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
                        let next_energy = enthalpy[slot]
                            - dt * ((heat_flux[1] - heat_flux[0])
                                + (heat_flux[3] - heat_flux[2]));
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

impl Workspace {
    fn step(
        &mut self,
        job: &GasBatchJob<'_>,
        diffuse: bool,
        dt: f32,
        pool: &rayon::ThreadPool,
    ) -> Result<(), String> {
        let (width, height, channels) = job.dimensions;
        let count = width
            .checked_mul(height)
            .ok_or("gas tensor dimensions overflow")?;
        let values = count
            .checked_mul(channels)
            .ok_or("gas tensor dimensions overflow")?;
        if width == 0
            || height == 0
            || channels == 0
            || !dt.is_finite()
            || dt <= 0.0
            || job.mass_output.len() != values
            || job.temperature_output.len() != count
            || job.state.mass.len() != values
            || job.state.solid.len() != count
            || job.state.species.len() != channels
            || job.temperature.len() != count
            || job.conductivity.len() != channels
            || job.conductivity.iter().any(|k| !k.is_finite() || *k < 0.0)
        {
            return Err("invalid CPU gas tensor dimensions, outputs or coefficients".into());
        }
        if count >= 8192 {
            if job
                .state
                .enthalpy
                .iter()
                .any(|e| !e.is_finite() || *e < 0.0)
            {
                return Err("state contains negative mass/enthalpy or non-finite values".into());
            }
            let environment = crate::float_env::FloatEnv::capture();
            let results: Vec<_> = pool.install(|| {
                job.state
                    .mass
                    .par_chunks(4096 * channels)
                    .zip(job.state.solid.par_chunks(4096))
                    .zip(job.temperature.par_chunks(4096))
                    .map(|((mass, solid), temperature)| {
                        let _environment = environment.install();
                        let state = State {
                            mass,
                            enthalpy: &[],
                            velocity_x: &[],
                            velocity_y: &[],
                            solid,
                            species: job.state.species,
                        };
                        onisim_gpu::multicomponent::validate_temperature_state(
                            &state,
                            temperature.len(),
                            channels,
                            temperature,
                        )
                    })
                    .collect()
            });
            for result in results {
                result?;
            }
        } else {
            onisim_gpu::multicomponent::validate_temperature_state(
                &job.state,
                count,
                channels,
                job.temperature,
            )?;
        }
        if channels == 1 {
            self.sparse_transport = false;
            return self.step_single_channel(job, dt, pool);
        }
        let environment = crate::float_env::FloatEnv::capture();
        self.source_enthalpy.resize(values, 0.0);
        self.density.resize(count, 0.0);
        let rows = width * 2;
        let minimum_chunks = if count < 8192 {
            count.div_ceil(rows)
        } else {
            1
        };
        let (present_species, open_slots) = pool.install(|| {
            self.source_enthalpy
                .par_chunks_mut(rows * channels)
                .with_min_len(minimum_chunks)
                .zip(self.density.par_chunks_mut(rows))
                .enumerate()
                .map(|(chunk, (energy, density))| {
                    let _environment = environment.install();
                    let mut present = 0usize;
                    let mut open = 0usize;
                    for (local, (energy, density)) in
                        energy.chunks_mut(channels).zip(density).enumerate()
                    {
                        let cell = chunk * rows + local;
                        let track = job.state.solid[cell] == 0;
                        open += usize::from(track) * channels;
                        let mut total = 0.0;
                        for channel in 0..channels {
                            let m = job.state.mass[cell * channels + channel];
                            total += m;
                            present += usize::from(track && m != 0.0);
                            energy[channel] = m
                                * job.state.species[channel].specific_heat_capacity
                                * job.temperature[cell];
                        }
                        *density = total;
                    }
                    (present, open)
                })
                .reduce(|| (0, 0), |(pa, oa), (pb, ob)| (pa + pb, oa + ob))
        });
        let solid = job.state.solid;
        let (mass, enthalpy) = if diffuse && channels > 1 {
            self.diffused_mass.resize(values, 0.0);
            self.diffused_enthalpy.resize(values, 0.0);
            let density = &self.density;
            let source_energy = &self.source_enthalpy;
            pool.install(|| {
                self.diffused_mass
                    .par_chunks_mut(rows * channels)
                    .with_min_len(minimum_chunks)
                    .zip(self.diffused_enthalpy.par_chunks_mut(rows * channels))
                    .enumerate()
                    .for_each(|(chunk, (mass, energy))| {
                        let _environment = environment.install();
                        let coordinates = cell_coordinates(width, chunk * rows, mass.len() / channels);
                        for ((mass, energy), (cell, x, y)) in mass
                            .chunks_mut(channels)
                            .zip(energy.chunks_mut(channels))
                            .zip(coordinates)
                        {
                            let adjacent = neighbors_at(cell, x, y, width, height);
                            for channel in 0..channels {
                                let slot = cell * channels + channel;
                                let ma = job.state.mass[slot];
                                let mut mass_flux = 0.0;
                                let mut heat_flux = 0.0;
                                if solid[cell] == 0 && density[cell] > 0.0 {
                                    for other in adjacent.into_iter().flatten() {
                                        if solid[other] != 0 || density[other] <= 0.0 {
                                            continue;
                                        }
                                        let other_slot = other * channels + channel;
                                        let mb = job.state.mass[other_slot];
                                        let flow = (ma / density[cell] - mb / density[other])
                                            * density[cell].min(density[other])
                                            * 0.05;
                                        mass_flux += flow;
                                        if flow > 0.0 && ma > 0.0 {
                                            heat_flux += flow * source_energy[slot] / ma;
                                        }
                                        if flow < 0.0 && mb > 0.0 {
                                            heat_flux += flow * source_energy[other_slot] / mb;
                                        }
                                    }
                                }
                                mass[channel] = ma - mass_flux;
                                energy[channel] = source_energy[slot] - heat_flux;
                            }
                        }
                    })
            });
            (&self.diffused_mass[..], &self.diffused_enthalpy[..])
        } else {
            (job.state.mass, &self.source_enthalpy[..])
        };
        self.velocity.resize(count, [0.0; 2]);
        self.conductivity.resize(count, 0.0);
        let total = |cell: usize| {
            mass[cell * channels..(cell + 1) * channels]
                .iter()
                .sum::<f32>()
        };
        let min_drift = job
            .state
            .species
            .iter()
            .map(|s| s.vertical_drift)
            .fold(f32::INFINITY, f32::min);
        let max_drift = job
            .state
            .species
            .iter()
            .map(|s| s.vertical_drift)
            .fold(f32::NEG_INFINITY, f32::max);
        let finite = pool.install(|| {
            self.velocity
                .par_chunks_mut(rows)
                .with_min_len(minimum_chunks)
                .zip(self.conductivity.par_chunks_mut(rows))
                .enumerate()
                .map(|(chunk, (velocities, conductivity))| {
                    let _environment = environment.install();
                    let mut finite = true;
                    let coordinates = cell_coordinates(width, chunk * rows, velocities.len());
                    for ((velocity, conductivity), (cell, x, y)) in
                        velocities.iter_mut().zip(conductivity).zip(coordinates)
                    {
                        let center = total(cell);
                        let adjacent = neighbors_at(cell, x, y, width, height).map(|neighbor| {
                            neighbor
                                .filter(|&other| solid[other] == 0)
                                .map_or(center, total)
                        });
                        *velocity = if solid[cell] == 0 {
                            [
                                0.04 * (adjacent[0] - adjacent[1]),
                                0.04 * (adjacent[2] - adjacent[3]),
                            ]
                        } else {
                            [0.0; 2]
                        };
                        let rate = velocity[0].abs()
                            + (velocity[1] + min_drift)
                                .abs()
                                .max((velocity[1] + max_drift).abs());
                        finite &= rate.is_finite();
                        let mut weighted = 0.0;
                        for channel in 0..channels {
                            weighted += mass[cell * channels + channel] * job.conductivity[channel];
                        }
                        *conductivity = if center > 0.0 { weighted / center } else { 0.0 };
                    }
                    finite
                })
                .all(|finite| finite)
        });
        if !finite {
            return Err("non-finite generated gas velocity".into());
        }
        self.mass_output.resize(values, 0.0);
        self.thermal.resize(count, ThermalCell::default());
        // At least three quarters of the input slots must be empty before
        // sparse face checks pay off. Dense mixtures keep their original
        // branch-free species loop through a separate monomorphization.
        self.sparse_transport = present_species.saturating_mul(4) < open_slots;
        #[cfg(test)]
        if self.reference_transport {
            if self.sparse_transport {
                transport_reference::transport::<true>(&mut self.mass_output, &mut self.thermal, job,
                    (mass, enthalpy), &self.velocity, dt, pool);
            } else {
                transport_reference::transport::<false>(&mut self.mass_output, &mut self.thermal, job,
                    (mass, enthalpy), &self.velocity, dt, pool);
            }
            return self.finish_thermal(width, height, dt, pool);
        }
        if self.sparse_transport {
            transport::<true>(&mut self.mass_output, &mut self.thermal, job,
                (mass, enthalpy), &self.velocity, dt, pool);
        } else {
            transport::<false>(&mut self.mass_output, &mut self.thermal, job,
                (mass, enthalpy), &self.velocity, dt, pool);
        }
        self.finish_thermal(width, height, dt, pool)
    }

    fn step_single_channel(
        &mut self,
        job: &GasBatchJob<'_>,
        dt: f32,
        pool: &rayon::ThreadPool,
    ) -> Result<(), String> {
        let (width, height, _) = job.dimensions;
        let count = width * height;
        let rows = width * 8;
        let minimum_chunks = if count < 8192 {
            count.div_ceil(rows)
        } else {
            1
        };
        let mass = job.state.mass;
        let solid = job.state.solid;
        let heat = job.state.species[0].specific_heat_capacity;
        let drift = job.state.species[0].vertical_drift;
        let conductivity = job.conductivity[0];
        let environment = crate::float_env::FloatEnv::capture();
        self.velocity.resize(count, [0.0; 2]);
        self.conductivity.resize(count, 0.0);
        let uniform_temperature = solid
            .iter()
            .position(|&blocked| blocked == 0)
            .map_or(0.0, |cell| job.temperature[cell]);
        let (finite, isothermal) = pool.install(|| {
            self.velocity
                .par_chunks_mut(rows)
                .with_min_len(minimum_chunks)
                .zip(self.conductivity.par_chunks_mut(rows))
                .enumerate()
                .map(|(chunk, (velocity, conductivities))| {
                    let _environment = environment.install();
                    let mut finite = true;
                    let mut isothermal = true;
                    for (row, (velocities, conductivities)) in velocity
                        .chunks_mut(width)
                        .zip(conductivities.chunks_mut(width))
                        .enumerate()
                    {
                        let y = chunk * 8 + row;
                        for (x, (velocity, k)) in
                            velocities.iter_mut().zip(conductivities).enumerate()
                        {
                            let cell = y * width + x;
                            let center = mass[cell];
                            isothermal &= solid[cell] != 0
                                || (center >= 0.01
                                    && center * heat * job.temperature[cell] >= 0.01
                                    && job.temperature[cell] == uniform_temperature);
                            let adjacent = neighbors_at(cell, x, y, width, height).map(|other| {
                                other
                                    .filter(|&other| solid[other] == 0)
                                    .map_or(center, |other| mass[other])
                            });
                            *velocity = if solid[cell] == 0 {
                                [
                                    0.04 * (adjacent[0] - adjacent[1]),
                                    0.04 * (adjacent[2] - adjacent[3]),
                                ]
                            } else {
                                [0.0; 2]
                            };
                            finite &= (velocity[0].abs() + (velocity[1] + drift).abs()).is_finite();
                            *k = if center > 0.0 {
                                center * conductivity / center
                            } else {
                                0.0
                            };
                        }
                    }
                    (finite, isothermal)
                })
                .reduce(|| (true, true), |a, b| (a.0 && b.0, a.1 && b.1))
        });
        if !finite {
            return Err("non-finite generated gas velocity".into());
        }
        self.mass_output.resize(count, 0.0);
        self.thermal.resize(count, ThermalCell::default());
        self.temperature_output.resize(count, 0.0);
        let velocity = &self.velocity;
        let cap = 1.0f32.min(0.125 / dt);
        let diffusion = 2.0f32.min(0.125 / dt);
        let flux = |a: f32, b: f32, speed: f32| {
            let speed = speed.clamp(-cap, cap);
            let delta = if (a - b).abs() <= 1e-7f32.max(a.max(b) * 1e-5) {
                0.0
            } else {
                a - b
            };
            speed * if speed >= 0.0 { a } else { b } + diffusion * delta
        };
        pool.install(|| {
            self.mass_output
                .par_chunks_mut(rows)
                .with_min_len(minimum_chunks)
                .zip(self.thermal.par_chunks_mut(rows))
                .zip(self.temperature_output.par_chunks_mut(rows))
                .enumerate()
                .for_each(|(chunk, ((output, thermal), temperatures))| {
                    let _environment = environment.install();
                    for (row, ((outputs, thermals), temperatures)) in output
                        .chunks_mut(width)
                        .zip(thermal.chunks_mut(width))
                        .zip(temperatures.chunks_mut(width))
                        .enumerate()
                    {
                        let y = chunk * 8 + row;
                        for (x, ((output, thermal), temperature)) in outputs
                            .iter_mut()
                            .zip(thermals)
                            .zip(temperatures)
                            .enumerate()
                        {
                            let cell = y * width + x;
                            if solid[cell] != 0 {
                                *output = 0.0;
                                *thermal = ThermalCell {
                                    mass: 0.0,
                                    heat: 1.0,
                                    temperature: 0.0,
                                };
                                *temperature = 0.0;
                                continue;
                            }
                            let adjacent = neighbors_at(cell, x, y, width, height)
                                .map(|other| other.filter(|&other| solid[other] == 0));
                            let uniform_temperature = isothermal
                                || (mass[cell] >= 0.01
                                    && mass[cell] * heat * job.temperature[cell] >= 0.01
                                    && adjacent.iter().flatten().all(|&other| {
                                        mass[other] >= 0.01
                                            && job.temperature[other] == job.temperature[cell]
                                    }));
                            let mut mass_flux = [0.0; 4];
                            let mut energy_flux = [0.0; 4];
                            for (face, other) in adjacent.iter().enumerate() {
                                let Some(other) = *other else {
                                    continue;
                                };
                                let axis = usize::from(face >= 2);
                                let speed = 0.5 * (velocity[cell][axis] + velocity[other][axis])
                                    + if axis == 1 { drift } else { 0.0 };
                                let (a, b) = if face == 0 || face == 2 {
                                    (other, cell)
                                } else {
                                    (cell, other)
                                };
                                mass_flux[face] = flux(mass[a], mass[b], speed);
                                if !uniform_temperature {
                                    energy_flux[face] = flux(
                                        mass[a] * heat * job.temperature[a],
                                        mass[b] * heat * job.temperature[b],
                                        speed,
                                    );
                                }
                            }
                            *output = mass[cell]
                                - dt * ((mass_flux[1] - mass_flux[0])
                                    + (mass_flux[3] - mass_flux[2]));
                            let capacity = *output * heat;
                            *thermal = if *output > 0.0 && capacity > 0.0 {
                                let temperature = if uniform_temperature {
                                    job.temperature[cell]
                                } else {
                                    (mass[cell] * heat * job.temperature[cell]
                                        - dt * ((energy_flux[1] - energy_flux[0])
                                            + (energy_flux[3] - energy_flux[2])))
                                        / capacity
                                };
                                ThermalCell {
                                    mass: *output,
                                    heat: capacity / *output,
                                    temperature,
                                }
                            } else {
                                ThermalCell {
                                    mass: *output,
                                    heat: 1.0,
                                    temperature: 0.0,
                                }
                            };
                            if isothermal {
                                *temperature =
                                    if thermal.mass <= 0.0 || thermal.mass * thermal.heat <= 1e-6 {
                                        thermal.temperature
                                    } else {
                                        thermal.temperature.clamp(1.0, 10000.0)
                                    };
                            }
                        }
                    }
                })
        });
        if isothermal {
            // Every open cell retains the same temperature after advection.
            // Every conductive face therefore contributes exactly zero heat.
            if self
                .mass_output
                .iter()
                .chain(&self.temperature_output)
                .any(|v| !v.is_finite())
            {
                return Err("non-finite CPU gas tensor output".into());
            }
            Ok(())
        } else {
            self.finish_thermal(width, height, dt, pool)
        }
    }

    fn finish_thermal(
        &mut self,
        width: usize,
        height: usize,
        dt: f32,
        pool: &rayon::ThreadPool,
    ) -> Result<(), String> {
        let count = width * height;
        let rows = width * 2;
        let minimum_chunks = if count < 8192 {
            count.div_ceil(rows)
        } else {
            1
        };
        let environment = crate::float_env::FloatEnv::capture();
        let thermal = &self.thermal;
        let conductivity = &self.conductivity;
        let heat_flux = |a: usize, b: usize| {
            let a_cell = thermal[a];
            let b_cell = thermal[b];
            if a_cell.mass <= 0.0 || b_cell.mass <= 0.0 {
                return 0.0;
            }
            let delta = a_cell.temperature - b_cell.temperature;
            if delta.abs() < 1e-6 {
                return 0.0;
            }
            let k = 2.0 * (conductivity[a] * conductivity[b])
                / (conductivity[a] + conductivity[b] + 1e-6);
            let limit =
                (a_cell.mass * a_cell.heat).min(b_cell.mass * b_cell.heat) * delta.abs() * 0.25;
            (k * delta * (dt * 4.0)).max(-limit).min(limit)
        };
        self.temperature_output.resize(count, 0.0);
        pool.install(|| {
            self.temperature_output
                .par_chunks_mut(rows)
                .with_min_len(minimum_chunks)
                .enumerate()
                .for_each(|(chunk, output)| {
                    let _environment = environment.install();
                    let coordinates = cell_coordinates(width, chunk * rows, output.len());
                    for (output, (cell, x, y)) in output.iter_mut().zip(coordinates) {
                        let state = thermal[cell];
                        let capacity = state.mass * state.heat;
                        if state.mass <= 0.0 || capacity <= 1e-6 {
                            *output = state.temperature;
                            continue;
                        }
                        let adjacent = neighbors_at(cell, x, y, width, height);
                        let mut energy = 0.0;
                        if let Some(other) = adjacent[0] {
                            energy += heat_flux(other, cell);
                        }
                        if let Some(other) = adjacent[1] {
                            energy -= heat_flux(cell, other);
                        }
                        if let Some(other) = adjacent[2] {
                            energy += heat_flux(other, cell);
                        }
                        if let Some(other) = adjacent[3] {
                            energy -= heat_flux(cell, other);
                        }
                        *output = (state.temperature + energy / capacity).clamp(1.0, 10000.0);
                    }
                })
        });
        if self
            .mass_output
            .iter()
            .chain(&self.temperature_output)
            .any(|v| !v.is_finite())
        {
            return Err("non-finite CPU gas tensor output".into());
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn row_coordinates_cover_odd_shapes_and_partial_chunks() {
        for (width, height) in [(1, 7), (17, 13), (96, 65)] {
            let count = width * height;
            for rows in [1, 2, 8] {
                for start in (0..count).step_by(width * rows) {
                    let length = (count - start).min(width * rows);
                    let actual: Vec<_> = cell_coordinates(width, start, length).collect();
                    let expected: Vec<_> = (start..start + length)
                        .map(|cell| (cell, cell % width, cell / width)).collect();
                    assert_eq!(actual, expected);
                }
            }
            assert_eq!(cell_coordinates(width, count, 0).count(), 0);
        }
    }

    #[test]
    fn isothermal_advection_matches_explicit_thermal_gather_bits() {
        let (width, height) = (53, 29);
        let count = width * height;
        let pool = rayon::ThreadPoolBuilder::new()
            .num_threads(4)
            .build()
            .unwrap();
        for temperature in [1.0, 300.0, 10001.0] {
            for heat in [1.0, 2.5, 0.000001] {
                let mass: Vec<_> = (0..count)
                    .map(|cell| 10000.0 + (cell % 31) as f32)
                    .collect();
                let solid: Vec<_> = (0..count).map(|cell| u32::from(cell % 97 == 0)).collect();
                let temperatures = vec![temperature; count];
                let species = [onisim_gpu::multicomponent::SpeciesInfo {
                    element_id: 3,
                    phase: 1,
                    specific_heat_capacity: heat,
                    vertical_drift: 0.01,
                }];
                let mut mass_output = vec![0.0; count];
                let mut temperature_output = vec![0.0; count];
                let job = GasBatchJob {
                    dimensions: (width, height, 1),
                    state: State {
                        mass: &mass,
                        enthalpy: &[],
                        velocity_x: &[],
                        velocity_y: &[],
                        solid: &solid,
                        species: &species,
                    },
                    temperature: &temperatures,
                    conductivity: &[0.05],
                    mass_output: &mut mass_output,
                    temperature_output: &mut temperature_output,
                };
                let mut workspace = Workspace::default();
                workspace.step_single_channel(&job, 0.05, &pool).unwrap();
                let optimized = workspace.temperature_output.clone();
                workspace
                    .finish_thermal(width, height, 0.05, &pool)
                    .unwrap();
                assert!(
                    optimized
                        .iter()
                        .zip(&workspace.temperature_output)
                        .all(|(a, b)| a.to_bits() == b.to_bits())
                );
            }
        }
    }

    #[test]
    fn owned_outputs_match_borrowed_outputs_and_preserve_errors() {
        let (width, height, channels) = (96, 96, 2);
        let count = width * height;
        let mut gpu = GpuSimulation::new(width, height).unwrap();
        let mass: Vec<_> = (0..count * channels)
            .map(|slot| 0.25 + (slot % 11) as f32 / 16.0)
            .collect();
        let temperature: Vec<_> = (0..count).map(|cell| 300.0 + (cell % 17) as f32).collect();
        let solid = vec![0; count];
        let species = [
            onisim_gpu::multicomponent::SpeciesInfo {
                element_id: 3,
                phase: 1,
                specific_heat_capacity: 1.0,
                vertical_drift: 0.02,
            },
            onisim_gpu::multicomponent::SpeciesInfo {
                element_id: 4,
                phase: 1,
                specific_heat_capacity: 2.0,
                vertical_drift: -0.01,
            },
        ];
        let state = State {
            mass: &mass,
            enthalpy: &[],
            velocity_x: &[],
            velocity_y: &[],
            solid: &solid,
            species: &species,
        };
        let mut expected_mass = vec![0.0; count * channels];
        let mut expected_temperature = vec![0.0; count];
        gpu.step_gas(
            width,
            height,
            channels,
            &state,
            &temperature,
            &[0.1, 0.2],
            true,
            0.05,
            &mut expected_mass,
            &mut expected_temperature,
        )
        .unwrap();
        let mut output_mass = vec![123.0; count * channels];
        let mut output_temperature = vec![456.0; count];
        for _ in 0..3 {
            gpu.step_gas_owned(
                width,
                height,
                channels,
                &state,
                &temperature,
                &[0.1, 0.2],
                true,
                0.05,
                &mut output_mass,
                &mut output_temperature,
            )
            .unwrap();
            assert_eq!(output_mass, expected_mass);
            assert_eq!(output_temperature, expected_temperature);
        }
        let pointers = (output_mass.as_ptr(), output_temperature.as_ptr());
        let mut invalid_mass = mass.clone();
        invalid_mass[count * channels - 1] = f32::NAN;
        let invalid = State {
            mass: &invalid_mass,
            ..state
        };
        assert!(
            gpu.step_gas_owned(
                width,
                height,
                channels,
                &invalid,
                &temperature,
                &[0.1, 0.2],
                true,
                0.05,
                &mut output_mass,
                &mut output_temperature
            )
            .is_err()
        );
        assert_eq!(output_mass, expected_mass);
        assert_eq!(output_temperature, expected_temperature);
        assert_eq!(
            pointers,
            (output_mass.as_ptr(), output_temperature.as_ptr())
        );
    }

    #[test]
    fn cpu_batch_rejects_late_invalid_world_before_any_output_commit() {
        let count = 96 * 96;
        let mut gpu = GpuSimulation::new(96, 96).unwrap();
        let mass = vec![1.0; count];
        let mut invalid_mass = mass.clone();
        invalid_mass[count - 1] = f32::NAN;
        let temperature = vec![300.0; count];
        let solid = vec![0; count];
        let species = [onisim_gpu::multicomponent::SpeciesInfo {
            element_id: 3,
            phase: 1,
            specific_heat_capacity: 1.0,
            vertical_drift: 0.0,
        }];
        let mut first_mass = vec![123.0; count];
        let mut first_temperature = vec![456.0; count];
        let mut second_mass = first_mass.clone();
        let mut second_temperature = first_temperature.clone();
        let mut jobs = [
            GasBatchJob {
                dimensions: (96, 96, 1),
                state: State {
                    mass: &mass,
                    enthalpy: &[],
                    velocity_x: &[],
                    velocity_y: &[],
                    solid: &solid,
                    species: &species,
                },
                temperature: &temperature,
                conductivity: &[0.1],
                mass_output: &mut first_mass,
                temperature_output: &mut first_temperature,
            },
            GasBatchJob {
                dimensions: (96, 96, 1),
                state: State {
                    mass: &invalid_mass,
                    enthalpy: &[],
                    velocity_x: &[],
                    velocity_y: &[],
                    solid: &solid,
                    species: &species,
                },
                temperature: &temperature,
                conductivity: &[0.1],
                mass_output: &mut second_mass,
                temperature_output: &mut second_temperature,
            },
        ];
        assert!(gpu.try_cpu_gas(&mut jobs, true, 0.05).is_err());
        assert!(first_mass.iter().chain(&second_mass).all(|&m| m == 123.0));
        assert!(
            first_temperature
                .iter()
                .chain(&second_temperature)
                .all(|&t| t == 456.0)
        );
    }

    #[test]
    #[ignore = "manual CPU gas-chain benchmark; run without a game or concurrent benchmark"]
    fn benchmark_cpu_gas_chain() {
        let (width, height, channels) = (160, 96, 16);
        let count = width * height;
        let sparse = std::env::var_os("CYCLETRIM_GAS_BENCH_SPARSE").is_some();
        let pool = rayon::ThreadPoolBuilder::new()
            .num_threads(8)
            .build()
            .unwrap();
        let solid: Vec<_> = (0..count).map(|cell| u32::from(cell % 7 == 0)).collect();
        let mass: Vec<_> = (0..count * channels)
            .map(|slot| {
                let cell = slot / channels;
                if solid[cell] != 0 || (sparse && slot % channels != cell % channels) {
                    0.0
                } else {
                    ((slot * 17) % 9) as f32 * 0.125
                }
            })
            .collect();
        let temperature: Vec<_> = (0..count)
            .map(|cell| 300.0 + (cell % 17) as f32 * 9.0)
            .collect();
        let species: Vec<_> = (0..channels)
            .map(|channel| onisim_gpu::multicomponent::SpeciesInfo {
                element_id: channel as u16 + 1,
                phase: 1,
                specific_heat_capacity: 0.5 + channel as f32 * 0.25,
                vertical_drift: (channel as f32 % 3.0 - 1.0) * 0.03,
            })
            .collect();
        let conductivity: Vec<_> = (0..channels)
            .map(|channel| 0.1 + channel as f32 * 0.03)
            .collect();
        let mut output_mass = vec![0.0; mass.len()];
        let mut output_temperature = vec![0.0; count];
        let before: f64 = mass.iter().map(|&m| f64::from(m)).sum();
        let mut workspace = Workspace::default();
        for diffuse in [false, true] {
            let mut samples = Vec::new();
            let mut reference_samples = Vec::new();
            let mut paired_reductions = Vec::new();
            let mut pair = [0.0; 2];
            let mut reference_bits = Vec::new();
            for sample in 0..140 {
                let job = GasBatchJob {
                    dimensions: (width, height, channels),
                    state: State {
                        mass: &mass,
                        enthalpy: &[],
                        velocity_x: &[],
                        velocity_y: &[],
                        solid: &solid,
                        species: &species,
                    },
                    temperature: &temperature,
                    conductivity: &conductivity,
                    mass_output: &mut output_mass,
                    temperature_output: &mut output_temperature,
                };
                let reference = sample % 4 == 0 || sample % 4 == 3;
                workspace.reference_transport = reference;
                let started = std::time::Instant::now();
                workspace.step(&job, diffuse, 0.05, &pool).unwrap();
                let elapsed = started.elapsed().as_secs_f64() * 1000.0;
                pair[usize::from(!reference)] = elapsed;
                if sample >= 20 {
                    if reference { reference_samples.push(elapsed); } else { samples.push(elapsed); }
                    if sample % 2 == 1 { paired_reductions.push(1.0 - pair[1] / pair[0]); }
                }
                if sample == 0 {
                    reference_bits = workspace.mass_output.iter().chain(&workspace.temperature_output)
                        .map(|v| v.to_bits()).collect();
                } else if sample == 1 {
                    assert!(workspace.mass_output.iter().chain(&workspace.temperature_output)
                        .zip(&reference_bits).all(|(v, bits)| v.to_bits() == *bits),
                        "paired gas benchmark outputs differ from frozen CPU reference");
                }
                std::hint::black_box(&workspace.mass_output);
            }
            let after: f64 = workspace.mass_output.iter().map(|&m| f64::from(m)).sum();
            assert!((before - after).abs() <= before * 1e-6);
            assert!(workspace.temperature_output.iter().all(|t| t.is_finite()));
            let checksum = workspace
                .mass_output
                .iter()
                .chain(&workspace.temperature_output)
                .fold(0_u64, |hash, value| {
                    hash.rotate_left(7) ^ u64::from(value.to_bits())
                });
            eprintln!("CPU gas paired samples sparse={sparse} diffuse={diffuse} reference={reference_samples:?} candidate={samples:?} reductions={paired_reductions:?}");
            samples.sort_by(f64::total_cmp);
            reference_samples.sort_by(f64::total_cmp);
            paired_reductions.sort_by(f64::total_cmp);
            eprintln!(
                "CPU gas chain 160x96x16 sparse={sparse} diffuse={diffuse} 8 threads 10 warmup pairs + 60 measured pairs median={:.6}ms p95={:.6}ms checksum={checksum:016x} reference_median={:.6}ms paired_reduction_median={:.3}%; not whole-game FPS",
                (samples[29] + samples[30]) * 0.5,
                samples[56],
                (reference_samples[29] + reference_samples[30]) * 0.5,
                (paired_reductions[29] + paired_reductions[30]) * 50.0
            );
        }
    }

    #[test]
    fn parallel_cpu_gas_matches_gpu_for_every_channel_and_thermal_stage() {
        let (width, height) = (32, 24);
        let count = width * height;
        let pool = rayon::ThreadPoolBuilder::new()
            .num_threads(2)
            .build()
            .unwrap();
        let mut gpu = GpuSimulation::new(width, height).unwrap();
        gpu.thermal_reference = true;
        let mut workspace = Workspace::default();
        for (channels, sparse) in [1, 2, 3, 9, 16].into_iter()
            .flat_map(|channels| [false, true].map(move |sparse| (channels, sparse))) {
            let solid: Vec<_> = (0..count).map(|cell| u32::from(cell % 31 == 0)).collect();
            let mass: Vec<_> = (0..count * channels)
                .map(|slot| {
                    if solid[slot / channels] != 0 || slot / channels % 29 == 0
                        || (sparse && slot % channels != (slot / channels) % channels) {
                        0.0
                    } else {
                        0.125 + ((slot * 17) % 9) as f32 * 0.125
                    }
                })
                .collect();
            let temperature: Vec<_> = (0..count)
                .map(|cell| 300.0 + (cell % 17) as f32 * 9.0)
                .collect();
            let species: Vec<_> = (0..channels)
                .map(|channel| onisim_gpu::multicomponent::SpeciesInfo {
                    element_id: channel as u16 + 1,
                    phase: 1,
                    specific_heat_capacity: 0.5 + channel as f32 * 0.25,
                    vertical_drift: (channel as f32 % 3.0 - 1.0) * 0.03,
                })
                .collect();
            let conductivity: Vec<_> = (0..channels).map(|c| 0.1 + c as f32 * 0.03).collect();
            for diffuse in [false, true] {
                let mut actual_mass = vec![0.0; mass.len()];
                let mut actual_temperature = vec![0.0; count];
                let mut expected_mass = vec![0.0; mass.len()];
                let mut expected_temperature = vec![0.0; count];
                let state = State {
                    mass: &mass,
                    enthalpy: &[],
                    velocity_x: &[],
                    velocity_y: &[],
                    solid: &solid,
                    species: &species,
                };
                workspace.reference_transport = true;
                workspace
                    .step(
                        &GasBatchJob {
                            dimensions: (width, height, channels),
                            state,
                            temperature: &temperature,
                            conductivity: &conductivity,
                            mass_output: &mut actual_mass,
                            temperature_output: &mut actual_temperature,
                        },
                        diffuse,
                        0.05,
                        &pool,
                    )
                    .unwrap();
                let cpu_reference: Vec<_> = workspace.mass_output.iter()
                    .chain(&workspace.temperature_output).map(|v| v.to_bits()).collect();
                workspace.reference_transport = false;
                workspace.step(&GasBatchJob {
                    dimensions: (width, height, channels),
                    state: State { mass: &mass, enthalpy: &[], velocity_x: &[], velocity_y: &[],
                        solid: &solid, species: &species },
                    temperature: &temperature, conductivity: &conductivity,
                    mass_output: &mut actual_mass, temperature_output: &mut actual_temperature,
                }, diffuse, 0.05, &pool).unwrap();
                for (index, (actual, expected)) in workspace.mass_output.iter()
                    .chain(&workspace.temperature_output).zip(cpu_reference).enumerate() {
                    assert_eq!(actual.to_bits(), expected,
                        "CPU reference bits: channels={channels}, sparse={sparse}, diffuse={diffuse}, output={index}");
                }
                let state = State {
                    mass: &mass,
                    enthalpy: &[],
                    velocity_x: &[],
                    velocity_y: &[],
                    solid: &solid,
                    species: &species,
                };
                gpu.step_gas(
                    width,
                    height,
                    channels,
                    &state,
                    &temperature,
                    &conductivity,
                    diffuse,
                    0.05,
                    &mut expected_mass,
                    &mut expected_temperature,
                )
                .unwrap();
                for (index, (&actual, &expected)) in workspace
                    .mass_output
                    .iter()
                    .chain(&workspace.temperature_output)
                    .zip(expected_mass.iter().chain(&expected_temperature))
                    .enumerate()
                {
                    assert!(
                        (actual - expected).abs() <= expected.abs() * 1e-5 + 1e-6,
                        "channels={channels}, sparse={sparse}, diffuse={diffuse}, output {index}: {actual} vs {expected}"
                    );
                }
            }
        }
    }
}
