//! CPU texture images consumed by managed PropertyTextures.LoadRawTextureData.
//! Static official ABI: GameData constructor 0x3a730, publication 0x32530,
//! and CopySimDataToGame 0x58600. Every GameData owns its own fixed allocations.

#[path = "presentation_flow.rs"]
mod flow;
#[path = "presentation_liquid.rs"]
mod liquid;
#[path = "presentation_sun.rs"]
mod sun;

use crate::bytes::overlaps;
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;
use crate::world_cells::filled;

pub(crate) struct PresentationBuffers {
    flow: Box<[[f32; 2]]>,
    liquid: Box<[u32]>,
    liquid_data: Box<[u32]>,
    material: Box<[u32]>,
    exposed: Box<[u8]>,
}

impl PresentationBuffers {
    pub(crate) fn new(count: usize) -> Result<Self> {
        Ok(Self {
            flow: filled(count, [0.0; 2])?.into_boxed_slice(),
            liquid: filled(count, 0)?.into_boxed_slice(),
            liquid_data: filled(count, 0)?.into_boxed_slice(),
            material: filled(count, 0)?.into_boxed_slice(),
            exposed: filled(count, 0)?.into_boxed_slice(),
        })
    }

    /// Called on the worker's unpublished GameData, after its final physics tick.
    /// This never reallocates the arrays whose addresses managed code retains.
    pub(crate) fn update(&mut self, world: &PhysicsWorld, elements: &ElementTable) -> Result<()> {
        self.validate(world, elements)?;
        flow::update(
            &mut self.flow,
            world.width,
            world.height,
            &world.read.element,
            &world.write.element,
            &world.write.mass,
            &world.flow,
        );
        sun::update(&mut self.exposed, world, elements);
        liquid::update(
            &mut self.liquid,
            &mut self.liquid_data,
            &mut self.material,
            &self.exposed,
            world,
            elements,
        );
        Ok(())
    }

    /// Descriptor fields +456, +464, +472, +480, +488, in that order.
    /// These are CPU addresses; they are not Unity native texture handles.
    pub(crate) fn pointers(&self) -> [usize; 5] {
        [
            self.flow.as_ptr() as usize,
            self.liquid.as_ptr() as usize,
            self.liquid_data.as_ptr() as usize,
            self.material.as_ptr() as usize,
            self.exposed.as_ptr() as usize,
        ]
    }

    pub(crate) fn overlaps_storage(&self, start: usize, length: usize) -> bool {
        macro_rules! image_overlaps {
            ($image:ident) => {
                overlaps(
                    start,
                    length,
                    self.$image.as_ptr() as usize,
                    std::mem::size_of_val(&*self.$image),
                )
            };
        }
        image_overlaps!(flow)
            || image_overlaps!(liquid)
            || image_overlaps!(liquid_data)
            || image_overlaps!(material)
            || image_overlaps!(exposed)
    }

    fn validate(&self, world: &PhysicsWorld, elements: &ElementTable) -> Result<()> {
        if world.width.checked_mul(world.height) != Some(self.flow.len())
            || world
                .write
                .element
                .iter()
                .any(|&element| element as usize >= elements.len())
            || world.world_offsets.iter().any(|&[x, y, width, height]| {
                let top = y as i64 + height as i64 - 1;
                width > 0
                    && (x < 0
                        || y < 0
                        || x as i64 + width as i64 > world.width as i64
                        || top < 0
                        || top >= world.height as i64)
            })
        {
            return Err(invalid(
                "texture images no longer match the initialized world",
            ));
        }
        for element in &elements.elements {
            let record = &element.record;
            if record.gradient_count as usize > record.gradient.len()
                || [record.low_transition, record.high_transition]
                    .into_iter()
                    .any(|index| index != u16::MAX && index as usize >= elements.len())
            {
                return Err(invalid(
                    "texture element gradient or transition index is invalid",
                ));
            }
        }
        if world
            .write
            .properties
            .iter()
            .any(|&flags| flags & 0x30 == 0x30)
            && !elements
                .elements
                .iter()
                .any(|element| element.record.id as u32 == sun::OVERRIDE_HASH)
        {
            return Err(invalid("sunlight material override element is missing"));
        }
        Ok(())
    }
}

fn invalid(message: &str) -> Error {
    Error::new(Status::InvalidState, message)
}
