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
        Self {
            world,
            elements,
            diseases,
            thermal,
            mass,
            radiation,
            residual_time: 0.0,
        }
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
            .track_grid_changes(self.world.width * self.world.height);
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
                self.world.sync_read_from_write();
                self.update_data_lists(&mut output.events)?;
            } else {
                let diseases = self.diseases.as_deref().ok_or_else(|| {
                    Error::new(Status::InvalidState, "physics requires registered diseases")
                })?;
                let mut systems = crate::simulation_step::StepSystems {
                    mass: &mut self.mass,
                    thermal: &mut self.thermal,
                    radiation: &mut self.radiation,
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

        // CopySimDataToGame publishes/clears accumulated component energy only
        // once for the whole Step, rather than after every processed frame.
        self.thermal.publish(&mut output.events)?;
        self.mass
            .publish_emitted_mass(&mut output.events, self.world.vacuum_element)?;
        output.events.consolidate_spawn_ore();
        output
            .events
            .resolve_grid_changes(&old.grid, &self.world, &self.elements)?;
        self.world.publish_to(&mut output.grid);
        // +0x58 is copied to GameData before its global periodic reset. Empty
        // and zero-elapsed batches advance this counter exactly like Step.
        if ACCUMULATED_FLOW_PUBLICATIONS
            .try_update(Ordering::Relaxed, Ordering::Relaxed, |count| {
                Some((count + 1) % 15)
            })
            .is_ok_and(|previous| previous == 14)
        {
            self.world.accumulated_flow.fill(0.0);
        }
        output.presentation.update(&self.world, &self.elements)?;
        output.grid.set_num_frames(frame_count);
        output.events.publish_to(&mut output.grid)?;
        for (index, pointer) in output.presentation.pointers().into_iter().enumerate() {
            output.grid.set_texture_pointer(index, pointer);
        }
        // Prepare writes incoming visibility into the other, old GameData via
        // ReadyLease::release_with. The native Step swaps output's existing
        // visibility storage here; copying old.visible would remove its delay.
        std::mem::swap(&mut self.world.active_mask, &mut output.visible);
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
