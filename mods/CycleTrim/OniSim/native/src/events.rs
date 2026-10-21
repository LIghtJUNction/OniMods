use crate::grid::Grid;
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;

pub(crate) use crate::event_records::*;

macro_rules! queues {
    ($($field:ident: $kind:ty),* $(,)?) => {
        #[derive(Default)]
        pub(crate) struct EventQueues {
            $(pub(crate) $field: Vec<$kind>,)*
            change_seen: Vec<u64>,
            change_limit: usize,
        }

        impl EventQueues {
            pub(crate) fn clear(&mut self) {
                $(self.$field.clear();)*
                self.change_seen.clear();
                self.change_limit = 0;
            }

            // Cache these ranges while the unpublished/leased GameData is
            // owned here. Once returned to the worker, alias checks must use
            // the cache rather than borrow that worker-owned object again.
            pub(crate) fn storage_ranges(&self) -> [(usize, usize); 30] {
                [$(
                    if self.$field.capacity() == 0 {
                        (0, 0)
                    } else {
                        (self.$field.as_ptr() as usize,
                         self.$field.capacity() * std::mem::size_of::<$kind>())
                    },
                )*
                    if self.change_seen.capacity() == 0 { (0, 0) } else {
                        (self.change_seen.as_ptr() as usize, self.change_seen.capacity() * 8)
                    },
                ]
            }

            pub(crate) fn overlaps_storage(&self, start: usize, length: usize) -> bool {
                $(
                    (self.$field.capacity() != 0 && crate::bytes::overlaps(
                        start, length, self.$field.as_ptr() as usize,
                        self.$field.capacity() * std::mem::size_of::<$kind>(),
                    )) ||
                )* (self.change_seen.capacity() != 0 && crate::bytes::overlaps(
                    start, length, self.change_seen.as_ptr() as usize,
                    self.change_seen.capacity() * 8,
                ))
            }

            pub(crate) fn publish_to(&self, grid: &mut Grid) -> Result<()> {
                // Calculate all lengths before changing any descriptor field.
                let spans = [$(span(&self.$field)?),*];
                for (slot, (count, pointer)) in spans.into_iter().enumerate() {
                    grid.set_event_span(slot, count, pointer);
                }
                Ok(())
            }
        }
    };
}

// Order is the official x64 GameDataUpdate's 29 count/pointer pairs.
queues! {
    solid_info: SolidInfo,
    liquid_change_info: LiquidChangeInfo,
    solid_substance_change_info: SolidSubstanceChangeInfo,
    substance_change_info: SubstanceChangeInfo,
    callback_info: CallbackInfo,
    spawn_falling_liquid_info: SpawnFallingLiquidInfo,
    dig_info: SpawnOreInfo,
    spawn_ore_info: SpawnOreInfo,
    spawn_fx_info: SpawnFXInfo,
    unstable_cell_info: UnstableCellInfo,
    world_damage_info: WorldDamageInfo,
    building_temperatures: BuildingTemperatureInfo,
    mass_consumed_callbacks: MassConsumedCallback,
    mass_emitted_callbacks: MassEmittedCallback,
    disease_consumption_callbacks: DiseaseConsumptionCallback,
    component_state_changed_messages: ComponentStateChangedMessage,
    removed_mass_entries: ConsumedMassInfo,
    emitted_mass_entries: EmittedMassInfo,
    element_chunk_infos: ElementChunkInfo,
    element_chunk_melted_infos: MeltedInfo,
    building_overheat_infos: MeltedInfo,
    building_no_longer_overheated_infos: MeltedInfo,
    building_melted_infos: MeltedInfo,
    cell_melted_infos: CellMeltedInfo,
    backwall_element_changed_infos: BackwallElementChangedInfo,
    backwall_should_transition_infos: BackwallShouldTransitionInfo,
    disease_emitted_infos: DiseaseEmittedInfo,
    disease_consumed_infos: DiseaseConsumedInfo,
    radiation_consumed_callbacks: ConsumedRadiationCallback,
}

pub(crate) type Events = EventQueues;

fn span<T>(records: &[T]) -> Result<(i32, usize)> {
    let count = i32::try_from(records.len())
        .map_err(|_| Error::new(Status::ResourceLimit, "event count exceeds official ABI"))?;
    Ok((
        count,
        if count == 0 {
            0
        } else {
            records.as_ptr() as usize
        },
    ))
}

impl EventQueues {
    pub(crate) fn resolve_grid_changes(
        &mut self,
        previous: &Grid,
        world: &PhysicsWorld,
        elements: &ElementTable,
    ) -> Result<()> {
        if previous.len() != world.width * world.height {
            return Err(Error::new(
                Status::InternalFailure,
                "published grid dimensions differ from simulation",
            ));
        }
        // Work on the unpublished event set only. The previous descriptor and
        // its records remain borrowed until Prepare swaps the two event sets.
        for event in &self.substance_change_info {
            let public = usize::try_from(event.cell_idx)
                .ok()
                .filter(|&index| index < previous.len())
                .ok_or_else(|| {
                    Error::new(
                        Status::InternalFailure,
                        "substance change has invalid public cell",
                    )
                })?;
            let old = previous.element[public] as usize;
            let new = world.write.element[world.internal_index(public)] as usize;
            if old >= elements.len() || new >= elements.len() {
                return Err(Error::new(
                    Status::InvalidState,
                    "substance change references an unknown element",
                ));
            }
        }
        self.substance_change_info
            .sort_unstable_by_key(|event| event.cell_idx);
        self.substance_change_info
            .dedup_by_key(|event| event.cell_idx);
        let mut solid_changes = 0;
        let mut solids = 0;
        let mut liquids = 0;
        for event in &self.substance_change_info {
            let public = event.cell_idx as usize;
            let old = previous.element[public];
            let new = world.write.element[world.internal_index(public)];
            if old == new {
                continue;
            }
            let old_phase = elements.elements[old as usize].record.state & 3;
            let new_phase = elements.elements[new as usize].record.state & 3;
            solid_changes += usize::from((old_phase == 3) != (new_phase == 3));
            solids += usize::from(old_phase == 3 || new_phase == 3);
            liquids += usize::from(old_phase == 2 || new_phase == 2);
        }
        let allocation = || {
            Error::new(
                Status::ResourceLimit,
                "could not allocate derived grid events",
            )
        };
        self.solid_info
            .try_reserve(solid_changes)
            .map_err(|_| allocation())?;
        self.solid_substance_change_info
            .try_reserve(solids)
            .map_err(|_| allocation())?;
        self.liquid_change_info
            .try_reserve(liquids)
            .map_err(|_| allocation())?;
        for event in &mut self.substance_change_info {
            let public = event.cell_idx as usize;
            let old = previous.element[public];
            let new = world.write.element[world.internal_index(public)];
            event.old_elem_idx = old;
            event.new_elem_idx = new;
            if old == new {
                continue;
            }
            let old_phase = elements.elements[old as usize].record.state & 3;
            let new_phase = elements.elements[new as usize].record.state & 3;
            if (old_phase == 3) != (new_phase == 3) {
                self.solid_info.push(SolidInfo {
                    cell_idx: event.cell_idx,
                    is_solid: if new_phase == 3 { -1 } else { 0 },
                });
            }
            if old_phase == 3 || new_phase == 3 {
                self.solid_substance_change_info
                    .push(SolidSubstanceChangeInfo {
                        cell_idx: event.cell_idx,
                    });
            }
            if old_phase == 2 || new_phase == 2 {
                self.liquid_change_info.push(LiquidChangeInfo {
                    cell_idx: event.cell_idx,
                });
            }
        }
        Ok(())
    }

    pub(crate) fn substance_change(&mut self, game_cell: i32) {
        self.substance_changed(game_cell, u16::MAX, u16::MAX);
    }

    pub(crate) fn track_grid_changes(&mut self, cells: usize) {
        // At most 2 MiB per unpublished buffer. Allocation pressure retains
        // the original append/sort/deduplicate path without partial mutation.
        self.change_limit = 0;
        self.change_seen.clear();
        let words = cells.div_ceil(64);
        if words > 262_144 || self.change_seen.try_reserve_exact(words).is_err() {
            return;
        }
        self.change_seen.resize(words, 0);
        self.change_limit = cells;
    }

    pub(crate) fn substance_changed(&mut self, game_cell: i32, old: u16, new: u16) {
        if let Ok(index) = usize::try_from(game_cell)
            && index < self.change_limit
        {
            let word = &mut self.change_seen[index / 64];
            let mask = 1_u64 << (index % 64);
            if *word & mask != 0 {
                return;
            }
            *word |= mask;
        }
        self.substance_change_info.push(SubstanceChangeInfo {
            cell_idx: game_cell,
            old_elem_idx: old,
            new_elem_idx: new,
        });
    }

    pub(crate) fn spawn_falling_liquid(
        &mut self,
        cell_idx: i32,
        elem_idx: u16,
        disease_idx: u8,
        mass: f32,
        temperature: f32,
        disease_count: i32,
    ) {
        self.spawn_falling_liquid_info.push(SpawnFallingLiquidInfo {
            cell_idx,
            elem_idx,
            disease_idx,
            pad0: 0,
            mass,
            temperature,
            disease_count,
        });
    }

    pub(crate) fn spawn_ore(
        &mut self,
        cell_idx: i32,
        elem_idx: u16,
        disease_idx: u8,
        mass: f32,
        temperature: f32,
        disease_count: i32,
    ) {
        self.spawn_ore_info.push(SpawnOreInfo {
            cell_idx,
            elem_idx,
            disease_idx,
            pad0: 0,
            mass,
            temperature,
            disease_count,
        });
    }

    pub(crate) fn cell_melted(&mut self, game_cell: i32) {
        self.cell_melted_infos.push(CellMeltedInfo { game_cell });
    }

    pub(crate) fn backwall_should_transition(&mut self, game_cell: i32) {
        self.backwall_should_transition_infos
            .push(BackwallShouldTransitionInfo { game_cell });
    }
}

#[cfg(test)]
mod change_tracking_tests {
    use super::*;

    fn ids(events: &EventQueues) -> Vec<i32> {
        let mut values = events
            .substance_change_info
            .iter()
            .map(|event| event.cell_idx)
            .collect::<Vec<_>>();
        values.sort_unstable();
        values.dedup();
        values
    }

    #[test]
    fn early_dedup_preserves_cell_membership_with_resets_merges_and_invalid_ids() {
        for cells in [0, 1, 63, 64, 65, 129] {
            let mut tracked = EventQueues::default();
            let mut ordinary = EventQueues::default();
            tracked.track_grid_changes(cells);
            let mut random = 0x12345678u32;
            for iteration in 0..4096 {
                random ^= random << 13;
                random ^= random >> 17;
                random ^= random << 5;
                let cell = (random as usize % (cells + 3)) as i32 - 1;
                tracked.substance_changed(cell, iteration as u16, !(iteration as u16));
                ordinary.substance_changed(cell, iteration as u16, !(iteration as u16));
                if iteration % 97 == 0 {
                    // Untracked temporary queues append records directly.
                    let value = SubstanceChangeInfo {
                        cell_idx: cell,
                        old_elem_idx: 255,
                        new_elem_idx: 255,
                    };
                    tracked.substance_change_info.push(value);
                    ordinary.substance_change_info.push(value);
                }
                assert_eq!(ids(&tracked), ids(&ordinary));
                if iteration % 511 == 0 {
                    tracked.clear();
                    ordinary.clear();
                    tracked.track_grid_changes(cells);
                }
            }
        }
    }

    #[test]
    fn tracker_is_bounded_and_private_storage_participates_in_alias_checks() {
        let mut events = EventQueues::default();
        events.track_grid_changes(129);
        assert_eq!(events.change_seen.len(), 3);
        let start = events.change_seen.as_ptr() as usize;
        assert!(events.overlaps_storage(start, 1));
        assert_eq!(
            events.storage_ranges()[29],
            (start, events.change_seen.capacity() * 8)
        );
        events.clear();
        events.track_grid_changes(usize::MAX);
        assert_eq!(events.change_limit, 0);
        events.substance_change(0);
        events.substance_change(0);
        assert_eq!(events.substance_change_info.len(), 2);
    }
}

#[cfg(test)]
mod tracker_capacity_regression {
    use super::EventQueues;

    #[test]
    fn growing_dimensions_never_exceed_tracker_word_budget() {
        const WORDS: usize = 262_144;
        let mut events = EventQueues::default();
        // A capacity just above half the budget makes amortized growth
        // double beyond the advertised bound on the next larger world.
        for words in [WORDS / 2 + 1, WORDS, 1, 0, WORDS + 1] {
            events.track_grid_changes(words * 64);
            assert!(events.change_seen.capacity() <= WORDS);
            if words <= WORDS {
                assert_eq!(events.change_seen.len(), words);
                assert_eq!(events.change_limit, words * 64);
            } else {
                assert_eq!(events.change_limit, 0);
                assert!(events.change_seen.is_empty());
            }
        }
    }
}
