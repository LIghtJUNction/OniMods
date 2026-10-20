use std::ffi::c_void;
use std::sync::{Arc, Mutex, MutexGuard, OnceLock};

use crate::cell_material::CellMaterialQueue;
use crate::frames::{NewFrame, QueuedFrame, Scheduler, MAX_PENDING_COMMANDS};
use crate::frames_commands::EarlyCommands;
use crate::frames_data::GameData;
use crate::frames_engine::Engine;
use crate::grid::Grid;
use crate::mass_components::MassComponents;
use crate::radiation::RadiationEmitters;
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::thermal_components::ThermalComponents;
use crate::worker::Worker;
use crate::worker_shape::WorldShape;
use crate::world::PhysicsWorld;
use crate::world_messages::WorldMessages;

pub(crate) type Callback = Option<extern "C" fn(i32, *const c_void) -> i32>;
pub(crate) type SimulationWorker = Worker<QueuedFrame, GameData>;

#[derive(Default)]
pub(crate) struct State {
    pub(crate) generation: u64,
    pub(crate) exhausted: bool,
    pub(crate) active: bool,
    pub(crate) closing: bool,
    pub(crate) callback: Callback,
    pub(crate) grid: Option<Grid>,
    pub(crate) elements: Option<Arc<ElementTable>>,
    pub(crate) diseases: Option<Arc<DiseaseTable>>,
    pub(crate) interactions: Vec<crate::gas::GasObliteration>,
    // Before Start, lifecycle handlers own the real world here. Afterwards only
    // the Engine owns mutable arrays; submission validators use shape alone.
    pub(crate) world: Option<PhysicsWorld>,
    pub(crate) shape: Option<WorldShape>,
    pub(crate) engine: Option<Arc<Mutex<Engine>>>,
    pub(crate) worker: Option<SimulationWorker>,
    pub(crate) scheduler: Scheduler,
    pub(crate) early: EarlyCommands,
    pub(crate) material: CellMaterialQueue,
    pub(crate) world_messages: WorldMessages,
    pub(crate) thermal: ThermalComponents,
    pub(crate) mass: MassComponents,
    pub(crate) radiation_emitters: RadiationEmitters,
    pub(crate) queued_component_commands: usize,
    pub(crate) borrowed_ranges: Vec<(usize, usize)>,
    pub(crate) save_image: Option<Vec<u8>>,
    pub(crate) fault: Option<String>,
}

fn singleton() -> &'static Mutex<State> {
    static STATE: OnceLock<Mutex<State>> = OnceLock::new();
    STATE.get_or_init(|| Mutex::new(State::default()))
}

pub(crate) fn lock() -> Result<MutexGuard<'static, State>> {
    singleton()
        .lock()
        .map_err(|_| Error::new(Status::InternalFailure, "native session mutex is poisoned"))
}

pub(crate) fn shutdown() -> Result<()> {
    let (mut worker, outcome) = {
        let mut state = singleton()
            .lock()
            .unwrap_or_else(|poison| poison.into_inner());
        state.closing = true;
        let worker = state.worker.take();
        let outcome = state.clear_session();
        (worker, outcome)
    };
    // Physics never needs the submission mutex. Drop that mutex before joining
    // so a pending Prepare can wake, observe invalidation, and release its lease.
    let joined = worker.as_mut().map_or(Ok(()), SimulationWorker::shutdown);
    let mut state = singleton()
        .lock()
        .unwrap_or_else(|poison| poison.into_inner());
    state.closing = false;
    singleton().clear_poison();
    outcome.and(joined)
}

pub(crate) fn reset_initialize(callback: Callback) -> Result<()> {
    // Official CleanUp destroys tables as well as the old Sim and FrameSync.
    // A prior worker fault must not prevent a fresh, fully cleaned session.
    let _ = shutdown();
    lock()?.initialize(callback)
}

impl State {
    pub(crate) fn overlaps_native_storage(&self, start: usize, length: usize) -> bool {
        self.grid
            .as_ref()
            .is_some_and(|grid| grid.overlaps_storage(start, length))
            || self.borrowed_ranges.iter().any(|&(pointer, bytes)| {
                bytes != 0 && crate::bytes::overlaps(start, length, pointer, bytes)
            })
            || self.save_image.as_ref().is_some_and(|image| {
                image.capacity() != 0
                    && crate::bytes::overlaps(
                        start,
                        length,
                        image.as_ptr() as usize,
                        image.capacity(),
                    )
            })
    }

    pub(crate) fn next_generation(&self) -> Result<u64> {
        if self.exhausted {
            return Err(Error::new(
                Status::ResourceLimit,
                "generation exhausted; restart process",
            ));
        }
        self.generation.checked_add(1).ok_or_else(|| {
            Error::new(
                Status::ResourceLimit,
                "generation exhausted; restart process",
            )
        })
    }

    pub(crate) fn require_active(&self) -> Result<()> {
        if !self.active || self.closing {
            return Err(Error::new(
                Status::InvalidState,
                "SIM_Initialize is required",
            ));
        }
        Ok(())
    }

    pub(crate) fn require_conduit_update(&self) -> Result<()> {
        self.require_active()?;
        if self.save_image.is_some() {
            return Err(Error::new(Status::InvalidState, "EndSave is required"));
        }
        if let Some(fault) = &self.fault {
            return Err(Error::new(Status::InvalidState, fault.clone()));
        }
        if let Some(worker) = &self.worker {
            worker.handle().check()?;
        }
        Ok(())
    }

    pub(crate) fn queue_conduit_energy(&mut self, records: &[[u8; 16]]) -> Result<()> {
        self.require_conduit_update()?;
        if self
            .queued_component_commands
            .checked_add(records.len())
            .is_none_or(|length| length > MAX_PENDING_COMMANDS)
        {
            return Err(Error::new(
                Status::ResourceLimit,
                "pending command limit exceeded",
            ));
        }
        self.thermal.queue_energy_batch(records)?;
        self.queued_component_commands += records.len();
        Ok(())
    }

    pub(crate) fn grid(&self) -> Result<&Grid> {
        self.require_active()?;
        self.grid.as_ref().ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                "Allocate or Initialize grid message is required",
            )
        })
    }

    pub(crate) fn initialized_grid(&self) -> Result<&Grid> {
        let grid = self.grid()?;
        if !grid.initialized {
            return Err(Error::new(
                Status::InvalidState,
                "grid initialization message is required",
            ));
        }
        Ok(grid)
    }

    pub(crate) fn with_world_mut<T>(
        &mut self,
        apply: impl FnOnce(&mut PhysicsWorld) -> Result<T>,
    ) -> Result<T> {
        if let Some(engine) = &self.engine {
            let mut engine = engine.lock().map_err(|_| {
                Error::new(
                    Status::InternalFailure,
                    "simulation Engine mutex is poisoned",
                )
            })?;
            apply(&mut engine.world)
        } else {
            apply(self.world.as_mut().ok_or_else(|| {
                Error::new(Status::InvalidState, "physics world is not allocated")
            })?)
        }
    }

    pub(crate) fn start_worker(&mut self, legacy: Option<u16>) -> Result<*mut c_void> {
        let world = self.world.as_mut().unwrap();
        self.shape = Some(WorldShape::from_world(world));
        if let Some(boundary) = legacy {
            world.sync_read_from_write();
            world.settle_thermal_boundaries(boundary);
        }
        world.initialize_boundary();
        world.sync_read_from_write();
        let grid = self.grid.as_mut().unwrap();
        world.publish_to(grid);
        let mut output = GameData::new(grid)?;
        let mut old = GameData::new(grid)?;
        output.grid.started = true;
        old.grid.started = true;
        let initial_pointers = old.presentation.pointers();
        let mut initial_ranges = Vec::new();
        initial_ranges.try_reserve_exact(34).map_err(|_| {
            Error::new(
                Status::ResourceLimit,
                "could not reserve borrowed buffer ranges",
            )
        })?;
        old.fill_borrowed_ranges(&mut initial_ranges);
        let engine = Arc::new(Mutex::new(Engine::new(
            self.world.take().unwrap(),
            Arc::clone(self.elements.as_ref().unwrap()),
            self.diseases.clone(),
            self.thermal.take_engine_state(),
            self.mass.take_engine_state(),
            self.radiation_emitters.take_engine_state(),
        )));
        let engine_for_worker = Arc::clone(&engine);
        let worker = SimulationWorker::spawn(
            output,
            old,
            Vec::new(),
            move |frames, regions, output, old| {
                engine_for_worker
                    .lock()
                    .map_err(|_| {
                        Error::new(
                            Status::InternalFailure,
                            "simulation Engine mutex is poisoned",
                        )
                    })?
                    .run_batch(frames, regions, output, old)
            },
        );
        match worker {
            Ok(worker) => {
                self.engine = Some(engine);
                self.worker = Some(worker);
                self.borrowed_ranges = initial_ranges;
                let grid = self.grid.as_mut().unwrap();
                for (index, pointer) in initial_pointers.into_iter().enumerate() {
                    grid.set_texture_pointer(index, pointer);
                }
                grid.started = true;
                Ok(grid.descriptor() as *mut c_void)
            }
            Err(error) => {
                if let Ok(engine) = Arc::try_unwrap(engine) {
                    self.world = Some(
                        engine
                            .into_inner()
                            .unwrap_or_else(|poison| poison.into_inner())
                            .world,
                    );
                } else {
                    self.fault = Some("worker spawn failed with retained Engine ownership".into());
                }
                Err(error)
            }
        }
    }

    pub(crate) fn submit_frame(&mut self, frame: NewFrame) -> Result<()> {
        let handle = self
            .worker
            .as_ref()
            .ok_or_else(|| {
                Error::new(
                    Status::InvalidState,
                    "Start is required before NewGameFrame",
                )
            })?
            .handle();
        handle.submit_with(|| {
            let next_slot = self.scheduler.free_slots.pop().unwrap_or_default();
            let early = std::mem::replace(&mut self.early, next_slot);
            let queued = QueuedFrame {
                elapsed: frame.elapsed,
                early,
                material: self.material.take_pending(),
                world: self.world_messages.take_pending(),
                thermal: self.thermal.take_pending(),
                mass: self.mass.take_pending(),
                radiation: self.radiation_emitters.take_pending(),
            };
            self.queued_component_commands = 0;
            (queued, frame.regions)
        })
    }

    pub(crate) fn publish_ready(&mut self, data: &mut GameData) -> Result<*mut c_void> {
        self.scheduler
            .free_slots
            .try_reserve(data.retired_slots.len())
            .map_err(|_| {
                Error::new(
                    Status::ResourceLimit,
                    "could not recycle frame command slots",
                )
            })?;
        data.events.publish_to(&mut data.grid)?;
        for (index, pointer) in data.presentation.pointers().into_iter().enumerate() {
            data.grid.set_texture_pointer(index, pointer);
        }
        data.fill_borrowed_ranges(&mut self.borrowed_ranges);
        let grid = self.grid.as_mut().unwrap();
        grid.copy_from(&data.grid);
        self.scheduler.free_slots.append(&mut data.retired_slots);
        Ok(grid.descriptor() as *mut c_void)
    }

    pub(crate) fn finish_prepare(&mut self, data: &mut GameData) -> Result<*mut c_void> {
        let descriptor = self.publish_ready(data)?;
        self.scheduler.completed_frames = 0;
        Ok(descriptor)
    }

    pub(crate) fn initialize(&mut self, callback: Callback) -> Result<()> {
        if self.active || self.closing {
            return Err(Error::new(
                Status::InvalidState,
                "a native session already exists",
            ));
        }
        self.generation = self.next_generation()?;
        self.active = true;
        self.callback = callback;
        Ok(())
    }

    fn clear_session(&mut self) -> Result<()> {
        let changed = self.active
            || self.grid.is_some()
            || self.elements.is_some()
            || self.diseases.is_some();
        self.grid = None;
        self.elements = None;
        self.diseases = None;
        self.interactions.clear();
        self.callback = None;
        self.active = false;
        self.world = None;
        self.shape = None;
        self.engine = None;
        self.scheduler = Scheduler::default();
        self.early = EarlyCommands::default();
        self.material = CellMaterialQueue::default();
        self.world_messages = WorldMessages::default();
        self.thermal = ThermalComponents::default();
        self.mass = MassComponents::default();
        self.radiation_emitters = RadiationEmitters::default();
        self.queued_component_commands = 0;
        self.borrowed_ranges.clear();
        self.save_image = None;
        self.fault = None;
        if changed {
            if let Some(generation) = self.generation.checked_add(1) {
                self.generation = generation;
            } else {
                self.exhausted = true;
                return Err(Error::new(
                    Status::ResourceLimit,
                    "shutdown released state; generation exhausted; restart process",
                ));
            }
        }
        Ok(())
    }
}
