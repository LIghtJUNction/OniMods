use crate::bytes::MAX_BATCH;
use crate::frames::MAX_PENDING_COMMANDS;
use crate::frames_plan::{parse, Action, Preview, FRAME, LOAD};
use crate::state::State;
use crate::status::{Error, Result, Status};
use std::ffi::c_void;

pub(crate) fn execute(
    state: &mut State,
    id: i32,
    length: usize,
    count: usize,
    bytes: &[u8],
) -> Result<*mut c_void> {
    state.require_active()?;
    if let Some(fault) = &state.fault {
        return Err(Error::new(
            Status::InvalidState,
            format!("simulation fault; Shutdown required: {fault}"),
        ));
    }
    if count > MAX_BATCH {
        return Err(Error::new(
            Status::ResourceLimit,
            "batch exceeds 4096 messages",
        ));
    }
    if count == 0 {
        return Ok(std::ptr::null_mut());
    }
    if state.save_image.is_some() {
        return Err(Error::new(
            Status::InvalidState,
            "EndSave is required before further messages",
        ));
    }
    let thermal_message = crate::thermal_components::is_message(id);
    let mass_message = crate::mass_components::is_message(id);
    let radiation_message = crate::radiation::is_message(id);
    if thermal_message || mass_message || radiation_message {
        if count != 1 {
            return Err(Error::new(
                Status::Unsupported,
                "component batch transactions are not implemented",
            ));
        }
        if state.queued_component_commands >= MAX_PENDING_COMMANDS {
            return Err(Error::new(
                Status::ResourceLimit,
                "pending component command limit exceeded",
            ));
        }
        let world = state.world.as_ref().ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                "component messages require an allocated physics world",
            )
        })?;
        let elements = state.elements.as_ref().unwrap();
        if thermal_message {
            state.thermal.queue(id, bytes)?;
        }
        if mass_message {
            let diseases = state.diseases.as_ref().ok_or_else(|| {
                Error::new(
                    Status::InvalidState,
                    "mass components require registered diseases",
                )
            })?;
            state.mass.queue(id, bytes, world, elements, diseases)?;
        }
        if radiation_message {
            state.radiation_emitters.queue(id, bytes, world, elements)?;
        }
        state.queued_component_commands += 1;
        return Ok(std::ptr::null_mut());
    }
    if id == FRAME && count != 1 {
        return Err(Error::new(
            Status::Unsupported,
            "multi-frame batch commit is not implemented",
        ));
    }
    if id == LOAD {
        if count != 1 {
            return Err(Error::new(
                Status::Unsupported,
                "batched Load is not supported",
            ));
        }
        if state.grid.as_ref().is_none_or(|grid| grid.started) {
            return Err(Error::new(
                Status::InvalidState,
                "Load requires an allocated, unstarted grid",
            ));
        }
        if !state.scheduler.cell_modifications.is_empty() || state.queued_component_commands != 0 {
            return Err(Error::new(
                Status::InvalidState,
                "finish queued commands before Load",
            ));
        }
        let elements = state
            .elements
            .as_ref()
            .ok_or_else(|| Error::new(Status::InvalidState, "Load requires registered elements"))?;
        let world = state.world.as_mut().ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                "Load requires an allocated physics world",
            )
        })?;
        crate::save::load_into(world, elements, state.diseases.as_ref(), bytes)?;
        let grid = state.grid.as_mut().unwrap();
        world.publish_to(grid);
        grid.initialized = true;
        grid.seed = world.rng;
        // Official managed Load only interprets this as a nonnull success token.
        return Ok(grid.descriptor() as *mut c_void);
    }
    let mut preview = Preview::from_state(state);
    let mut actions = Vec::new();
    actions
        .try_reserve_exact(count)
        .map_err(|_| Error::new(Status::ResourceLimit, "could not allocate batch plan"))?;
    for index in 0..count {
        let at = index * length;
        actions.push(
            parse(id, &bytes[at..at + length], &mut preview, state).map_err(|error| {
                Error::new(
                    error.status,
                    format!("batch item {index}: {}", error.message),
                )
            })?,
        );
    }
    let added_commands = actions
        .iter()
        .filter(|action| matches!(action, Action::Modify(_)))
        .count();
    state.scheduler.reserve(added_commands)?;
    let possible_events = state.scheduler.cell_modifications.len() + added_commands;
    if state.pending_events.substance_change_info.len() + possible_events > MAX_PENDING_COMMANDS {
        return Err(Error::new(
            Status::ResourceLimit,
            "publish frame events before queueing more commands",
        ));
    }
    state
        .pending_events
        .substance_change_info
        .try_reserve(possible_events)
        .map_err(|_| {
            Error::new(
                Status::ResourceLimit,
                "could not reserve frame substance-change events",
            )
        })?;
    // All parsing, allocation, lifecycle and generation checks completed above.
    // No fallible operation remains in this commit loop.
    let mut result = std::ptr::null_mut();
    for action in actions {
        result = std::ptr::null_mut();
        match action {
            Action::Allocate(grid, world, generation)
            | Action::Initialize(grid, world, generation) => {
                state.grid = Some(grid);
                state.world = Some(world);
                state.generation = generation;
                state.scheduler = Default::default();
                state.pending_events = Default::default();
                state.published_events = Default::default();
                state.thermal = Default::default();
                state.mass = Default::default();
                state.radiation_emitters = Default::default();
                state.queued_component_commands = 0;
            }
            Action::Start(legacy) => {
                let world = state.world.as_mut().unwrap();
                if let Some(thermal_boundary) = legacy {
                    world.sync_read_from_write();
                    world.settle_thermal_boundaries(thermal_boundary);
                }
                world.initialize_boundary();
                world.sync_read_from_write();
                let grid = state.grid.as_mut().unwrap();
                world.publish_to(grid);
                grid.started = true;
                result = grid.descriptor() as *mut c_void;
            }
            Action::Modify(replacement) => {
                state.scheduler.cell_modifications.push(replacement);
            }
            Action::Elements(table) => state.elements = Some(table),
            Action::Diseases(table) => state.diseases = Some(table),
            Action::Frame(frame) => state.process_frame(frame)?,
            Action::Prepare(visibility) => {
                result = state.prepare_publication(&visibility)?;
            }
        }
    }
    Ok(result)
}
