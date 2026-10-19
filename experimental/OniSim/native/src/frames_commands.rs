//! Fixed-layout cell commands recovered from the official ProcessFrame path.
use crate::events::EventQueues;
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

#[path = "frames_commands_energy.rs"]
mod energy;
#[path = "frames_commands_parse.rs"]
mod parse;
#[path = "frames_commands_process.rs"]
mod process;

pub(crate) const MODIFY_ENERGY: i32 = 818_320_644;
pub(crate) const SET_INSULATION: i32 = -898_773_121;
pub(crate) const SET_STRENGTH: i32 = 1_593_243_982;
pub(crate) const CHANGE_PROPERTIES: i32 = -469_311_643;
pub(crate) const CONSUME_DISEASE: i32 = -1_019_841_536;
pub(crate) const MODIFY_DISEASE: i32 = -1_853_671_274;
pub(crate) const SET_DEBUG: i32 = -1_683_118_492;
pub(crate) const MODIFY_BACKWALL: i32 = -605_421_515;
pub(crate) const SAVED_OPTIONS: i32 = 1_154_135_737;

#[derive(Clone, Copy)]
struct CellFloat {
    cell: usize,
    value: f32,
}

#[derive(Clone, Copy)]
struct Energy {
    cell: usize,
    kilojoules: f32,
    max_temperature: f32,
    diagnostic_id: i32,
}

#[derive(Clone, Copy)]
struct Properties {
    cell: usize,
    callback: i32,
    mask: u8,
    set: bool,
}

#[derive(Clone, Copy)]
struct ConsumeDisease {
    cell: usize,
    callback: i32,
    percent: f32,
    maximum: i32,
}

#[derive(Clone, Copy)]
struct ModifyDisease {
    cell: usize,
    disease: u8,
    count: i32,
}

#[derive(Clone, Copy)]
struct Backwall {
    public: i32,
    cell: usize,
    element: u16,
    mass: f32,
    temperature: f32,
}

#[derive(Clone, Copy)]
struct DebugProperties {
    raw: [u8; 12],
}

impl Default for DebugProperties {
    fn default() -> Self {
        let mut raw = [0; 12];
        raw[..4].copy_from_slice(&0x3a83126f_u32.to_le_bytes());
        raw[4..8].copy_from_slice(&0x3a83126f_u32.to_le_bytes());
        Self { raw }
    }
}

// Per-kind vectors preserve the native order even when incoming kinds interleave.
#[derive(Clone, Default)]
pub(crate) struct EarlyCommands {
    insulation: Vec<CellFloat>,
    strength: Vec<CellFloat>,
    energy: Vec<Energy>,
    properties: Vec<Properties>,
    consume_disease: Vec<ConsumeDisease>,
    modify_disease: Vec<ModifyDisease>,
    backwall: Vec<Backwall>,
    // SimFrameInfo's debug record belongs to the slot, not to global state.
    // Recycle this whole drained command set to preserve the slot's record.
    debug: DebugProperties,
    debug_pending: bool,
}

impl EarlyCommands {
    pub(crate) fn len(&self) -> usize {
        self.insulation.len()
            + self.strength.len()
            + self.energy.len()
            + self.properties.len()
            + self.consume_disease.len()
            + self.modify_disease.len()
            + self.backwall.len()
            + usize::from(self.debug_pending)
    }
}

pub(crate) fn is_message(id: i32) -> bool {
    matches!(
        id,
        MODIFY_ENERGY
            | SET_INSULATION
            | SET_STRENGTH
            | CHANGE_PROPERTIES
            | CONSUME_DISEASE
            | MODIFY_DISEASE
            | SET_DEBUG
            | MODIFY_BACKWALL
    )
}

fn allocation(_: std::collections::TryReserveError) -> Error {
    Error::new(
        Status::ResourceLimit,
        "could not allocate cell command storage",
    )
}

fn push<T>(queue: &mut Vec<T>, command: T) -> Result<()> {
    queue.try_reserve(1).map_err(allocation)?;
    queue.push(command);
    Ok(())
}

// ELF 0x316b0: this command mutates saved options synchronously, outside a frame.
pub(crate) fn apply_saved_options(world: &mut PhysicsWorld, payload: &[u8]) -> Result<()> {
    if payload.len() != 2 {
        return Err(crate::bytes::invalid("SetSavedOptions requires two bytes"));
    }
    world.saved_options = (world.saved_options & !payload[0]) | payload[1];
    Ok(())
}
