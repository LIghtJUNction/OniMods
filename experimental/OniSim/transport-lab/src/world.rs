//! Storage-only fixture. Numerical operations use the production source modules.
pub(crate) use crate::world_cells::Cells;
#[derive(Clone, Copy)]
pub(crate) struct Region {
    pub min_x: i32,
    pub min_y: i32,
    pub max_x: i32,
    pub max_y: i32,
}
#[derive(Clone)]
pub(crate) struct PhysicsWorld {
    pub width: usize,
    pub height: usize,
    pub stride: usize,
    pub padded_height: usize,
    pub read: Cells,
    pub write: Cells,
    pub backwall_element: Vec<u16>,
    pub backwall_mass: Vec<f32>,
    pub backwall_temperature: Vec<f32>,
    pub flow: Vec<[f32; 4]>,
    pub dirty: Vec<bool>,
    pub dirty_flags: Vec<u8>,
    pub vacuum_element: u16,
    pub sink_element: u16,
    pub scan_direction: i32,
    pub tick_seed: u16,
    pub rng: u32,
}
impl PhysicsWorld {
    pub fn new(width: usize, height: usize) -> Self {
        let stride = width + 2;
        let n = stride * (height + 2);
        Self {
            width,
            height,
            stride,
            padded_height: height + 2,
            read: Cells::new(n).unwrap_or_else(|e| panic!("{}", e.message)),
            write: Cells::new(n).unwrap_or_else(|e| panic!("{}", e.message)),
            backwall_element: vec![0; n],
            backwall_mass: vec![0.0; n],
            backwall_temperature: vec![0.0; n],
            flow: vec![[0.0; 4]; n],
            dirty: vec![false; n],
            dirty_flags: vec![0; n],
            vacuum_element: 0,
            sink_element: 4,
            scan_direction: 1,
            tick_seed: 0,
            rng: 12345,
        }
    }
    pub fn mark_dirty(&mut self, index: usize, flags: u8) {
        self.dirty[index] = true;
        self.dirty_flags[index] |= flags;
    }
    pub fn region(&self) -> Region {
        Region {
            min_x: 1,
            min_y: 1,
            max_x: self.width as i32 + 1,
            max_y: self.height as i32 + 1,
        }
    }
}
