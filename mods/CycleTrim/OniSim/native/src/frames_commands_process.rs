use super::*;
use crate::events::{BackwallElementChangedInfo, CallbackInfo};
use crate::numeric::cvtt_f32_i32;

impl EarlyCommands {
    // ProcessFrame ELF 0x4ad00..0x4b084. The mass cell queues follow this stage.
    pub(crate) fn process_before_cells(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: Option<&DiseaseTable>,
        events: &mut EventQueues,
    ) -> Result<()> {
        let disease_table = if self.properties.is_empty() {
            None
        } else {
            Some(diseases.ok_or_else(|| {
                Error::new(
                    Status::InvalidState,
                    "properties require registered diseases",
                )
            })?)
        };
        // Displacement may inspect all cardinal/diagonal neighbours. Unknown
        // table indices are rejected before changing any property or temperature.
        if !self.properties.is_empty()
            && world
                .write
                .element
                .iter()
                .any(|&index| index as usize >= elements.len())
        {
            return Err(Error::new(
                Status::InvalidState,
                "displacement has an unknown element",
            ));
        }
        if disease_table.is_some_and(|table| {
            world
                .write
                .disease_idx
                .iter()
                .any(|&index| index != u8::MAX && index as usize >= table.diseases.len())
        }) {
            return Err(Error::new(
                Status::InvalidState,
                "displacement has an unknown disease",
            ));
        }
        if self
            .energy
            .iter()
            .any(|command| world.write.element[command.cell] as usize >= elements.len())
        {
            return Err(Error::new(
                Status::InvalidState,
                "energy target has an unknown element",
            ));
        }
        if !self.energy.is_empty()
            && elements.elements.iter().any(|element| {
                [
                    element.record.low_transition,
                    element.record.high_transition,
                ]
                .iter()
                .any(|&index| index != u16::MAX && index as usize >= elements.len())
            })
        {
            return Err(Error::new(
                Status::InvalidState,
                "energy transition references an unknown element",
            ));
        }
        let energy_count = self.energy.len();
        events
            .callback_info
            .try_reserve(self.properties.len())
            .map_err(allocation)?;
        events
            .substance_change_info
            // At most four gas moves (two notifications each), followed by
            // the five-notification liquid distribution in the existing helper.
            .try_reserve(self.properties.len() * 13 + energy_count)
            .map_err(allocation)?;
        events
            .spawn_ore_info
            .try_reserve(energy_count * 2)
            .map_err(allocation)?;
        events
            .cell_melted_infos
            .try_reserve(energy_count)
            .map_err(allocation)?;
        events
            .spawn_falling_liquid_info
            .try_reserve(energy_count)
            .map_err(allocation)?;

        for command in self.insulation.drain(..) {
            world.write.insulation[command.cell] = cvtt_f32_i32(command.value * 255.0) as u8;
        }
        for command in self.strength.drain(..) {
            world.write.strength[command.cell] = cvtt_f32_i32(command.value) as u8;
        }
        for command in self.energy.drain(..) {
            energy::modify(world, elements, events, command);
        }
        for command in self.properties.drain(..) {
            let cell = command.cell;
            if command.set {
                world.write.properties[cell] |= command.mask;
            } else {
                world.write.properties[cell] &= !command.mask;
            }
            let properties = world.write.properties[cell];
            // The native COMISS/JAE skips ordered nonpositive mass; unordered
            // mass reaches the helper, whose own phase/quantity gates still apply.
            if properties != 0 && !(world.write.mass[cell] <= 0.0) {
                let element = world.write.element[cell];
                let diseases = disease_table.unwrap();
                if properties & 1 != 0 {
                    crate::gas::displace_gas(world, elements, diseases, events, cell, element);
                } else if properties & 2 != 0 {
                    crate::fluid_displacement::displace_liquid(
                        world, elements, diseases, events, cell, element,
                    );
                }
            }
            if command.callback != -1 {
                events.callback_info.push(CallbackInfo {
                    callback_idx: command.callback,
                });
            }
        }
        Ok(())
    }

    // ProcessFrame ELF 0x4b644..0x4b8eb, after mass consumption/emission and
    // before the cell radiation and radiation-parameter queues.
    pub(crate) fn process_disease(
        &mut self,
        world: &mut PhysicsWorld,
        _elements: &ElementTable,
        diseases: Option<&DiseaseTable>,
        events: &mut EventQueues,
    ) -> Result<()> {
        let table = if self
            .modify_disease
            .iter()
            .all(|command| command.disease == u8::MAX)
        {
            None
        } else {
            let table = diseases.ok_or_else(|| {
                Error::new(
                    Status::InvalidState,
                    "disease changes require registered diseases",
                )
            })?;
            for command in &self.modify_disease {
                if command.disease != u8::MAX && command.disease as usize >= table.diseases.len() {
                    return Err(Error::new(
                        Status::InvalidState,
                        "unknown disease in cell command",
                    ));
                }
            }
            Some(table)
        };
        events
            .disease_consumption_callbacks
            .try_reserve(self.consume_disease.len())
            .map_err(allocation)?;
        for command in self.consume_disease.drain(..) {
            crate::disease::consume(
                world,
                events,
                command.cell,
                command.percent,
                command.maximum,
                command.callback,
            )?;
        }
        for command in self.modify_disease.drain(..) {
            if command.disease == u8::MAX {
                // Native's raw-count branch never reads the disease table.
                let count = world.write.disease_count[command.cell].wrapping_add(command.count);
                world.write.disease_count[command.cell] = count;
                if count < 1 {
                    crate::fluid_helpers::clear_disease(&mut world.write, command.cell);
                }
            } else {
                let table = table.unwrap();
                let old = world.write.disease_idx[command.cell];
                if old != u8::MAX && old as usize >= table.diseases.len() {
                    return Err(Error::new(
                        Status::InvalidState,
                        "unknown current cell disease",
                    ));
                }
                crate::disease::modify_cell(
                    world,
                    table,
                    command.cell,
                    command.disease,
                    command.count,
                )?;
            }
        }
        Ok(())
    }

    // ProcessFrame ELF 0x4c603..0x4c6d6: chunk modifications precede this
    // queue, and RemoveElementChunk follows it. No direct dirty flag is set.
    pub(crate) fn process_backwall(
        &mut self,
        world: &mut PhysicsWorld,
        _elements: &ElementTable,
        events: &mut EventQueues,
    ) -> Result<()> {
        events
            .backwall_element_changed_infos
            .try_reserve(self.backwall.len())
            .map_err(allocation)?;
        for command in self.backwall.drain(..) {
            let old = world.backwall_element[command.cell];
            world.backwall_element[command.cell] = command.element;
            world.backwall_mass[command.cell] = command.mass;
            world.backwall_temperature[command.cell] = command.temperature;
            if old != command.element {
                events
                    .backwall_element_changed_infos
                    .push(BackwallElementChangedInfo {
                        game_cell: command.public,
                    });
            }
        }
        Ok(())
    }

    // HandleMessage overwrites the frame slot; ProcessFrame 0x4cf99 applies
    // its value every frame, after all component command queues. The owner must
    // rotate whole EarlyCommands slots before execution, then recycle this slot.
    pub(crate) fn process_tail(&mut self, world: &mut PhysicsWorld) -> Result<()> {
        world.building_heat_transfer_scale = crate::bytes::f32at(&self.debug.raw, 0);
        world.building_contact_heat_transfer_scale = crate::bytes::f32at(&self.debug.raw, 4);
        world.simulate_all_active = self.debug.raw[8] != 0;
        self.debug_pending = false;
        Ok(())
    }
}
