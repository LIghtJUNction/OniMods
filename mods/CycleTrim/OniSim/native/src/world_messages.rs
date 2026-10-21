//! World topology handlers and the separate ProcessFrame world-zone phase.
//! Source contract: official ELF 0x315d0, 0x33060, 0x3d6c0, 0x3dc90,
//! 0x3deb0; queued zone modifications at ProcessFrame 0x4ad00.

#[path = "world_messages_cells.rs"]
mod cells_kernel;

use crate::bytes::{MAX_PAYLOAD, dimensions, i32at, invalid};
use crate::status::{Error, Result, Status};
use crate::world::PhysicsWorld;

pub(crate) const CLEAR: i32 = -1_836_204_275;
pub(crate) const DEFINE_OFFSETS: i32 = -895_846_551;
pub(crate) const SET_ZONES: i32 = -457_308_393;
pub(crate) const MODIFY_ZONE: i32 = -449_718_014;
pub(crate) const INITIALIZE_VACUUM: i32 = -752_676_153;
pub(crate) const FREE: i32 = -1_167_792_921;
const MAX_PENDING: usize = 65_536;

#[derive(Clone, Copy)]
pub(crate) struct Rectangle {
    pub(crate) x: i32,
    pub(crate) y: i32,
    pub(crate) width: i32,
    pub(crate) height: i32,
}

impl Rectangle {
    fn parse(bytes: &[u8], at: usize, world: &PhysicsWorld) -> Result<Self> {
        let rect = Self {
            width: i32at(bytes, at),
            height: i32at(bytes, at + 4),
            x: i32at(bytes, at + 8),
            y: i32at(bytes, at + 12),
        };
        rect.validate(world)?;
        Ok(rect)
    }

    fn validate(&self, world: &PhysicsWorld) -> Result<()> {
        if self.width < -1 || self.height < -1 || self.x < 0 || self.y < 0 {
            return Err(invalid(
                "world rectangle has invalid coordinates or dimensions",
            ));
        }
        let width = self.width as i64 + 2;
        let height = self.height as i64 + 2;
        if self.x as i64 + width > world.stride as i64
            || self.y as i64 + height > world.padded_height as i64
        {
            return Err(invalid(
                "world rectangle and its halo exceed allocated storage",
            ));
        }
        Ok(())
    }

    fn record(self) -> [i32; 4] {
        [self.x, self.y, self.width, self.height]
    }
}

pub(crate) enum ImmediateCommand {
    Clear,
    DefineOffsets(Vec<[i32; 4]>),
    SetZones(Vec<u8>),
    InitializeVacuum(Rectangle),
    Free(Rectangle),
}

pub(crate) fn is_immediate(id: i32) -> bool {
    matches!(
        id,
        CLEAR | DEFINE_OFFSETS | SET_ZONES | INITIALIZE_VACUUM | FREE
    )
}

pub(crate) fn parse_immediate(
    id: i32,
    bytes: &[u8],
    world: &PhysicsWorld,
) -> Result<ImmediateCommand> {
    if bytes.len() > MAX_PAYLOAD {
        return Err(allocation());
    }
    Ok(match id {
        CLEAR => {
            if !bytes.is_empty() {
                return Err(invalid("ClearUnoccupiedCells requires an empty payload"));
            }
            ImmediateCommand::Clear
        }
        DEFINE_OFFSETS => {
            if bytes.len() < 4 {
                return Err(invalid("DefineWorldOffsets requires a count"));
            }
            let count = usize::try_from(i32at(bytes, 0))
                .map_err(|_| invalid("negative world offset count"))?;
            let length = count
                .checked_mul(16)
                .and_then(|value| value.checked_add(4))
                .ok_or_else(allocation)?;
            let logical = trim_fixed_payload(bytes, length)?;
            let mut offsets = Vec::new();
            offsets.try_reserve_exact(count).map_err(|_| allocation())?;
            for record in logical[4..].as_chunks::<16>().0 {
                let rect = Rectangle {
                    x: i32at(record, 0),
                    y: i32at(record, 4),
                    width: i32at(record, 8),
                    height: i32at(record, 12),
                };
                rect.validate(world)?;
                offsets.push(rect.record());
            }
            ImmediateCommand::DefineOffsets(offsets)
        }
        SET_ZONES => {
            let count = world
                .width
                .checked_mul(world.height)
                .ok_or_else(allocation)?;
            if bytes.len() != count {
                return Err(invalid("SetWorldZones requires one byte per public cell"));
            }
            let mut zones = Vec::new();
            zones
                .try_reserve_exact(world.stride * world.padded_height)
                .map_err(|_| allocation())?;
            zones.resize(world.stride * world.padded_height, 0);
            for row in 0..world.height {
                let start = (row + 1) * world.stride + 1;
                zones[start..start + world.width]
                    .copy_from_slice(&bytes[row * world.width..(row + 1) * world.width]);
            }
            ImmediateCommand::SetZones(zones)
        }
        INITIALIZE_VACUUM => {
            let logical = trim_fixed_payload(bytes, 24)?;
            // The first two i32 values are consumed but unused by 0x3deb0.
            ImmediateCommand::InitializeVacuum(Rectangle::parse(logical, 8, world)?)
        }
        FREE => {
            let logical = trim_fixed_payload(bytes, 16)?;
            ImmediateCommand::Free(Rectangle::parse(logical, 0, world)?)
        }
        _ => return Err(invalid("unknown immediate world message")),
    })
}

pub(crate) fn apply_immediate(command: ImmediateCommand, world: &mut PhysicsWorld) -> Result<()> {
    match command {
        ImmediateCommand::Clear => cells_kernel::clear_all_primary(world),
        ImmediateCommand::DefineOffsets(offsets) => world.world_offsets = offsets,
        ImmediateCommand::SetZones(zones) => world.inactive_mask = Some(zones),
        ImmediateCommand::InitializeVacuum(rect) => {
            rect.validate(world)?;
            world
                .world_offsets
                .try_reserve(1)
                .map_err(|_| allocation())?;
            cells_kernel::initialize_rectangle(world, rect);
            world.world_offsets.push(rect.record());
        }
        ImmediateCommand::Free(rect) => {
            rect.validate(world)?;
            cells_kernel::free_rectangle(world, rect);
            if let Some(index) = world
                .world_offsets
                .iter()
                .position(|&record| record == rect.record())
            {
                world.world_offsets.remove(index);
            }
        }
    }
    Ok(())
}

#[derive(Clone, Copy)]
struct ZoneChange {
    public_cell: usize,
    zone: u8,
}

#[derive(Default)]
pub(crate) struct PendingCommands {
    zones: Vec<ZoneChange>,
}

impl PendingCommands {
    pub(crate) fn len(&self) -> usize {
        self.zones.len()
    }
    pub(crate) fn is_empty(&self) -> bool {
        self.zones.is_empty()
    }
}

#[derive(Default)]
pub(crate) struct WorldMessages {
    pending: PendingCommands,
}

impl WorldMessages {
    pub(crate) fn is_message(id: i32) -> bool {
        id == MODIFY_ZONE
    }

    pub(crate) fn queue(
        &mut self,
        id: i32,
        bytes: &[u8],
        world: &crate::worker_shape::WorldShape,
    ) -> Result<()> {
        if id != MODIFY_ZONE || bytes.len() != 8 {
            return Err(invalid("ModifyCellWorldZone requires an 8-byte record"));
        }
        let public_cell =
            usize::try_from(i32at(bytes, 0)).map_err(|_| invalid("negative world zone cell"))?;
        if public_cell >= world.width * world.height {
            return Err(invalid("world zone cell exceeds the public grid"));
        }
        if self.pending.len() >= MAX_PENDING {
            return Err(allocation());
        }
        self.pending
            .zones
            .try_reserve(1)
            .map_err(|_| allocation())?;
        self.pending.zones.push(ZoneChange {
            public_cell,
            zone: bytes[4],
        });
        Ok(())
    }

    pub(crate) fn take_pending(&mut self) -> PendingCommands {
        std::mem::take(&mut self.pending)
    }

    pub(crate) fn append(&mut self, mut staged: Self) -> Result<()> {
        if self.len().saturating_add(staged.len()) > MAX_PENDING {
            return Err(allocation());
        }
        self.pending
            .zones
            .try_reserve(staged.pending.zones.len())
            .map_err(|_| allocation())?;
        self.pending.zones.append(&mut staged.pending.zones);
        Ok(())
    }

    pub(crate) fn len(&self) -> usize {
        self.pending.len()
    }
    pub(crate) fn is_empty(&self) -> bool {
        self.pending.is_empty()
    }
}

/// Run after ProcessCellModifications and before any component registration.
/// The caller captures this owned queue at NewFrame submission, never later.
pub(crate) fn process_pending(
    commands: &mut PendingCommands,
    world: &mut PhysicsWorld,
) -> Result<()> {
    if commands.is_empty() {
        return Ok(());
    }
    let zones = world.inactive_mask.as_ref().ok_or_else(|| {
        Error::new(
            Status::InvalidState,
            "SetWorldZones must precede queued zone modifications",
        )
    })?;
    if zones.len() != world.stride * world.padded_height
        || commands
            .zones
            .iter()
            .any(|change| change.public_cell >= world.width * world.height)
    {
        return Err(invalid(
            "queued zone modifications no longer match the world",
        ));
    }
    for change in commands.zones.drain(..) {
        let internal = world.internal_index(change.public_cell);
        world.inactive_mask.as_mut().unwrap()[internal] = change.zone;
    }
    Ok(())
}

/// Managed InitializeFromCells passes MemoryStream capacity, including zero tail.
pub(crate) fn trim_initialize_payload(bytes: &[u8]) -> Result<&[u8]> {
    if bytes.len() < 14 {
        return Err(invalid("InitializeFromCells requires a 14-byte prefix"));
    }
    let count = dimensions(i32at(bytes, 0), i32at(bytes, 4))?;
    let length = count
        .checked_mul(40)
        .and_then(|value| value.checked_add(14))
        .ok_or_else(allocation)?;
    trim_fixed_payload(bytes, length)
}

pub(crate) fn trim_fixed_payload(bytes: &[u8], logical_length: usize) -> Result<&[u8]> {
    if logical_length > MAX_PAYLOAD || bytes.len() > MAX_PAYLOAD {
        return Err(allocation());
    }
    let logical = bytes
        .get(..logical_length)
        .ok_or_else(|| invalid("truncated world message"))?;
    if bytes[logical_length..].iter().any(|&byte| byte != 0) {
        return Err(invalid(
            "world message has nonzero bytes after its logical payload",
        ));
    }
    Ok(logical)
}

fn allocation() -> Error {
    Error::new(
        Status::ResourceLimit,
        "world message size, queue or allocation exceeds its limit",
    )
}
