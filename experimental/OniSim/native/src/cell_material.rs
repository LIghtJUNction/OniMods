use crate::bytes::{f32at, i32at, invalid, u16at};
use crate::frames::MAX_PENDING_COMMANDS;
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::worker_shape::WorldShape;

pub(crate) const MODIFY: i32 = -1252920804;
pub(crate) const DIG: i32 = 833038498;
pub(crate) const CONSUME: i32 = 1727657959;
pub(crate) const EMIT: i32 = 797274363;

#[derive(Clone, Copy)]
pub(crate) struct Modification {
    pub(crate) cell: usize,
    pub(crate) callback: i32,
    pub(crate) temperature: f32,
    pub(crate) mass: f32,
    pub(crate) count: i32,
    pub(crate) element: u16,
    pub(crate) replace_type: u8,
    pub(crate) disease: u8,
    pub(crate) subtype: u8,
}

#[derive(Clone, Copy)]
pub(crate) struct Dig {
    pub(crate) cell: usize,
    pub(crate) callback: i32,
    pub(crate) skip_event: bool,
    pub(crate) backwall: bool,
}

#[derive(Clone, Copy)]
pub(crate) struct Consumption {
    pub(crate) cell: usize,
    pub(crate) callback: i32,
    pub(crate) mass: f32,
    pub(crate) element: u16,
    pub(crate) radius: u8,
    pub(crate) height: u8,
}

#[derive(Clone, Copy)]
pub(crate) struct Emission {
    pub(crate) cell: usize,
    pub(crate) callback: i32,
    pub(crate) mass: f32,
    pub(crate) temperature: f32,
    pub(crate) count: i32,
    pub(crate) element: u16,
    pub(crate) disease: u8,
}

#[derive(Default)]
pub(crate) struct PendingCommands {
    pub(crate) modifications: Vec<Modification>,
    pub(crate) digs: Vec<Dig>,
    pub(crate) consumption: Vec<Consumption>,
    pub(crate) emission: Vec<Emission>,
}

#[derive(Default)]
pub(crate) struct CellMaterialQueue {
    pending: PendingCommands,
}

pub(crate) fn is_message(id: i32) -> bool {
    matches!(id, MODIFY | DIG | CONSUME | EMIT)
}

impl PendingCommands {
    pub(crate) fn len(&self) -> usize {
        self.modifications.len() + self.digs.len() + self.consumption.len() + self.emission.len()
    }
    pub(crate) fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

fn reserve<T>(queue: &mut Vec<T>) -> Result<()> {
    queue.try_reserve(1).map_err(|_| {
        Error::new(
            Status::ResourceLimit,
            "could not reserve material command queue",
        )
    })
}

impl CellMaterialQueue {
    pub(crate) fn is_empty(&self) -> bool {
        self.pending.is_empty()
    }
    pub(crate) fn len(&self) -> usize {
        self.pending.len()
    }
    pub(crate) fn take_pending(&mut self) -> PendingCommands {
        std::mem::take(&mut self.pending)
    }

    pub(crate) fn queue(
        &mut self,
        id: i32,
        payload: &[u8],
        world: &WorldShape,
        elements: &ElementTable,
        diseases: Option<&DiseaseTable>,
    ) -> Result<bool> {
        let size = match id {
            MODIFY => 28,
            DIG => 12,
            CONSUME => 16,
            EMIT => 24,
            _ => return Ok(false),
        };
        if payload.len() != size {
            return Err(invalid("material message has the wrong record length"));
        }
        if self.pending.len() >= MAX_PENDING_COMMANDS {
            return Err(Error::new(
                Status::ResourceLimit,
                "pending material command limit exceeded",
            ));
        }
        let public = i32at(payload, 0);
        if public < 0 || public as usize >= world.width * world.height {
            return Err(invalid("material command cell is outside the public grid"));
        }
        let cell = public as usize;
        let callback = i32at(payload, 4);
        let check_element = |element: u16| -> Result<()> {
            if element as usize >= elements.len() {
                return Err(invalid("material element index is invalid"));
            }
            Ok(())
        };
        let check_disease = |disease: u8| -> Result<()> {
            if disease != 255 && diseases.is_none_or(|table| disease as usize >= table.len()) {
                return Err(invalid("material disease index is not registered"));
            }
            Ok(())
        };
        match id {
            MODIFY => {
                let command = Modification {
                    cell,
                    callback,
                    temperature: f32at(payload, 8),
                    mass: f32at(payload, 12),
                    count: i32at(payload, 16),
                    element: u16at(payload, 20),
                    replace_type: payload[22],
                    disease: payload[23],
                    subtype: payload[24],
                };
                check_element(command.element)?;
                check_disease(command.disease)?;
                reserve(&mut self.pending.modifications)?;
                self.pending.modifications.push(command);
            }
            DIG => {
                reserve(&mut self.pending.digs)?;
                self.pending.digs.push(Dig {
                    cell,
                    callback,
                    skip_event: payload[8] != 0,
                    backwall: payload[9] != 0,
                });
            }
            CONSUME => {
                let command = Consumption {
                    cell,
                    callback,
                    mass: f32at(payload, 8),
                    element: u16at(payload, 12),
                    radius: payload[14],
                    height: payload[15],
                };
                check_element(command.element)?;
                if command.height != 0 {
                    let internal = world.internal_index(cell);
                    if internal % world.stride + command.radius as usize > world.stride
                        || internal / world.stride + command.height as usize > world.padded_height
                    {
                        return Err(invalid(
                            "rectangular consumption extends outside padded storage",
                        ));
                    }
                }
                reserve(&mut self.pending.consumption)?;
                self.pending.consumption.push(command);
            }
            EMIT => {
                let command = Emission {
                    cell,
                    callback,
                    mass: f32at(payload, 8),
                    temperature: f32at(payload, 12),
                    count: i32at(payload, 16),
                    element: u16at(payload, 20),
                    disease: payload[22],
                };
                check_element(command.element)?;
                check_disease(command.disease)?;
                reserve(&mut self.pending.emission)?;
                self.pending.emission.push(command);
            }
            _ => unreachable!(),
        }
        Ok(true)
    }
}
