//! Simulation-only state. Submission never borrows this Engine to seal frames.

use std::sync::Arc;
use std::sync::atomic::{AtomicU8, Ordering};

use crate::frames::QueuedFrame;
use crate::frames_data::GameData;
use crate::mass_components::MassComponents;
use crate::radiation::RadiationEmitters;
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::thermal_components::ThermalComponents;
use crate::world::{PhysicsWorld, Region};

// Official ELF 0xc00d8 is process-global BSS. Its only code references are
// CopySimDataToGame's increment and fifteen-publication reset; Initialize and
// Shutdown do not reset the cycle.
static ACCUMULATED_FLOW_PUBLICATIONS: AtomicU8 = AtomicU8::new(0);

pub(crate) struct Engine {
    pub(crate) world: PhysicsWorld,
    pub(crate) elements: Arc<ElementTable>,
    pub(crate) diseases: Option<Arc<DiseaseTable>>,
    pub(crate) thermal: ThermalComponents,
    pub(crate) mass: MassComponents,
    pub(crate) radiation: RadiationEmitters,
    pub(crate) residual_time: f32,
    pub(crate) gpu: Option<crate::gpu_sim::GpuSimulation>,
    pub(crate) game_element_events_only: bool,
    // Both GameData buffers must have seen the same unchanged world before
    // their derived presentation can be reused on alternating idle batches.
    empty_publication_streak: u8,
    idle_frames_previous: bool,
    publication_profile: [u32; 4],
    zero_frame_command_mask: u8,
    zero_command_batches: [u32; 6],
    zero_early_mask: u8,
}

impl Engine {
    pub(crate) fn new(
        world: PhysicsWorld,
        elements: Arc<ElementTable>,
        diseases: Option<Arc<DiseaseTable>>,
        thermal: ThermalComponents,
        mass: MassComponents,
        radiation: RadiationEmitters,
    ) -> Self {
        let gpu = if std::env::var("CYCLETRIM_ONISIM_GPU").map(|v| v != "0").unwrap_or(true) {
            match crate::gpu_sim::GpuSimulation::new(world.stride, world.padded_height) {
                Ok(gpu) => Some(gpu),
                Err(error) => {
                    eprintln!("[CycleTrim OniSim] GPU initialization failed: {error}");
                    None
                }
            }
        } else {
            None
        };
        Self {
            world,
            elements,
            diseases,
            thermal,
            mass,
            radiation,
            residual_time: 0.0,
            gpu,
            game_element_events_only: false,
            empty_publication_streak: 0,
            idle_frames_previous: false,
            publication_profile: [0; 4],
            zero_frame_command_mask: 0,
            zero_command_batches: [0; 6],
            zero_early_mask: 0,
        }
    }

    pub(crate) fn invalidate_empty_publications(&mut self) {
        self.empty_publication_streak = 0;
    }

    /// One official Step/CopySimDataToGame batch. The worker calls this with
    /// its two unpublished GameData allocations, without holding FFI State.
    pub(crate) fn run_batch(
        &mut self,
        frames: Vec<QueuedFrame>,
        regions: &[Region],
        output: &mut GameData,
        old: &GameData,
    ) -> Result<()> {
        let empty_batch = frames.is_empty();
        let idle_frames = !empty_batch && frames.iter().all(|frame| {
            frame.elapsed == 0.0 && frame.early.only_debug() && frame.material.is_empty()
                && frame.world.is_empty() && frame.thermal.is_empty() && frame.mass.is_empty()
                && frame.radiation.is_empty()
        });
        // Empty batches do not synchronize world.read, whereas zero-time
        // frames do. Fill both publication buffers again when switching modes.
        if idle_frames != self.idle_frames_previous { self.empty_publication_streak = 0; }
        self.idle_frames_previous = idle_frames;
        let unchanged_batch = empty_batch || idle_frames;
        let reuse_derived = unchanged_batch && self.empty_publication_streak >= 2;
        static PROFILE: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
        let mut profile_batch = false;
        if *PROFILE.get_or_init(|| std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some()) {
            let zero_with_commands = !empty_batch && !idle_frames && frames.iter().all(|frame| frame.elapsed == 0.0);
            let category = if empty_batch { 0 } else if idle_frames { 1 } else if zero_with_commands { 2 } else { 3 };
            self.publication_profile[category] += 1;
            profile_batch = self.publication_profile.iter().sum::<u32>() % 64 == 1;
            if zero_with_commands {
                let mut batch_mask = 0u8;
                for frame in &frames {
                    self.zero_early_mask |= frame.early.profile_mask();
                    batch_mask |= u8::from(frame.early.len() != 0)
                        | (u8::from(!frame.material.is_empty()) << 1)
                        | (u8::from(!frame.world.is_empty()) << 2)
                        | (u8::from(!frame.thermal.is_empty()) << 3)
                        | (u8::from(!frame.mass.is_empty()) << 4)
                        | (u8::from(!frame.radiation.is_empty()) << 5);
                }
                self.zero_frame_command_mask |= batch_mask;
                for bit in 0..6 { self.zero_command_batches[bit] += u32::from(batch_mask & (1 << bit) != 0); }
            }
            if self.publication_profile.iter().sum::<u32>() >= 256 {
                eprintln!("[CycleTrim OniSim] publication profile batches: empty={} idle_zero={} zero_with_commands={} advancing={} zero_command_mask={:#04x} (bits: early=1 material=2 world=4 thermal=8 mass=16 radiation=32)",
                    self.publication_profile[0], self.publication_profile[1], self.publication_profile[2], self.publication_profile[3], self.zero_frame_command_mask);
                eprintln!("[CycleTrim OniSim] zero-time command batches: early={} material={} world={} thermal={} mass={} radiation={}",
                    self.zero_command_batches[0], self.zero_command_batches[1], self.zero_command_batches[2],
                    self.zero_command_batches[3], self.zero_command_batches[4], self.zero_command_batches[5]);
                eprintln!("[CycleTrim OniSim] zero-time early mask={:#04x} (bits: insulation=1 strength=2 energy=4 properties=8 consume_disease=16 modify_disease=32 backwall=64 debug=128)", self.zero_early_mask);
                self.publication_profile = [0; 4];
                self.zero_frame_command_mask = 0;
                self.zero_command_batches = [0; 6];
                self.zero_early_mask = 0;
            }
        }
        // Only sampled diagnostic batches read the clock. Simulation wall time
        // includes GPU waits; publication stages below operate on CPU storage.
        let batch_started = profile_batch.then(std::time::Instant::now);
        let mut profile_mark = batch_started;
        let mut lap = || match profile_mark.as_mut() {
            Some(start) => {
                let now = std::time::Instant::now();
                let elapsed = now.duration_since(*start).as_secs_f64() * 1000.0;
                *start = now;
                elapsed
            }
            None => 0.0,
        };
        if reuse_derived {
            static LOG_EMPTY: std::sync::Once = std::sync::Once::new();
            static LOG_ZERO: std::sync::Once = std::sync::Once::new();
            if idle_frames { LOG_ZERO.call_once(|| eprintln!("[CycleTrim OniSim] Reusing derived data for zero-time frames")); }
            else { LOG_EMPTY.call_once(|| eprintln!("[CycleTrim OniSim] Reusing derived data for empty batches")); }
        }
        if !unchanged_batch { self.invalidate_empty_publications(); }
        if !output.retired_slots.is_empty() {
            return Err(Error::new(
                Status::InternalFailure,
                "Prepare did not recycle completed frame slots",
            ));
        }
        let frame_count = i32::try_from(frames.len()).map_err(|_| {
            Error::new(
                Status::ResourceLimit,
                "completed frame count exceeds official ABI",
            )
        })?;
        output
            .retired_slots
            .try_reserve(frames.len())
            .map_err(|_| {
                Error::new(
                    Status::ResourceLimit,
                    "could not reserve completed frame slots",
                )
            })?;
        self.world
            .regions
            .try_reserve(regions.len().saturating_sub(self.world.regions.len()))
            .map_err(|_| Error::new(Status::ResourceLimit, "could not reserve active regions"))?;
        self.world.regions.clear();
        self.world.regions.extend_from_slice(regions);
        // Sim::Main releases conduit handles once before Step, including an
        // empty batch. Releasing per ProcessFrame would change generations.
        crate::conduit_ffi::release_queued_handles()?;
        output.events.clear();
        output
            .events
            .defer_grid_changes(self.world.width * self.world.height);
        // Step clears flow once before all ProcessNextFrame calls, including
        // when no game frames are available. Substeps never clear these flows.
        self.world.flow.fill([0.0; 4]);

        if frames.is_empty() {
            self.update_data_lists(&mut output.events)?;
        }
        for mut frame in frames {
            let (ticks, residual) = crate::frames::subdivide(frame.elapsed, self.residual_time)?;
            self.process_commands(&mut frame, &mut output.events)?;
            if ticks == 0 {
                if !reuse_derived { self.world.sync_read_from_write(); }
                self.update_data_lists(&mut output.events)?;
            } else {
                let diseases = self.diseases.as_deref().ok_or_else(|| {
                    Error::new(Status::InvalidState, "physics requires registered diseases")
                })?;
                let mut systems = crate::simulation_step::StepSystems {
                    mass: &mut self.mass,
                    thermal: &mut self.thermal,
                    radiation: &mut self.radiation,
                    gpu: self.gpu.as_mut(),
                };
                // The first preparation replaces both the frame preflight and
                // its identical first-substep check. Its borrow prevents any
                // intervening world/table mutation. Later substeps still check
                // their changed state exactly as before.
                crate::simulation_step::PreparedStep::new(
                    &mut self.world,
                    &self.elements,
                    diseases,
                    frame.elapsed,
                )?
                .run(&mut output.events, &mut systems)?;
                for _ in 1..ticks {
                    crate::simulation_step::PreparedStep::new(
                        &mut self.world,
                        &self.elements,
                        diseases,
                        crate::simulation_step::FIXED_DT,
                    )?
                    .run(&mut output.events, &mut systems)?;
                }
                self.residual_time = residual;
            }
            // Retain the drained slot's persistent DebugProperties record.
            // Prepare returns these owned slots to submission's native LIFO.
            output.retired_slots.push(frame.early);
        }
        let simulation_ms = lap();

        // CopySimDataToGame publishes/clears accumulated component energy only
        // once for the whole Step, rather than after every processed frame.
        self.thermal.publish(&mut output.events)?;
        self.mass
            .publish_emitted_mass(&mut output.events, self.world.vacuum_element)?;
        output.events.consolidate_spawn_ore();
        output.multicomponent_unchanged = reuse_derived;
        let world = &self.world;
        let events = &mut output.events;
        let grid = Arc::make_mut(&mut output.grid);
        let elements = &self.elements;
        let presentation = &mut output.presentation;
        let multicomponent = &mut output.multicomponent;
        let pool = crate::parallel::pool(world.width * world.height);
        let environment = crate::float_env::FloatEnv::capture();
        let game_element_events_only = self.game_element_events_only;
        let publish_grid = || {
            let started = profile_batch.then(std::time::Instant::now);
            let resolve = || -> Result<()> {
                events.resolve_grid_changes(&old.grid, world, elements)?;
                if game_element_events_only {
                    let before = events.substance_change_info.len();
                    // Game.StepTheSim uses this list only to refresh Grid.Element.
                    // Mass/temperature are already exposed through the bound arrays.
                    events.substance_change_info.retain(|event|
                        event.old_elem_idx != event.new_elem_idx);
                    if profile_batch {
                        eprintln!("[CycleTrim OniSim] game element events before={before} after={}",
                            events.substance_change_info.len());
                    }
                }
                Ok(())
            };
            let publish = || {
                if reuse_derived { world.publish_accumulated_flow_to(grid); }
                else { world.publish_to(grid); }
            };
            let result = if let Some(pool) = pool {
                pool.install(|| rayon::join(resolve, publish)).0
            } else {
                let mut resolve = resolve;
                let mut publish = publish;
                let result = resolve();
                if result.is_ok() { publish(); }
                result
            };
            (result, started.map_or(0.0, |t| t.elapsed().as_secs_f64() * 1000.0))
        };
        let publish_derived = || {
            if reuse_derived { return (Ok(()), 0.0, 0.0, 0.0); }
            let started = profile_batch.then(std::time::Instant::now);
            let textures = || {
                let _environment = environment.install();
                let started = profile_batch.then(std::time::Instant::now);
                let result = presentation.update(world, elements);
                (result, started.map_or(0.0, |t| t.elapsed().as_secs_f64() * 1000.0))
            };
            let species = || {
                let _environment = environment.install();
                let started = profile_batch.then(std::time::Instant::now);
                // The authoritative world is frozen for this publication.
                // Species records need no intermediate Grid copy.
                Arc::make_mut(multicomponent).update_from_simulation(elements, world);
                started.map_or(0.0, |t| t.elapsed().as_secs_f64() * 1000.0)
            };
            let ((result, textures_ms), species_ms) = if let Some(pool) = pool {
                pool.install(|| rayon::join(textures, species))
            } else {
                let mut textures = textures;
                let mut species = species;
                (textures(), species())
            };
            (result, textures_ms, species_ms,
                started.map_or(0.0, |t| t.elapsed().as_secs_f64() * 1000.0))
        };
        let ((grid_result, grid_ms), (derived_result, textures_ms, species_ms, derived_ms)) =
            if let Some(pool) = pool {
                pool.install(|| rayon::join(publish_grid, publish_derived))
            } else {
                let mut publish_grid = publish_grid;
                let mut publish_derived = publish_derived;
                (publish_grid(), publish_derived())
            };
        grid_result?;
        derived_result?;
        let publication_ms = lap();
        // The accumulated-flow view is copied before its process-global reset.
        if ACCUMULATED_FLOW_PUBLICATIONS
            .try_update(Ordering::Relaxed, Ordering::Relaxed, |count| Some((count + 1) % 15))
            .is_ok_and(|previous| previous == 14) {
            self.world.accumulated_flow.fill(0.0);
        }
        let grid = Arc::make_mut(&mut output.grid);
        grid.set_num_frames(frame_count);
        output.events.publish_to(grid)?;
        for (index, pointer) in output.presentation.pointers().into_iter().enumerate() {
            grid.set_texture_pointer(index, pointer);
        }
        // Prepare writes incoming visibility into the other, old GameData via
        // ReadyLease::release_with. The native Step swaps output's existing
        // visibility storage here; copying old.visible would remove its delay.
        std::mem::swap(&mut self.world.active_mask, &mut output.visible);
        if unchanged_batch { self.empty_publication_streak = self.empty_publication_streak.saturating_add(1); }
        if profile_batch {
            let finalize_ms = lap();
            let total_ms = batch_started.unwrap().elapsed().as_secs_f64() * 1000.0;
            let gpu_active = self.gpu.is_some();
            let species_reused = if reuse_derived { output.grid.len() } else { output.multicomponent.reused_cells };
            eprintln!("[CycleTrim OniSim] batch profile frames={frame_count} gpu_active={gpu_active} reuse_derived={reuse_derived} simulation_wall={simulation_ms:.3}ms grid_publish_wall={grid_ms:.3}ms textures_wall={textures_ms:.3}ms species_publish_wall={species_ms:.3}ms derived_wall={derived_ms:.3}ms publication_wall={publication_ms:.3}ms finalize_wall={finalize_ms:.3}ms measured_wall={total_ms:.3}ms species_reused_cells={species_reused}");
        }
        Ok(())
    }

    fn update_data_lists(&mut self, events: &mut crate::events::EventQueues) -> Result<()> {
        self.thermal.update_data_list_only()?;
        self.mass.update_data_lists(events)
    }

    fn process_commands(
        &mut self,
        frame: &mut QueuedFrame,
        events: &mut crate::events::EventQueues,
    ) -> Result<()> {
        let world = &mut self.world;
        let elements = self.elements.as_ref();
        let registered = self.diseases.as_deref();
        // Paused no-germ commands and Dig are valid before disease registration.
        // The material helpers receive a genuinely empty table in that case;
        // commands/current cells with actual germs cannot use that path.
        let absent = DiseaseTable {
            element_count: elements.len(),
            ..DiseaseTable::default()
        };
        if registered.is_none()
            && !frame.material.is_empty()
            && world
                .write
                .disease_idx
                .iter()
                .any(|&disease| disease != u8::MAX)
        {
            return Err(Error::new(
                Status::InvalidState,
                "material commands on germ-bearing cells require registered diseases",
            ));
        }
        let diseases = registered.unwrap_or(&absent);
        frame
            .early
            .process_before_cells(world, elements, registered, events)?;
        frame.material.process_mass(
            world,
            elements,
            diseases,
            events,
            self.mass.flood_scratch(),
        )?;
        frame
            .early
            .process_disease(world, elements, registered, events)?;
        self.radiation
            .process_cell_pending(&mut frame.radiation, world, events)?;
        frame
            .material
            .process_dig(world, elements, diseases, events)?;
        frame
            .material
            .process_modifications(world, elements, diseases, events)?;
        crate::world_messages::process_pending(&mut frame.world, world)?;
        self.thermal
            .process_before_backwall(&mut frame.thermal, world, elements, events)?;
        frame.early.process_backwall(world, elements, events)?;
        self.thermal
            .process_after_backwall(&mut frame.thermal, world, elements, events)?;
        self.mass
            .process_element_pending(&mut frame.mass, world, events)?;
        self.radiation
            .process_emitter_pending(&mut frame.radiation, world, events)?;
        self.mass
            .process_disease_pending(&mut frame.mass, world, events)?;
        frame.early.process_tail(world)
    }
}
