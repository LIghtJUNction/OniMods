//! Route public cell messages to the existing ordered frame queues.
use crate::cell_material::CellMaterialQueue;
use crate::frames_commands::EarlyCommands;
use crate::state::State;
use crate::status::{Error, Result, Status};

pub(crate) fn execute(
    state: &mut State,
    id: i32,
    length: usize,
    count: usize,
    bytes: &[u8],
) -> Result<bool> {
    let material = crate::cell_material::is_message(id);
    let early = crate::frames_commands::is_message(id);
    if !material && !early {
        return Ok(false);
    }
    let shape = state.shape.as_ref().ok_or_else(|| {
        Error::new(
            Status::InvalidState,
            "cell commands require an allocated world",
        )
    })?;
    let elements = state.elements.as_deref().ok_or_else(|| {
        Error::new(
            Status::InvalidState,
            "cell commands require registered elements",
        )
    })?;
    let diseases = state.diseases.as_deref();
    // Stage the entire incoming batch. A bad later record cannot enqueue an
    // earlier record, and growing the destination cannot partially commit it.
    let mut new_material = CellMaterialQueue::default();
    let mut new_early = EarlyCommands::default();
    for index in 0..count {
        let at = index * length;
        let payload = &bytes[at..at + length];
        let result = if material {
            new_material.queue(id, payload, shape, elements, diseases)
        } else {
            new_early.queue(id, payload, shape, elements, diseases)
        };
        result.map_err(|error| {
            Error::new(
                error.status,
                format!("batch item {index}: {}", error.message),
            )
        })?;
    }
    if material {
        state.material.append(new_material)?;
    } else {
        state.early.append(new_early)?;
    }
    Ok(true)
}
