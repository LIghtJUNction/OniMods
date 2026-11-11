//! Speculative batching preserves the existing region/event ordering. Inputs
//! are compared at the original consumption point after phase transitions and
//! earlier regions' components. Changed inputs are recomputed, never published.
use super::*;
use crate::gas_region_snapshot::GasRegionSnapshot;
use rayon::prelude::*;

pub(crate) struct GasBatch {
    staging: Vec<GasStaging>,
    profiling: bool,
    dispatched: usize,
    reused: usize,
    invalidated: usize,
    ready: Vec<Option<staging::GasRegion>>,
    sources: Vec<GasRegionSnapshot>,
}

impl GasBatch {
    pub(crate) fn prepare(
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        gpu: &mut crate::gpu_sim::GpuSimulation,
        diffuse: bool,
    ) -> crate::status::Result<Self> {
        static PROFILE: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
        let profiling = *PROFILE
            .get_or_init(|| std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some())
            && world.tick_seed % 17 == 0;
        let started = profiling.then(std::time::Instant::now);
        let mut staging = std::mem::take(&mut gpu.gas_batch_staging);
        staging.resize_with(world.regions.len(), GasStaging::default);
        staging.truncate(world.regions.len());
        let mut sources = std::mem::take(&mut gpu.gas_batch_sources);
        sources.resize_with(staging.len(), GasRegionSnapshot::default);
        sources.truncate(staging.len());
        if world.gas_channel_count == 0 {
            rebuild_gas_channels(world, elements);
        }
        let snapshot: &PhysicsWorld = world;
        let world = snapshot;
        let environment = crate::float_env::FloatEnv::capture();
        let prepare =
            |(index, (scratch, source)): (usize, (&mut GasStaging, &mut GasRegionSnapshot))| {
                let _environment = environment.install();
                let region = world.regions[index];
                let x = region.min_x.max(1) as usize;
                let y = region.min_y.max(1) as usize;
                let key = (
                    x,
                    y,
                    (region.max_x as usize).min(world.width).saturating_sub(x),
                    (region.max_y as usize).min(world.height).saturating_sub(y),
                );
                // Sleeping worlds do not get dense packing, uploads or dispatches.
                let sleeping = scratch.idle.get(&key).is_some_and(|old| {
                    old.thermal == diffuse
                        && old.species.iter().all(|species| {
                            elements
                                .elements
                                .get(species.element_id as usize)
                                .is_some_and(|element| {
                                    element.record.state & 3 == 1
                                        && element.record.specific_heat_capacity
                                            == species.specific_heat
                                        && element.record.thermal_conductivity
                                            == species.conductivity
                                })
                        })
                        && old.source.matches(world, key)
                });
                if sleeping || key.2 < 2 || key.3 < 2 || key.2 * key.3 < 256 {
                    return None;
                }
                let info = scratch.prepare_snapshot(world, elements, region, true, 256);
                if info.is_some() {
                    source.capture_into(world, key);
                }
                info
            };
        let work = world
            .regions
            .iter()
            .map(|r| (r.max_x - r.min_x).max(0) as usize * (r.max_y - r.min_y).max(0) as usize)
            .sum();
        let ready: Vec<_> = if let Some(pool) = crate::parallel::pool(work) {
            pool.install(|| {
                staging
                    .par_iter_mut()
                    .zip(sources.par_iter_mut())
                    .enumerate()
                    .map(prepare)
                    .collect()
            })
        } else {
            staging
                .iter_mut()
                .zip(sources.iter_mut())
                .enumerate()
                .map(prepare)
                .collect()
        };
        let packing_ms = started.map(|t| t.elapsed().as_secs_f64() * 1000.0);
        // Keep batches bounded, without excluding additional worlds. Each chunk
        // owns at most eight live GPU pipelines, including equal-sized worlds.
        let result = (|| {
            for (scratch, info) in staging.chunks_mut(8).zip(ready.chunks(8)) {
                let mut jobs: Vec<_> = scratch
                    .iter_mut()
                    .zip(info)
                    .filter_map(|(s, info)| {
                        let r = info.as_ref()?;
                        Some(crate::gpu_sim::GasBatchJob {
                            dimensions: (r.width, r.height, r.channels),
                            state: onisim_gpu::multicomponent::State {
                                mass: &s.mass,
                                enthalpy: &s.enthalpy,
                                velocity_x: &s.velocity_x,
                                velocity_y: &s.velocity_y,
                                solid: &s.solid,
                                species: &s.gpu_species,
                            },
                            temperature: &s.temperature,
                            conductivity: &s.species_conductivity,
                            mass_output: &mut s.next_mass,
                            temperature_output: &mut s.next_temperature,
                        })
                    })
                    .collect();
                gpu.step_gas_batch(&mut jobs, diffuse, FIXED_DT * 0.25)
                    .map_err(|error| {
                        crate::status::Error::new(
                            crate::status::Status::InternalFailure,
                            format!("GPU gas batch failed: {error}"),
                        )
                    })?;
            }
            Ok(())
        })();
        if let Err(error) = result {
            gpu.gas_batch_staging = staging;
            gpu.gas_batch_sources = sources;
            return Err(error);
        }
        let dispatched = ready.iter().filter(|entry| entry.is_some()).count();
        if let Some(started) = started {
            eprintln!(
                "[CycleTrim OniSim] gas batch prepare worlds={dispatched} wall={:.3}ms packing={:.3}ms",
                started.elapsed().as_secs_f64() * 1000.0,
                packing_ms.unwrap()
            );
        }
        Ok(Self {
            staging,
            ready,
            sources,
            profiling,
            dispatched,
            reused: 0,
            invalidated: 0,
        })
    }

    pub(crate) fn apply(
        &mut self,
        index: usize,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        events: &mut EventQueues,
        gpu: &mut crate::gpu_sim::GpuSimulation,
        diffuse: bool,
        solid: &mut Vec<u32>,
    ) -> crate::status::Result<bool> {
        let ready = self.ready[index].take().and_then(|info| {
            if self.sources[index].matches(world, (info.min_x, info.min_y, info.width, info.height))
            {
                self.reused += 1;
                Some(info)
            } else {
                self.invalidated += 1;
                None
            }
        });
        if let Some(info) = ready.as_ref() {
            let scratch = &mut self.staging[index];
            let region = world.regions[index];
            let key = (
                region.min_x.max(1) as usize,
                region.min_y.max(1) as usize,
                info.width,
                info.height,
            );
            scratch.idle.remove(&key);
            scratch.preparations += 1;
        }
        step_region_prepared_with_result(
            world,
            elements,
            events,
            world.regions[index],
            Some(gpu),
            diffuse,
            Some(solid),
            true,
            256,
            &mut self.staging[index],
            ready,
        )
    }

    pub(crate) fn retain(self, gpu: &mut crate::gpu_sim::GpuSimulation) {
        if self.profiling {
            eprintln!(
                "[CycleTrim OniSim] gas batch applied dispatched={} reused={} invalidated={}",
                self.dispatched, self.reused, self.invalidated
            );
        }
        gpu.gas_batch_staging = self.staging;
        gpu.gas_batch_sources = self.sources;
    }
}
