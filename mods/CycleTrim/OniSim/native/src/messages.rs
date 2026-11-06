use crate::bytes::MAX_BATCH;
use crate::frames::MAX_PENDING_COMMANDS;
use crate::frames_plan::{Action, LOAD, PREPARE, Preview, parse};
use crate::state::State;
use crate::status::{Error, Result, Status};
use std::ffi::c_void;

pub(crate) fn validate_prepare(state: &State, bytes: &[u8]) -> Result<Vec<u8>> {
    state.require_active()?;
    if state.fault.is_some() || state.save_image.is_some() {
        return Err(Error::new(
            Status::InvalidState,
            "Prepare requires a healthy session without an active save image",
        ));
    }
    let mut preview = Preview::from_state(state);
    match parse(PREPARE, bytes, &mut preview, state)? {
        Action::Prepare(visibility) => Ok(visibility),
        _ => unreachable!("PREPARE parser returned a different action"),
    }
}

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
    // Present in the managed enum, absent from BOTH dispatch registries in the
    // pinned 744825 native library. Its SIM_HandleMessage returns null without
    // consuming their payload. Visibility is submitted through Prepare.
    if matches!(id, -563_057_023 | -409_964_931 | -727_746_602) {
        return Ok(std::ptr::null_mut());
    }
    if crate::messages_cells::execute(state, id, length, count, bytes)? {
        return Ok(std::ptr::null_mut());
    }
    if crate::messages_world::execute(state, id, length, count, bytes)? {
        return Ok(std::ptr::null_mut());
    }
    let thermal_message = crate::thermal_components::is_message(id);
    let mass_message = crate::mass_components::is_message(id);
    let radiation_message = crate::radiation::is_message(id);
    if thermal_message || mass_message || radiation_message {
        if state.queued_component_commands.saturating_add(count) > MAX_PENDING_COMMANDS {
            return Err(Error::new(
                Status::ResourceLimit,
                "pending component command limit exceeded",
            ));
        }
        let shape = state.shape.as_ref().ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                "component messages require an allocated world shape",
            )
        })?;
        let elements = state.elements.as_ref().unwrap();
        let mut thermal = crate::thermal_components::ThermalComponents::default();
        let mut mass = crate::mass_components::MassComponents::default();
        let mut radiation = crate::radiation::RadiationEmitters::default();
        let diseases = state.diseases.as_deref();
        for index in 0..count {
            let payload = &bytes[index * length..(index + 1) * length];
            let queued = if thermal_message {
                thermal.queue(id, payload)
            } else if mass_message {
                mass.queue(
                    id,
                    payload,
                    shape,
                    elements,
                    diseases.ok_or_else(|| {
                        Error::new(
                            Status::InvalidState,
                            "mass components require registered diseases",
                        )
                    })?,
                )
                .map(|_| ())
            } else {
                radiation.queue(id, payload, shape).map(|_| ())
            };
            queued.map_err(|error| {
                Error::new(
                    error.status,
                    format!("batch item {index}: {}", error.message),
                )
            })?;
        }
        if thermal_message {
            state.thermal.append_pending(thermal)?;
        }
        if mass_message {
            state.mass.append_pending(mass)?;
        }
        if radiation_message {
            state.radiation_emitters.append_pending(radiation)?;
        }
        state.queued_component_commands += count;
        return Ok(std::ptr::null_mut());
    }
    if id == LOAD {
        if state.grid.as_ref().is_none_or(|grid| grid.started) {
            return Err(Error::new(
                Status::InvalidState,
                "Load requires an allocated, unstarted grid",
            ));
        }
        if !state.material.is_empty()
            || state.early.len() != 0
            || !state.world_messages.is_empty()
            || state.queued_component_commands != 0
        {
            return Err(Error::new(
                Status::InvalidState,
                "finish queued commands before Load",
            ));
        }
        let elements = state
            .elements
            .as_deref()
            .ok_or_else(|| Error::new(Status::InvalidState, "Load requires registered elements"))?;
        let diseases = state.diseases.as_deref();
        let world = state.world.as_mut().ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                "Load requires an allocated physics world",
            )
        })?;
        let mut plans = Vec::new();
        plans
            .try_reserve_exact(count)
            .map_err(|_| Error::new(Status::ResourceLimit, "could not reserve Load batch"))?;
        let mut options = world.saved_options;
        for index in 0..count {
            let plan = crate::save::plan_load(
                world,
                elements,
                diseases,
                &bytes[index * length..(index + 1) * length],
                options,
            )
            .map_err(|error| {
                Error::new(
                    error.status,
                    format!("batch item {index}: {}", error.message),
                )
            })?;
            options = plan.saved_options;
            plans.push(plan);
        }
        for plan in plans {
            plan.apply(world);
        }
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
                state.shape = state
                    .world
                    .as_ref()
                    .map(crate::worker_shape::WorldShape::from_world);
                state.generation = generation;
                state.scheduler = Default::default();
                state.thermal = Default::default();
                state.mass = Default::default();
                state.radiation_emitters = Default::default();
                state.queued_component_commands = 0;
            }
            Action::Start(legacy) => {
                result = state.start_worker(legacy)?;
            }
            Action::Elements(table) => state.elements = Some(std::sync::Arc::new(table)),
            Action::Diseases(table) => state.diseases = Some(std::sync::Arc::new(table)),
            Action::Frame(frame) => {
                state.submit_frame(frame)?;
                state.scheduler.completed_frames += 1;
            }
            Action::Prepare(visibility) => {
                let _ = visibility;
                return Err(Error::new(
                    Status::InvalidState,
                    "Prepare must use the worker publication barrier",
                ));
            }
        }
    }
    Ok(result)
}
