use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::radiation::{self, RadiationEmitterData};
use crate::status::Result;
use crate::tables::ElementTable;
use crate::world::{PhysicsWorld, Region};

// Official Line ELF 0x668d0 normalizes the major axis before visiting both ends.
pub(crate) fn transmission(
    world: &PhysicsWorld, elements: &ElementTable,
    mut x0: i32, mut y0: i32, mut x1: i32, mut y1: i32,
) -> Result<f32> {
    let steep = (y1 - y0).abs() > (x1 - x0).abs();
    if steep {
        std::mem::swap(&mut x0, &mut y0);
        std::mem::swap(&mut x1, &mut y1);
    }
    if x0 > x1 {
        std::mem::swap(&mut x0, &mut x1);
        std::mem::swap(&mut y0, &mut y1);
    }
    let dx = (x1 - x0) as f32;
    let dy = (y1 - y0).abs() as f32;
    let mut error = dx * 0.5;
    let step = if y1 > y0 { 1 } else { -1 };
    let mut y = y0;
    let mut remaining = 1.0;
    for x in x0..=x1 {
        let (cell_x, cell_y) = if steep { (y, x) } else { (x, y) };
        let cell = cell_y as usize * world.stride + cell_x as usize;
        remaining *= 1.0 - radiation::attenuation(world, elements, cell)?;
        error -= dy;
        if error < 0.0 {
            y += step;
            error += dx;
        }
    }
    Ok(remaining)
}

fn inside(world: &PhysicsWorld, region: Region, x: i32, y: i32) -> bool {
    x > 0 && x < world.stride as i32 && y > 0 && y < world.padded_height as i32
        && x >= region.min_x && x <= region.max_x
        && y >= region.min_y && y <= region.max_y
}

pub(crate) fn angle_allows(data: &RadiationEmitterData, dx: i32, dy: i32) -> bool {
    if (dx == 0 && dy == 0) || data.angle == 360.0 || data.angle.is_nan() { return true; }
    let center = 360.0 + data.direction;
    let half = data.angle * 0.5;
    let lower = (center - half) % 360.0;
    let upper = (center + half) % 360.0;
    let angle = ((dy as f32).atan2(dx as f32) * 180.0 / std::f32::consts::PI + 360.0)
        % 360.0;
    if upper > lower { angle >= lower && angle <= upper }
    else { angle >= lower || angle <= upper }
}

#[derive(Clone, Copy)]
enum Deposit { Direct, Attenuated, Absorb }

struct Ring<'a> {
    world: &'a mut PhysicsWorld,
    elements: &'a ElementTable,
    region: Region,
    data: &'a RadiationEmitterData,
    source_x: i32,
    source_y: i32,
    mode: Deposit,
    absorbed: f32,
}

impl Ring<'_> {
    fn deposit(&mut self, x: i32, y: i32, amount: f32) -> Result<()> {
        if !inside(self.world, self.region, x, y) { return Ok(()); }
        let source = x == self.source_x && y == self.source_y;
        match self.mode {
            Deposit::Direct => {
                if !source && !angle_allows(self.data, x - self.source_x, y - self.source_y) {
                    return Ok(());
                }
            }
            Deposit::Attenuated => {
                if amount <= 0.0 || source
                    || !angle_allows(self.data, x - self.source_x, y - self.source_y) {
                    return Ok(());
                }
            }
            Deposit::Absorb => {
                if source || !angle_allows(self.data, x - self.source_x, y - self.source_y) {
                    return Ok(());
                }
            }
        }
        let cell = y as usize * self.world.stride + x as usize;
        match self.mode {
            Deposit::Direct => self.world.write.radiation[cell] += amount,
            Deposit::Attenuated => {
                let remaining = transmission(self.world, self.elements,
                    self.source_x, self.source_y, x, y)?;
                self.world.write.radiation[cell] += amount * smax(smin(1.0, remaining), 0.0);
            }
            Deposit::Absorb => {
                let existing = self.world.write.radiation[cell];
                if existing < f32::from_bits(0x3c23d70a) { return Ok(()); }
                let removed = smin(amount, existing);
                self.world.write.radiation[cell] = existing - removed;
                // Official 0x655e0 assigns the shared output for every accepted point.
                self.absorbed = removed;
            }
        }
        Ok(())
    }

    fn reflected(&mut self, x: i32, y: i32, amount: f32) -> Result<()> {
        self.deposit(self.source_x.wrapping_add(x), self.source_y.wrapping_add(y), amount)?;
        self.deposit(self.source_x.wrapping_sub(x), self.source_y.wrapping_add(y), amount)?;
        self.deposit(self.source_x.wrapping_add(x), self.source_y.wrapping_sub(y), amount)?;
        self.deposit(self.source_x.wrapping_sub(x), self.source_y.wrapping_sub(y), amount)
    }
}

// The compiler's roundf sequence uses the float immediately below 0.5.
fn rounded(value: f32) -> f32 {
    if value.abs() < 8_388_608.0 {
        let integer = cvtt_f32_i32(value.abs() + f32::from_bits(0x3effffff)) as f32;
        integer.copysign(value)
    } else { value }
}

fn reciprocal_sqrt(value: f32) -> f32 {
    #[cfg(target_arch = "x86_64")]
    let initial = unsafe {
        use std::arch::x86_64::{_mm_cvtss_f32, _mm_rsqrt_ss, _mm_set_ss};
        _mm_cvtss_f32(_mm_rsqrt_ss(_mm_set_ss(value)))
    };
    #[cfg(not(target_arch = "x86_64"))]
    let initial = 1.0 / value.sqrt();
    ((value * initial) * initial - 3.0) * (initial * -0.5)
}

// Official antialiased oval ELF 0x657c0. Duplicated axis points are intentional.
fn oval(state: &mut Ring<'_>, radius_x: i32, radius_y: i32, amount: f32) -> Result<()> {
    if radius_x <= 0 || radius_y <= 0 { return Ok(()); }
    let a2 = radius_x.wrapping_mul(radius_x);
    let b2 = radius_y.wrapping_mul(radius_y);
    let inverse = reciprocal_sqrt(a2.wrapping_add(b2) as f32);
    let max_x = rounded(inverse * a2 as f32);
    let mut x = 0.0;
    let inverse_a2 = 1.0 / a2 as f32;
    while x <= max_x {
        let y = radius_y as f32 * (1.0 - (x * x) * inverse_a2).sqrt();
        let floor = y.floor();
        let outer = rounded((y - floor) * amount);
        state.reflected(cvtt_f32_i32(x), cvtt_f32_i32(floor), outer)?;
        state.reflected(cvtt_f32_i32(x), cvtt_f32_i32(floor).wrapping_sub(1), amount - outer)?;
        x += 1.0;
    }
    let max_y = rounded(inverse * b2 as f32);
    let mut y = 0.0;
    let inverse_b2 = 1.0 / b2 as f32;
    while y <= max_y {
        let x = radius_x as f32 * (1.0 - (y * y) * inverse_b2).sqrt();
        let floor = x.floor();
        let outer = rounded((x - floor) * amount);
        state.reflected(cvtt_f32_i32(floor), cvtt_f32_i32(y), outer)?;
        state.reflected(cvtt_f32_i32(floor).wrapping_sub(1), cvtt_f32_i32(y), amount - outer)?;
        y += 1.0;
    }
    Ok(())
}

pub(crate) fn ring(
    world: &mut PhysicsWorld, elements: &ElementTable, region: Region,
    data: &RadiationEmitterData, radius_x: i32, radius_y: i32, absorb: bool,
) -> Result<f32> {
    let source_x = (data.cell % world.stride) as i32;
    let source_y = (data.cell / world.stride) as i32;
    let mut state = Ring { world, elements, region, data, source_x, source_y,
        mode: if absorb { Deposit::Absorb } else { Deposit::Direct }, absorbed: 0.0 };
    oval(&mut state, radius_x, radius_y, data.rads)?;
    Ok(state.absorbed)
}

pub(crate) fn emit(
    world: &mut PhysicsWorld, elements: &ElementTable, region: Region,
    data: &RadiationEmitterData, radius: u32,
) -> Result<()> {
    let scale = (data.iteration as i32 as f32 + 1.0) / radius as i32 as f32;
    let radius_x = (data.radius_x as f32 * scale).ceil() as i32;
    let radius_y = (data.radius_y as f32 * scale).ceil() as i32;
    let amount = if data.kind == 2 {
        data.rads / radius_x.wrapping_sub(1).wrapping_mul(radius_y.wrapping_sub(1)).max(1) as f32
    } else { data.rads };
    if data.iteration == 0 {
        world.write.radiation[data.cell] += amount;
        return Ok(());
    }
    if radius_x <= 1 && radius_y <= 1 { return Ok(()); }
    let source_x = (data.cell % world.stride) as i32;
    let source_y = (data.cell / world.stride) as i32;
    let mut state = Ring { world, elements, region, data, source_x, source_y,
        mode: Deposit::Attenuated, absorbed: 0.0 };
    oval(&mut state, radius_x, radius_y, amount)
}
