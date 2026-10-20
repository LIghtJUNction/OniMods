use crate::grid::Grid;
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;

pub(crate) use crate::event_records::*;

macro_rules! queues {
    ($($field:ident: $kind:ty),* $(,)?) => {
        #[derive(Default)]
        pub(crate) struct EventQueues { $(pub(crate) $field: Vec<$kind>),* }

        impl EventQueues {
            pub(crate) fn clear(&mut self) { $(self.$field.clear();)* }

            // Cache these ranges while the unpublished/leased GameData is
            // owned here. Once returned to the worker, alias checks must use
            // the cache rather than borrow that worker-owned object again.
            pub(crate) fn storage_ranges(&self) -> [(usize, usize); 29] {
                [$(
                    if self.$field.capacity() == 0 {
                        (0, 0)
                    } else {
                        (self.$field.as_ptr() as usize,
                         self.$field.capacity() * std::mem::size_of::<$kind>())
                    },
                )*]
            }

            pub(crate) fn overlaps_storage(&self, start: usize, length: usize) -> bool {
                $(
                    (self.$field.capacity() != 0 && crate::bytes::overlaps(
                        start, length, self.$field.as_ptr() as usize,
                        self.$field.capacity() * std::mem::size_of::<$kind>(),
                    )) ||
                )* false
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

    pub(crate) fn substance_changed(&mut self, game_cell: i32, old: u16, new: u16) {
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
