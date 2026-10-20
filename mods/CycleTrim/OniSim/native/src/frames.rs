use crate::bytes::{f32at, i32at, invalid};
use crate::status::{Error, Result, Status};
use crate::world::Region;

pub(crate) const MAX_PENDING_COMMANDS: usize = 65_536;

pub(crate) struct NewFrame {
    pub(crate) elapsed: f32,
    pub(crate) regions: Vec<Region>,
}

pub(crate) struct Replacement {
    pub(crate) index: usize,
    pub(crate) temperature: f32,
    pub(crate) mass: f32,
    pub(crate) disease_count: i32,
    pub(crate) element: u16,
    pub(crate) disease: u8,
}

#[derive(Default)]
pub(crate) struct Scheduler {
    pub(crate) free_slots: Vec<crate::frames_commands::EarlyCommands>,
    pub(crate) completed_frames: i32,
}

pub(crate) struct QueuedFrame {
    pub(crate) elapsed: f32,
    pub(crate) early: crate::frames_commands::EarlyCommands,
    pub(crate) material: crate::cell_material::PendingCommands,
    pub(crate) world: crate::world_messages::PendingCommands,
    pub(crate) thermal: crate::thermal_components::PendingCommands,
    pub(crate) mass: crate::mass_components::PendingCommands,
    pub(crate) radiation: crate::radiation::RadiationCommands,
}

impl NewFrame {
    pub(crate) fn parse(bytes: &[u8], width: i32, height: i32) -> Result<Self> {
        if bytes.is_empty() || bytes.len() % 28 != 0 {
            return Err(invalid(
                "NewGameFrame requires one or more 28-byte active-region records",
            ));
        }
        let elapsed = f32at(bytes, 0);
        if !elapsed.is_finite() || elapsed < 0.0 {
            return Err(invalid("frame elapsed must be finite and nonnegative"));
        }
        let mut regions = Vec::new();
        regions
            .try_reserve_exact(bytes.len() / 28)
            .map_err(|_| Error::new(Status::ResourceLimit, "could not allocate frame regions"))?;
        for record in bytes.chunks_exact(28) {
            let (min_x, min_y) = (i32at(record, 4), i32at(record, 8));
            let (max_x, max_y) = (i32at(record, 12), i32at(record, 16));
            let (sunlight, cosmic_radiation) = (f32at(record, 20), f32at(record, 24));
            if min_x < 0
                || min_y < 0
                || max_x < min_x
                || max_y < min_y
                || max_x > width
                || max_y > height
                || !sunlight.is_finite()
                || !cosmic_radiation.is_finite()
            {
                return Err(invalid(
                    "active region is outside the public grid or has non-finite fields",
                ));
            }
            regions.push(Region {
                min_x: min_x + 1,
                min_y: min_y + 1,
                max_x: max_x + 1,
                max_y: max_y + 1,
                sunlight,
                cosmic_radiation,
            });
        }
        Ok(Self { elapsed, regions })
    }
}

pub(crate) fn subdivide(elapsed: f32, residual: f32) -> Result<(usize, f32)> {
    if elapsed <= 0.0 {
        return Ok((0, residual));
    }
    let total = elapsed + residual;
    let requested = total * crate::simulation_step::TICKS_PER_SECOND;
    if !total.is_finite() || !requested.is_finite() || requested >= 4097.0 {
        return Err(Error::new(
            Status::ResourceLimit,
            "frame exceeds 4096 physics substeps",
        ));
    }
    let ticks = crate::numeric::cvtt_f32_i32(requested).max(1);
    Ok((
        ticks as usize,
        crate::numeric::smax(total - ticks as f32 * crate::simulation_step::FIXED_DT, 0.0),
    ))
}
