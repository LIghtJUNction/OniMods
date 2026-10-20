//! Immutable geometry for validators on the submission side of the worker.

use crate::world::PhysicsWorld;

#[derive(Clone, Copy)]
pub(crate) struct WorldShape {
    pub(crate) width: usize,
    pub(crate) height: usize,
    pub(crate) stride: usize,
    pub(crate) padded_height: usize,
}

impl WorldShape {
    /// Capture only during allocation/initialization, before worker ownership.
    pub(crate) fn from_world(world: &PhysicsWorld) -> Self {
        Self {
            width: world.width,
            height: world.height,
            stride: world.stride,
            padded_height: world.padded_height,
        }
    }

    pub(crate) fn internal_index(&self, public: usize) -> usize {
        (public / self.width + 1) * self.stride + public % self.width + 1
    }

    pub(crate) fn public_index(&self, internal: usize) -> Option<usize> {
        let (x, y) = (internal % self.stride, internal / self.stride);
        if x == 0 || x > self.width || y == 0 || y > self.height {
            return None;
        }
        Some((y - 1) * self.width + x - 1)
    }
}
