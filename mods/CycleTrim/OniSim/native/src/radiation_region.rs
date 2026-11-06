//! Environmental radiation phase from the official 0x69730 frame update.
use crate::numeric::{smax, smin};
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

// Read as float triples from rodata 0xa5ed0..0xa5fe7 plus the constructor's
// final (2,2,0.1). Iteration order affects additions and is retained.
const KERNEL: [(i32, i32, f32); 25] = [
    (-2, -2, 0.1),
    (-1, -2, 0.15),
    (0, -2, 0.25),
    (1, -2, 0.15),
    (2, -2, 0.1),
    (-2, -1, 0.15),
    (-1, -1, 0.5),
    (0, -1, 0.75),
    (1, -1, 0.5),
    (2, -1, 0.15),
    (-2, 0, 0.25),
    (-1, 0, 0.75),
    (0, 0, 1.0),
    (1, 0, 0.75),
    (2, 0, 0.25),
    (-2, 1, 0.15),
    (-1, 1, 0.5),
    (0, 1, 0.75),
    (1, 1, 0.5),
    (2, 1, 0.15),
    (-2, 2, 0.1),
    (-1, 2, 0.15),
    (0, 2, 0.25),
    (1, 2, 0.15),
    (2, 2, 0.1),
];

pub(crate) fn attenuation(
    world: &PhysicsWorld,
    elements: &ElementTable,
    cell: usize,
) -> Result<f32> {
    let element = elements
        .elements
        .get(world.write.element[cell] as usize)
        .ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                "radiation encountered an unknown element index",
            )
        })?;
    let params = world.radiation_params;
    let amount = if world.write.properties[cell] & 0x80 != 0 {
        params.absorption_solid
    } else {
        (world.write.mass[cell] / params.mass_scale) * params.absorption_mass_scale
            + params.absorption_base
    };
    let absorbed = element.record.radiation_absorption * amount;
    Ok(smax(smin(absorbed, 1.0), 0.0))
}

pub(crate) fn update_region(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    region: Region,
) -> Result<()> {
    // SimData+0x71a gates the complete native block, including germ radiation.
    if !world.radiation_enabled {
        return Ok(());
    }
    if region.min_x < 0
        || region.min_y < 0
        || region.max_x < region.min_x
        || region.max_y < region.min_y
        || region.max_x >= world.stride as i32
        || region.max_y >= world.padded_height as i32
    {
        return Err(Error::new(
            Status::InvalidArgument,
            "radiation region is outside padded storage",
        ));
    }
    let dose = world.radiation_params.dose_scale;
    // Above the region the sky transmission starts at 1.0. Native uses a
    // strict y+1<max_y condition, including its second independent top row.
    for y in (region.min_y..=region.max_y).rev() {
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            let incoming = if y + 1 < region.max_y {
                world.radiation_transmission[cell + world.stride]
            } else {
                1.0
            };
            let transmitted = (1.0 - attenuation(world, elements, cell)?) * incoming;
            world.radiation_transmission[cell] = if 0.01 <= transmitted {
                if transmitted <= 0.0 { 0.0 } else { transmitted }
            } else {
                0.0
            };
            let old = world.write.radiation[cell];
            let mut loss = old / dose;
            if loss == 0.0 {
                loss = if 1.0 < old { old - 1.0 } else { 0.0 };
                world.write.radiation[cell] = loss;
            } else {
                world.write.radiation[cell] = old - loss;
            }
        }
    }
    let nuclear = diseases
        .diseases
        .iter()
        .rposition(|disease| disease.hash as u32 == 0xd49f_77d6)
        .map_or(u8::MAX, |index| index as u8);
    for y in region.min_y..region.max_y {
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            let element = elements
                .elements
                .get(world.write.element[cell] as usize)
                .ok_or_else(|| {
                    Error::new(
                        Status::InvalidState,
                        "radiation encountered an unknown source element index",
                    )
                })?;
            let emission = element.record.radiation_per_1000_mass;
            if emission > 0.0 {
                let source = world.write.mass[cell] * 0.001;
                for (dx, dy, weight) in KERNEL {
                    let (tx, ty) = (x + dx, y + dy);
                    if region.min_x <= tx
                        && region.min_y <= ty
                        && tx <= region.max_x
                        && ty <= region.max_y
                    {
                        let target = ty as usize * world.stride + tx as usize;
                        world.write.radiation[target] =
                            emission * source * weight + world.write.radiation[target];
                    }
                }
            }
            let mut value = world.write.radiation[cell];
            if world.write.disease_idx[cell] == nuclear {
                value += world.write.disease_count[cell] as f32 * 0.001;
                world.write.radiation[cell] = value;
            }
            let transmission = world.radiation_transmission[cell];
            if transmission > 0.0 {
                value += transmission * (region.cosmic_radiation / dose);
                world.write.radiation[cell] = value;
            }
            // MaxRadiation is the unaligned float at rodata 0xa6008.
            // The <=0.01 dead band and unordered handling follow native branches.
            world.write.radiation[cell] = if !(value >= 0.0) || value <= 0.01 {
                0.0
            } else if value > 9_000_000.0 {
                9_000_000.0
            } else {
                value
            };
        }
    }
    Ok(())
}
