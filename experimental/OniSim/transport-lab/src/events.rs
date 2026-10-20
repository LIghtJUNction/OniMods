//! Only the two event methods reached by these production kernels. Record types
//! come from the unchanged native file. The event order is compared in tests.
pub(crate) use crate::event_records::*;
#[derive(Default, Clone)]
pub(crate) struct EventQueues {
    pub substance_change_info: Vec<SubstanceChangeInfo>,
    pub backwall_should_transition_infos: Vec<BackwallShouldTransitionInfo>,
}
impl EventQueues {
    pub(crate) fn substance_change(&mut self, game_cell: i32) {
        self.substance_changed(game_cell, u16::MAX, u16::MAX);
    }
    pub(crate) fn substance_changed(&mut self, game_cell: i32, old: u16, new: u16) {
        self.substance_change_info.push(SubstanceChangeInfo {
            cell_idx: game_cell,
            old_elem_idx: old,
            new_elem_idx: new,
        });
    }
    pub(crate) fn backwall_should_transition(&mut self, game_cell: i32) {
        self.backwall_should_transition_infos
            .push(BackwallShouldTransitionInfo { game_cell });
    }
    pub fn clear(&mut self) {
        self.substance_change_info.clear();
        self.backwall_should_transition_infos.clear();
    }
}
