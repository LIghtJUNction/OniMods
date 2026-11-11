//! Current session view, shared for game publications or detached for stable APIs.
use crate::grid::Grid;
use std::ops::{Deref, DerefMut};
use std::sync::Arc;

pub(crate) struct SessionGrid(Arc<Grid>);

impl From<Grid> for SessionGrid {
    fn from(grid: Grid) -> Self {
        Self(Arc::new(grid))
    }
}

impl Deref for SessionGrid {
    type Target = Grid;
    fn deref(&self) -> &Grid {
        &self.0
    }
}

impl DerefMut for SessionGrid {
    fn deref_mut(&mut self) -> &mut Grid {
        Arc::make_mut(&mut self.0)
    }
}

impl SessionGrid {
    pub(crate) fn detach(&mut self) {
        Arc::make_mut(&mut self.0);
    }

    pub(crate) fn publish(&mut self, grid: &Arc<Grid>, shared: bool) {
        if shared {
            self.0 = Arc::clone(grid);
        } else {
            self.copy_from(grid);
        }
    }
}
