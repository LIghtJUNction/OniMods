use crate::bytes::{dimensions, f32at, i32at, invalid, nonnegative, u16at};
use crate::frames::{NewFrame, Replacement, MAX_PENDING_COMMANDS};
use crate::grid::Grid;
use crate::state::State;
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

pub(crate) const ALLOCATE: i32 = 1_092_408_308;
pub(crate) const INITIALIZE: i32 = 2_062_421_945;
pub(crate) const START: i32 = -931_446_686;
pub(crate) const MODIFY: i32 = -1_252_920_804;
pub(crate) const ELEMENTS: i32 = 1_108_437_482;
pub(crate) const DISEASES: i32 = 825_301_935;
pub(crate) const FRAME: i32 = -775_326_397;
pub(crate) const PREPARE: i32 = 1_078_620_451;
pub(crate) const LOAD: i32 = -672_538_170;

pub(crate) enum Action {
    Allocate(Grid, PhysicsWorld, u64),
    Initialize(Grid, PhysicsWorld, u64),
    Start(Option<u16>),
    Modify(Replacement),
    Elements(ElementTable),
    Diseases(DiseaseTable),
    Frame(NewFrame),
    Prepare(Vec<u8>),
}

pub(crate) struct Preview {
    generation: u64,
    width: i32,
    height: i32,
    count: usize,
    allocated: bool,
    initialized: bool,
    started: bool,
    element_count: Option<usize>,
    disease_element_count: Option<usize>,
    grid_bytes: usize,
    completed_frames: i32,
    pending_commands: usize,
}

impl Preview {
    pub(crate) fn from_state(state: &State) -> Self {
        let grid = state.grid.as_ref();
        Self {
            generation: state.generation,
            width: grid.map_or(0, |grid| grid.width),
            height: grid.map_or(0, |grid| grid.height),
            count: grid.map_or(0, Grid::len),
            allocated: grid.is_some(),
            initialized: grid.is_some_and(|grid| grid.initialized),
            started: grid.is_some_and(|grid| grid.started),
            element_count: state.elements.as_ref().map(ElementTable::len),
            disease_element_count: state.diseases.as_ref().map(|table| table.element_count),
            grid_bytes: 0,
            completed_frames: state.scheduler.completed_frames,
            pending_commands: state.scheduler.cell_modifications.len()
                + state.queued_component_commands,
        }
    }

    fn lifecycle(&mut self, width: i32, height: i32, initialized: bool) -> Result<u64> {
        if self.pending_commands != 0 {
            return Err(Error::new(
                Status::InvalidState,
                "finish queued commands before changing grid lifecycle",
            ));
        }
        if self.started {
            return Err(Error::new(
                Status::InvalidState,
                "grid lifecycle cannot change after Start",
            ));
        }
        if initialized && self.allocated && (self.width != width || self.height != height) {
            return Err(Error::new(
                Status::InvalidState,
                "Initialize dimensions differ from Allocate",
            ));
        }
        let count = dimensions(width, height)?;
        // Planning owns replacement grids before any mutation. Bound their total
        // allocation so a small batch of Allocate messages cannot exhaust RAM.
        self.grid_bytes = self
            .grid_bytes
            .checked_add(count * 49 + (width as usize + 2) * (height as usize + 2) * 90)
            .ok_or_else(|| Error::new(Status::ResourceLimit, "batch grid allocation overflow"))?;
        if self.grid_bytes > 512 * 1024 * 1024 {
            return Err(Error::new(
                Status::ResourceLimit,
                "batch planned grid/world storage exceeds 512 MiB",
            ));
        }
        self.generation = self.generation.checked_add(1).ok_or_else(|| {
            Error::new(
                Status::ResourceLimit,
                "generation exhausted; restart process",
            )
        })?;
        self.width = width;
        self.height = height;
        self.count = count;
        self.allocated = true;
        self.initialized = initialized;
        self.completed_frames = 0;
        self.pending_commands = 0;
        Ok(self.generation)
    }
}

fn flags(bytes: &[u8], at: usize) -> Result<(u8, u8)> {
    let pair = (bytes[at], bytes[at + 1]);
    if pair.0 > 1 || pair.1 > 1 {
        return Err(invalid("radiation/headless flags must be 0 or 1"));
    }
    Ok(pair)
}

fn validate_image(bytes: &[u8]) -> Result<(i32, i32, u8, u8)> {
    if bytes.len() < 14 {
        return Err(invalid("Initialize message requires a 14-byte prefix"));
    }
    let (width, height) = (i32at(bytes, 0), i32at(bytes, 4));
    let count = dimensions(width, height)?;
    if bytes.len() != 14 + count * 40 {
        return Err(invalid(
            "Initialize message length does not match grid dimensions",
        ));
    }
    let (radiation, headless) = flags(bytes, 12)?;
    let diseases = 14 + count * 16;
    let backwalls = diseases + count * 12;
    for index in 0..count {
        let cell = 14 + index * 16;
        let disease = diseases + index * 12;
        let backwall = backwalls + index * 12;
        if !nonnegative(f32at(bytes, cell + 8))
            || !nonnegative(f32at(bytes, cell + 12))
            || i32at(bytes, disease + 4) < 0
            || !f32at(bytes, disease + 8).is_finite()
            || !nonnegative(f32at(bytes, backwall + 4))
            || !nonnegative(f32at(bytes, backwall + 8))
        {
            return Err(invalid(format!(
                "Initialize contains invalid numeric values at cell {index}"
            )));
        }
    }
    Ok((width, height, radiation, headless))
}

fn replacement(bytes: &[u8], preview: &Preview) -> Result<Replacement> {
    if bytes.len() != 28 {
        return Err(invalid("ModifyCell message must be 28 bytes"));
    }
    if !preview.initialized {
        return Err(Error::new(
            Status::InvalidState,
            "ModifyCell requires an initialized grid",
        ));
    }
    let index = i32at(bytes, 0);
    if index < 0 || index as usize >= preview.count {
        return Err(invalid("ModifyCell index is outside the grid"));
    }
    if i32at(bytes, 4) != -1 || bytes[22] != 1 || bytes[24] != 1 {
        return Err(Error::new(
            Status::Unsupported,
            "only absolute Replace=1/addSubType=1 with callbackIdx=-1 is implemented",
        ));
    }
    if u16at(bytes, 20) == u16::MAX {
        return Err(Error::new(
            Status::Unsupported,
            "ModifyCell sentinel element semantics are unknown",
        ));
    }
    let temperature = f32at(bytes, 8);
    let mass = f32at(bytes, 12);
    let disease_count = i32at(bytes, 16);
    if !nonnegative(temperature) || !nonnegative(mass) || disease_count < 0 {
        return Err(invalid(
            "ModifyCell temperature, mass and disease count must be finite and nonnegative",
        ));
    }
    if preview
        .element_count
        .is_none_or(|length| u16at(bytes, 20) as usize >= length)
    {
        return Err(invalid(
            "ModifyCell element index is not in the registered table",
        ));
    }
    if mass != 0.0 && temperature <= 0.0 {
        return Err(invalid(
            "nonzero ModifyCell mass requires positive temperature",
        ));
    }
    Ok(Replacement {
        index: index as usize,
        temperature,
        mass,
        disease_count,
        element: u16at(bytes, 20),
        disease: bytes[23],
    })
}

pub(crate) fn parse(id: i32, bytes: &[u8], preview: &mut Preview, state: &State) -> Result<Action> {
    match id {
        ALLOCATE => {
            if bytes.len() != 10 {
                return Err(invalid("Allocate message must be 10 bytes"));
            }
            let (width, height) = (i32at(bytes, 0), i32at(bytes, 4));
            let (radiation, headless) = flags(bytes, 8)?;
            let generation = preview.lifecycle(width, height, false)?;
            let elements = state.elements.as_ref().ok_or_else(|| {
                Error::new(Status::InvalidState, "CreateElements must precede Allocate")
            })?;
            let seed = std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .map_err(|_| {
                    Error::new(Status::InternalFailure, "system clock is before Unix epoch")
                })?
                .as_secs() as u32;
            let mut world = PhysicsWorld::new(width as usize, height as usize, seed, elements)?;
            world.falling_disabled = headless != 0;
            world.radiation_enabled = radiation != 0;
            let mut grid = Grid::new(width, height, radiation, headless)?;
            grid.seed = seed;
            world.publish_to(&mut grid);
            Ok(Action::Allocate(grid, world, generation))
        }
        INITIALIZE => {
            let (width, height, radiation, headless) = validate_image(bytes)?;
            let generation = preview.lifecycle(width, height, true)?;
            let mut grid = Grid::new(width, height, radiation, headless)?;
            grid.load_image(bytes);
            let elements = state.elements.as_ref().ok_or_else(|| {
                Error::new(
                    Status::InvalidState,
                    "CreateElements must precede Initialize",
                )
            })?;
            let world = PhysicsWorld::from_grid(&grid, elements)?;
            Ok(Action::Initialize(grid, world, generation))
        }
        START => {
            if !bytes.is_empty() {
                return Err(invalid("Start message must be empty"));
            }
            if !preview.initialized || preview.started {
                return Err(Error::new(
                    Status::InvalidState,
                    "Start requires an initialized, unstarted grid",
                ));
            }
            preview.started = true;
            let legacy = if state
                .world
                .as_ref()
                .is_some_and(|world| world.legacy_thermal_boundaries)
            {
                Some(PhysicsWorld::thermal_boundary_element(
                    state.elements.as_ref().unwrap(),
                )?)
            } else {
                None
            };
            Ok(Action::Start(legacy))
        }
        MODIFY => {
            let action = replacement(bytes, preview)?;
            preview.pending_commands += 1;
            if preview.pending_commands > MAX_PENDING_COMMANDS {
                return Err(Error::new(
                    Status::ResourceLimit,
                    "pending cell command limit exceeded",
                ));
            }
            Ok(Action::Modify(action))
        }
        FRAME => {
            if !preview.started {
                return Err(Error::new(
                    Status::InvalidState,
                    "NewGameFrame requires Start",
                ));
            }
            let frame = NewFrame::parse(bytes, preview.width, preview.height)?;
            state.scheduler.subdivision(frame.elapsed)?;
            if frame.elapsed > 0.0 {
                crate::simulation_step::preflight(
                    state.world.as_ref().unwrap(),
                    state.elements.as_ref().unwrap(),
                    state.diseases.as_ref(),
                    frame.elapsed,
                )?;
            }
            preview.completed_frames =
                preview.completed_frames.checked_add(1).ok_or_else(|| {
                    Error::new(Status::ResourceLimit, "unpublished frame count overflow")
                })?;
            preview.pending_commands = 0;
            Ok(Action::Frame(frame))
        }
        PREPARE => {
            if !preview.started || bytes.len() != preview.count {
                return Err(invalid(
                    "Prepare requires a started grid and one visibility byte per public cell",
                ));
            }
            let mut visibility = Vec::new();
            visibility.try_reserve_exact(bytes.len()).map_err(|_| {
                Error::new(Status::ResourceLimit, "could not allocate visibility input")
            })?;
            visibility.extend_from_slice(bytes);
            preview.completed_frames = 0;
            Ok(Action::Prepare(visibility))
        }
        ELEMENTS => {
            if preview.allocated || preview.started || preview.pending_commands != 0 {
                return Err(Error::new(
                    Status::InvalidState,
                    "element tables must be registered before grid allocation",
                ));
            }
            let table = ElementTable::parse(bytes).map_err(invalid)?;
            if preview
                .disease_element_count
                .is_some_and(|count| count != table.len())
            {
                return Err(Error::new(
                    Status::InvalidState,
                    "new element table count conflicts with retained disease table",
                ));
            }
            preview.element_count = Some(table.len());
            Ok(Action::Elements(table))
        }
        DISEASES => {
            if preview.started || preview.pending_commands != 0 {
                return Err(Error::new(
                    Status::InvalidState,
                    "disease tables cannot change after Start or with queued commands",
                ));
            }
            let count = preview.element_count.ok_or_else(|| {
                Error::new(
                    Status::InvalidState,
                    "CreateElements must precede CreateDiseases",
                )
            })?;
            let table = DiseaseTable::parse(bytes, count).map_err(invalid)?;
            preview.disease_element_count = Some(table.element_count);
            Ok(Action::Diseases(table))
        }
        _ => Err(Error::new(
            Status::Unsupported,
            format!("message {id} is not implemented"),
        )),
    }
}
