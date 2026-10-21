//! Minimal host adapter. Cell storage is the unchanged real project type.
//!
//! Full PhysicsWorld construction and unrelated simulation subsystems are not
//! linked. The compared functions use only these four world fields.

pub(crate) use crate::world_cells::Cells;

#[derive(Clone, Copy, Default)]
pub(crate) struct Region {
    pub(crate) min_x: i32,
    pub(crate) min_y: i32,
    pub(crate) max_x: i32,
    pub(crate) max_y: i32,
    pub(crate) sunlight: f32,
    pub(crate) cosmic_radiation: f32,
}

#[derive(Clone)]
pub(crate) struct PhysicsWorld {
    pub(crate) stride: usize,
    pub(crate) padded_height: usize,
    pub(crate) read: Cells,
    pub(crate) write: Cells,
}

impl PhysicsWorld {
    pub(crate) fn fixture(stride: usize, padded_height: usize) -> Self {
        let length = stride
            .checked_mul(padded_height)
            .expect("fixture storage overflow");
        Self {
            stride,
            padded_height,
            read: crate::shared::success(Cells::new(length)),
            write: crate::shared::success(Cells::new(length)),
        }
    }

    pub(crate) fn full_region(&self) -> Region {
        Region {
            min_x: 0,
            min_y: 0,
            max_x: self.stride as i32 - 1,
            max_y: self.padded_height as i32 - 1,
            ..Region::default()
        }
    }
}
