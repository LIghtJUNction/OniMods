//! Radiation component state and command queues, independently defined from the
//! official managed wire records and static Linux ELF control flow.
use crate::bytes::{f32at, i32at, u16at};
use crate::component_handles::ComponentHandles;
use crate::events::{ComponentStateChangedMessage, ConsumedRadiationCallback, EventQueues};
use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};
use crate::worker_shape::WorldShape;

pub(crate) use crate::radiation_region::{attenuation, update_region};

#[derive(Clone, Copy)]
pub(crate) struct RadiationParams {
    pub(crate) dose_scale: f32,
    pub(crate) mass_scale: f32,
    pub(crate) absorption_base: f32,
    pub(crate) absorption_mass_scale: f32,
    pub(crate) absorption_solid: f32,
}

impl Default for RadiationParams {
    fn default() -> Self {
        // SimData constructor 0x3e470; rodata 0xa5580..0xa558f.
        Self { dose_scale: f32::from_bits(0x3f8c_cccd), mass_scale: 2000.0,
            absorption_base: f32::from_bits(0x3e99_999a),
            absorption_mass_scale: f32::from_bits(0x3f33_3333),
            absorption_solid: f32::from_bits(0x3f4c_cccd) }
    }
}

#[derive(Clone)]
pub(crate) struct RadiationEmitterData {
    pub(crate) cell: usize,
    pub(crate) radius_x: u16,
    pub(crate) radius_y: u16,
    pub(crate) rads: f32,
    pub(crate) rate: f32,
    pub(crate) speed: f32,
    pub(crate) direction: f32,
    pub(crate) angle: f32,
    pub(crate) elapsed: f32,
    pub(crate) accumulated: f32,
    pub(crate) kind: i32,
    pub(crate) iteration: u32,
}

#[derive(Default)]
pub(crate) struct RadiationEmitters {
    pub(crate) emitters: ComponentHandles<RadiationEmitterData>,
    pending: RadiationCommands,
}

// A FrameSync seals these vectors by ownership transfer. Components remain in
// the simulator; commands arriving after the barrier enter a fresh collection.
#[derive(Default)]
pub(crate) struct RadiationCommands {
    add: Vec<(i32, RadiationEmitterData)>,
    modify: Vec<(i32, RadiationEmitterData)>,
    remove: Vec<(i32, i32)>,
    cell: Vec<(i32, f32, i32)>,
    params: Vec<(i32, f32)>,
}

pub(crate) fn is_message(id: i32) -> bool {
    matches!(id, -1505895314 | -503965465 | -704259919 | -1914877797 | 377112707)
}

fn allocation(_: std::collections::TryReserveError) -> Error {
    Error::new(Status::ResourceLimit, "could not allocate radiation queue")
}

fn internal_cell(shape: &WorldShape, cell: i32) -> Result<usize> {
    if cell < 0 || cell as usize >= shape.width * shape.height {
        return Err(Error::new(Status::InvalidArgument, "radiation cell is outside the grid"));
    }
    Ok(shape.internal_index(cell as usize))
}

fn decode(shape: &WorldShape, payload: &[u8], at: usize) -> Result<RadiationEmitterData> {
    let rate = f32at(payload, at + 8);
    let speed = f32at(payload, at + 12);
    let effective_speed = smin(speed, rate);
    Ok(RadiationEmitterData { cell: internal_cell(shape, i32at(payload, 4))?,
        radius_x: u16at(payload, at), radius_y: u16at(payload, at + 2),
        rads: f32at(payload, at + 4), rate, speed: effective_speed,
        direction: f32at(payload, at + 16), angle: f32at(payload, at + 20),
        elapsed: 0.0, accumulated: 0.0, kind: i32at(payload, at + 24), iteration: 0 })
}

impl RadiationEmitters {
    pub(crate) fn take_pending(&mut self) -> RadiationCommands {
        std::mem::take(&mut self.pending)
    }

    pub(crate) fn queue(&mut self, id: i32, payload: &[u8], shape: &WorldShape) -> Result<bool> {
        let length = match id {
            -1505895314 => 36, -503965465 => 40, -704259919 | 377112707 => 8,
            -1914877797 => 12, _ => return Ok(false),
        };
        if payload.len() != length {
            return Err(Error::new(Status::InvalidArgument, "invalid radiation message length"));
        }
        match id {
            -1505895314 => {
                let data = decode(shape, payload, 8)?;
                self.pending.add.try_reserve(1).map_err(allocation)?;
                self.pending.add.push((i32at(payload, 0), data));
            }
            -503965465 => {
                let data = decode(shape, payload, 12)?;
                self.pending.modify.try_reserve(1).map_err(allocation)?;
                self.pending.modify.push((i32at(payload, 0), data));
            }
            -704259919 => {
                self.pending.remove.try_reserve(1).map_err(allocation)?;
                self.pending.remove.push((i32at(payload, 0), i32at(payload, 4)));
            }
            -1914877797 => {
                internal_cell(shape, i32at(payload, 0))?;
                self.pending.cell.try_reserve(1).map_err(allocation)?;
                self.pending.cell.push((i32at(payload, 0), f32at(payload, 4), i32at(payload, 8)));
            }
            377112707 => {
                self.pending.params.try_reserve(1).map_err(allocation)?;
                self.pending.params.push((i32at(payload, 0), f32at(payload, 4)));
            }
            _ => unreachable!(),
        }
        Ok(true)
    }

    // ProcessFrame 0x4ad00: cell radiation and parameter queues precede the
    // component queues, and retain their own native per-kind ordering.
    pub(crate) fn process_cell_pending(&mut self, commands: &mut RadiationCommands,
        world: &mut PhysicsWorld, events: &mut EventQueues) -> Result<()> {
        let callbacks = commands.cell.iter().filter(|item| item.2 != -1).count();
        events.radiation_consumed_callbacks.try_reserve(callbacks).map_err(allocation)?;
        for (public, delta, callback) in commands.cell.drain(..) {
            let cell = world.internal_index(public as usize);
            let old = world.write.radiation[cell];
            let mut value = old + delta;
            let amount = if value <= 0.0 { value = 0.0; old } else { delta };
            world.write.radiation[cell] = value;
            if callback != -1 {
                events.radiation_consumed_callbacks.push(ConsumedRadiationCallback {
                    callback_idx: callback, game_cell: public, radiation: amount,
                });
            }
        }
        for (kind, value) in commands.params.drain(..) {
            match kind {
                0 => world.radiation_params.dose_scale = value,
                2 => world.radiation_params.absorption_base = value,
                3 => world.radiation_params.absorption_mass_scale = value,
                4 => world.radiation_params.absorption_solid = value,
                5 => world.radiation_params.mass_scale = value,
                // These selectors intentionally have no native assignment.
                _ => {},
            }
        }
        Ok(())
    }

    // Allocation or invalid removal can fail after earlier commands have been
    // applied. The frame owner must retain its fault boundary on that error.
    pub(crate) fn process_emitter_pending(&mut self, commands: &mut RadiationCommands,
        _world: &mut PhysicsWorld, events: &mut EventQueues) -> Result<()> {
        let callbacks = commands.add.iter().filter(|item| item.0 != -1).count()
            + commands.remove.iter().filter(|item| item.1 != -1).count();
        events.component_state_changed_messages.try_reserve(callbacks).map_err(allocation)?;
        for (callback, data) in commands.add.drain(..) {
            let handle = self.emitters.insert(data)?;
            if callback != -1 {
                events.component_state_changed_messages.push(ComponentStateChangedMessage {
                    callback_idx: callback, sim_handle: handle,
                });
            }
        }
        for (handle, mut replacement) in commands.modify.drain(..) {
            // Native Modify checks generation and skips stale/unknown handles.
            if let Some(index) = self.emitters.index(handle) {
                let existing = &mut self.emitters.data[index];
                replacement.elapsed = existing.elapsed;
                replacement.accumulated = existing.accumulated;
                replacement.iteration = existing.iteration;
                *existing = replacement;
            }
        }
        for (handle, callback) in commands.remove.drain(..) {
            self.emitters.remove(handle)?;
            if callback != -1 {
                events.component_state_changed_messages.push(ComponentStateChangedMessage {
                    callback_idx: callback, sim_handle: -1,
                });
            }
        }
        Ok(())
    }

    pub(crate) fn update(&mut self, dt: f32, world: &mut PhysicsWorld,
        elements: &ElementTable, region: Region) -> Result<()> {
        if !world.radiation_enabled { return Ok(()); }
        for data in &mut self.emitters.data {
            let x = (data.cell % world.stride) as i32;
            let y = (data.cell / world.stride) as i32;
            if !in_region(region, x, y) || data.rads <= 0.0
                || (data.radius_x == 0 && data.radius_y == 0) { continue; }
            data.elapsed += dt;
            let radius = u32::from(data.radius_x.max(data.radius_y));
            let increment = data.speed / radius as f32;
            let steps;
            // COMISS/JNE also takes the zero-period branch for an unordered rate.
            if data.rate == 0.0 || data.rate.is_nan() {
                data.elapsed = 0.0;
                data.iteration = 0;
                steps = 1;
            } else {
                if data.rate <= data.elapsed {
                    data.elapsed = 0.0;
                    data.iteration = 0;
                } else if data.speed < data.elapsed { continue; }
                steps = if dt / increment <= 1.0 && !(increment > data.accumulated) { 1 }
                    else { cvtt_f32_i32(dt / increment) };
            }
            for _ in 0..steps.max(0) {
                match data.kind {
                    0 => ellipse(world, elements, region, data)?,
                    1 | 2 => crate::radiation_rays::emit(world, elements, region, data, radius)?,
                    3 | 5 => {
                        let fraction = (data.iteration as f32 + 1.0) * (1.0 / radius as f32);
                        let rx = native_ceil(data.radius_x as f32 * fraction).max(1);
                        let ry = native_ceil(data.radius_y as f32 * fraction).max(1);
                        let absorbed = crate::radiation_rays::ring(world, elements, region,
                            data, rx, ry, data.kind == 5)?;
                        if data.kind == 5 && 0.0 <= absorbed {
                            world.write.radiation[data.cell] += absorbed;
                        }
                    }
                    // 4 and unrecognized kinds have no native switch body.
                    _ => {},
                }
                data.iteration = if data.iteration.wrapping_add(1) == radius { 0 }
                    else { data.iteration.wrapping_add(1) };
                data.accumulated -= increment;
            }
            data.accumulated += dt;
        }
        Ok(())
    }
}

pub(crate) fn in_region(region: Region, x: i32, y: i32) -> bool {
    region.min_x <= x && region.min_y <= y && x <= region.max_x && y <= region.max_y
}

pub(crate) fn native_ceil(value: f32) -> i32 {
    let integer = cvtt_f32_i32(value);
    if (integer as f32) < value { integer.wrapping_add(1) } else { integer }
}

fn ellipse(world: &mut PhysicsWorld, elements: &ElementTable, region: Region,
    data: &RadiationEmitterData) -> Result<()> {
    let ox = (data.cell % world.stride) as i32;
    let oy = (data.cell / world.stride) as i32;
    let rx = i32::from(data.radius_x);
    let ry = i32::from(data.radius_y);
    // The native vertical termination uses radius_x, even for non-square axes.
    for y in oy - ry..=oy - ry + rx * 2 {
        let dy = (y - oy) as f32;
        for x in ox - rx..=ox + rx {
            if x <= 0 || y <= 0 || x >= world.stride as i32
                || y >= world.padded_height as i32 || !in_region(region, x, y) { continue; }
            let dx = (x - ox) as f32;
            if (x != ox || y != oy)
                && !crate::radiation_rays::angle_allows(data, x - ox, y - oy) { continue; }
            if dx * dx * (1.0 / rx.wrapping_mul(rx) as f32)
                + dy * dy * (1.0 / ry.wrapping_mul(ry) as f32) <= 1.0 {
                let weight = (1.0 - dx.abs() * (1.0 / rx as f32))
                    * (1.0 - (1.0 / ry as f32) * dy.abs());
                let transmission = crate::radiation_rays::transmission(world, elements,
                    ox, oy, x, y)?;
                let clamped = smax(smin(1.0, transmission), 0.0);
                let amount = clamped * ((weight * data.rads) / world.radiation_params.dose_scale);
                let noise = if weight < 0.25 {
                    ((world.next_random() as f32 * f32::from_bits(0x3700_0100)) - 0.125) * amount
                } else { 0.0 };
                let cell = y as usize * world.stride + x as usize;
                world.write.radiation[cell] = amount + world.write.radiation[cell] + noise;
            }
        }
    }
    Ok(())
}

// Keep the complete pre-component update available through this module's public
// surface, with table ownership remaining in the frame controller.
pub(crate) fn update(world: &mut PhysicsWorld, elements: &ElementTable,
    diseases: &DiseaseTable, region: Region) -> Result<()> {
    update_region(world, elements, diseases, region)
}
