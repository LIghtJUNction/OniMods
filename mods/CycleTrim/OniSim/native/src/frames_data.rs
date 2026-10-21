use crate::events::EventQueues;
use crate::frames_commands::EarlyCommands;
use crate::grid::Grid;
use crate::presentation::PresentationBuffers;
use crate::status::Result;
use crate::world_cells::filled;

pub(crate) struct GameData {
    pub(crate) grid: Grid,
    pub(crate) events: EventQueues,
    pub(crate) visible: Vec<u8>,
    pub(crate) presentation: PresentationBuffers,
    pub(crate) retired_slots: Vec<EarlyCommands>,
}

impl GameData {
    pub(crate) fn new(previous: &Grid) -> Result<Self> {
        let mut grid = Grid::new(
            previous.width,
            previous.height,
            previous.radiation_enabled,
            previous.headless,
        )?;
        grid.copy_from(previous);
        Ok(Self {
            grid,
            events: EventQueues::default(),
            visible: filled(previous.len(), 0)?,
            presentation: PresentationBuffers::new(previous.len())?,
            retired_slots: Vec::new(),
        })
    }

    pub(crate) fn fill_borrowed_ranges(&self, ranges: &mut Vec<(usize, usize)>) {
        // State reserves all 34 entries at grid allocation, before any worker
        // starts. Prepare performs no allocation after publishing pointers.
        assert!(ranges.capacity() >= 34);
        ranges.clear();
        ranges.extend_from_slice(&self.events.storage_ranges());
        let lengths = [8, 4, 4, 4, 1];
        for (pointer, size) in self.presentation.pointers().into_iter().zip(lengths) {
            ranges.push((pointer, self.grid.len() * size));
        }
    }
}
