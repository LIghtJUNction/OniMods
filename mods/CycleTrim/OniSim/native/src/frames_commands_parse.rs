use super::*;
use crate::bytes::{f32at, i32at, invalid, u16at};
use crate::worker_shape::WorldShape;

fn cell(world: &WorldShape, public: i32) -> Result<usize> {
    if public < 0 || public as usize >= world.width * world.height {
        return Err(invalid("cell command index is outside the public grid"));
    }
    Ok(world.internal_index(public as usize))
}

fn disease_table(diseases: Option<&DiseaseTable>) -> Result<&DiseaseTable> {
    diseases.ok_or_else(|| {
        Error::new(
            Status::InvalidState,
            "cell command requires registered diseases",
        )
    })
}

impl EarlyCommands {
    pub(crate) fn queue(
        &mut self,
        id: i32,
        payload: &[u8],
        world: &WorldShape,
        elements: &ElementTable,
        diseases: Option<&DiseaseTable>,
    ) -> Result<bool> {
        let expected = match id {
            SET_INSULATION | SET_STRENGTH => 8,
            CHANGE_PROPERTIES | MODIFY_DISEASE | SET_DEBUG => 12,
            MODIFY_ENERGY | CONSUME_DISEASE | MODIFY_BACKWALL => 16,
            _ => return Ok(false),
        };
        if payload.len() != expected {
            return Err(invalid(format!(
                "cell command {id} requires {expected} bytes"
            )));
        }
        if self.len() >= crate::frames::MAX_PENDING_COMMANDS {
            return Err(Error::new(
                Status::ResourceLimit,
                "too many queued cell commands",
            ));
        }
        // The official records retain padding but never interpret it. Accept all
        // float bit patterns where native arithmetic has been recovered.
        match id {
            SET_INSULATION | SET_STRENGTH => {
                let command = CellFloat {
                    cell: cell(world, i32at(payload, 0))?,
                    value: f32at(payload, 4),
                };
                if id == SET_INSULATION {
                    push(&mut self.insulation, command)?;
                } else {
                    push(&mut self.strength, command)?;
                }
            }
            MODIFY_ENERGY => {
                let target = cell(world, i32at(payload, 0))?;
                // +12 is a diagnostic id, read only by native error logging.
                push(
                    &mut self.energy,
                    Energy {
                        cell: target,
                        kilojoules: f32at(payload, 4),
                        max_temperature: f32at(payload, 8),
                        diagnostic_id: i32at(payload, 12),
                    },
                )?;
            }
            CHANGE_PROPERTIES => {
                let target = cell(world, i32at(payload, 0))?;
                disease_table(diseases)?;
                push(
                    &mut self.properties,
                    Properties {
                        cell: target,
                        callback: i32at(payload, 4),
                        mask: payload[8],
                        set: payload[9] != 0,
                    },
                )?;
            }
            CONSUME_DISEASE => {
                push(
                    &mut self.consume_disease,
                    ConsumeDisease {
                        cell: cell(world, i32at(payload, 0))?,
                        callback: i32at(payload, 4),
                        percent: f32at(payload, 8),
                        maximum: i32at(payload, 12),
                    },
                )?;
            }
            MODIFY_DISEASE => {
                let disease = payload[4];
                if disease != u8::MAX && disease as usize >= disease_table(diseases)?.diseases.len()
                {
                    return Err(invalid("cell command has an unknown disease index"));
                }
                push(
                    &mut self.modify_disease,
                    ModifyDisease {
                        cell: cell(world, i32at(payload, 0))?,
                        disease,
                        count: i32at(payload, 8),
                    },
                )?;
            }
            MODIFY_BACKWALL => {
                let public = i32at(payload, 0);
                let element = u16at(payload, 4);
                if element as usize >= elements.len() {
                    return Err(invalid("backwall command has an unknown element index"));
                }
                push(
                    &mut self.backwall,
                    Backwall {
                        public,
                        cell: cell(world, public)?,
                        element,
                        mass: f32at(payload, 8),
                        temperature: f32at(payload, 12),
                    },
                )?;
            }
            SET_DEBUG => {
                // HandleMessage ELF 0x4a670 replaces the frame's entire record.
                self.debug = DebugProperties {
                    raw: payload.try_into().unwrap(),
                };
                self.debug_pending = true;
            }
            _ => unreachable!(),
        }
        Ok(true)
    }
}
