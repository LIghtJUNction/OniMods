//! World messages retain their synchronous versus frame-queued distinction.
use crate::state::State;
use crate::status::{Error, Result, Status};
use crate::world::PhysicsWorld;
use crate::world_messages::{self, ImmediateCommand, WorldMessages};

pub(crate) fn execute(
    state: &mut State,
    id: i32,
    length: usize,
    count: usize,
    bytes: &[u8],
) -> Result<bool> {
    if id == crate::interactions::MESSAGE {
        let elements = state
            .elements
            .as_ref()
            .ok_or_else(|| invalid_state("interactions require registered elements"))?;
        let mut replacement = Vec::new();
        for index in 0..count {
            // Both the outer payload and the borrowed record array belong to
            // the FFI caller. The parser copies all retained fields immediately.
            replacement = unsafe {
                crate::interactions::parse(
                    &bytes[index * length..(index + 1) * length],
                    elements.len(),
                    |address, size| state.overlaps_native_storage(address, size),
                )?
            };
        }
        state.interactions = replacement;
        return Ok(true);
    }
    if WorldMessages::is_message(id) {
        let shape = state
            .shape
            .as_ref()
            .ok_or_else(|| invalid_state("zone command requires an allocated world"))?;
        let mut staged = WorldMessages::default();
        for index in 0..count {
            staged.queue(id, &bytes[index * length..(index + 1) * length], shape)?;
        }
        state.world_messages.append(staged)?;
        return Ok(true);
    }
    if !world_messages::is_immediate(id) && id != crate::frames_commands::SAVED_OPTIONS {
        return Ok(false);
    }
    state.with_world_mut(|world| apply(world, id, length, count, bytes))?;
    if let (Some(world), Some(grid)) = (state.world.as_ref(), state.grid.as_mut()) {
        world.publish_to(grid);
    }
    // Started worlds publish through the worker's existing ready barrier.
    // Never rewrite a borrowed GameData buffer on the submission thread.
    Ok(true)
}

fn apply(
    world: &mut PhysicsWorld,
    id: i32,
    length: usize,
    count: usize,
    bytes: &[u8],
) -> Result<()> {
    if id == crate::frames_commands::SAVED_OPTIONS {
        if length != 2 {
            return Err(crate::bytes::invalid(
                "SetSavedOptions requires two bytes per record",
            ));
        }
        for payload in bytes.as_chunks::<2>().0 {
            crate::frames_commands::apply_saved_options(world, payload)?;
        }
        return Ok(());
    }
    let mut commands = Vec::new();
    commands.try_reserve_exact(count).map_err(|_| resource())?;
    for index in 0..count {
        commands.push(world_messages::parse_immediate(
            id,
            &bytes[index * length..(index + 1) * length],
            world,
        )?);
    }
    // InitializeVacuum appends an offset record. Reserve every possible target
    // before any commit, including vectors replacing the original offset list.
    let added = commands
        .iter()
        .filter(|command| matches!(command, ImmediateCommand::InitializeVacuum(_)))
        .count();
    world
        .world_offsets
        .try_reserve(added)
        .map_err(|_| resource())?;
    for command in &mut commands {
        if let ImmediateCommand::DefineOffsets(offsets) = command {
            offsets.try_reserve(added).map_err(|_| resource())?;
        }
    }
    for command in commands {
        world_messages::apply_immediate(command, world)?;
    }
    Ok(())
}

fn invalid_state(message: &str) -> Error {
    Error::new(Status::InvalidState, message)
}
fn resource() -> Error {
    Error::new(
        Status::ResourceLimit,
        "could not reserve world command batch",
    )
}
