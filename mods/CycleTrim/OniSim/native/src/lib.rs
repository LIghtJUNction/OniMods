use std::ffi::{c_char, c_void};

mod bytes;
mod cell_material;
mod cell_material_add_gas;
mod cell_material_add_liquid;
mod cell_material_add_solid;
mod cell_material_commands;
mod cell_material_mass;
mod cell_material_rect;
mod component_handles;
mod conduit;
mod conduit_ffi;
mod diamond;
mod disease;
mod disease_emitters;
mod event_consolidation;
mod event_records;
mod event_sort;
mod events;
mod float_env;
mod fluid;
mod fluid_displacement;
mod fluid_helpers;
mod frames;
mod frames_commands;
mod frames_data;
mod frames_engine;
mod frames_plan;
mod gas;
mod grid;
mod mass_components;
mod messages;
mod numeric;
mod offgas;
mod partial_melt;
mod partial_transition;
mod postprocess;
mod postprocess_pressure;
mod presentation;
mod radiation;
mod radiation_rays;
mod radiation_region;
mod save;
mod simulation_step;
mod simulation_step_fluid;
mod state;
mod status;
mod sublimation;
mod thermal;
mod thermal_components;
mod transition;
mod unstable;
mod worker;
mod worker_shape;
// Retain all decoded metadata and raw fields, including those unused by kernels.
#[allow(dead_code)]
mod tables;
mod world;
mod world_cells;
mod world_messages;

use status::{boundary, Error, Result, Status};

#[cfg(not(all(target_os = "linux", target_arch = "x86_64", target_endian = "little")))]
compile_error!("this experimental ABI currently supports Linux x86_64 little-endian only");

const CAPABILITIES: &[u8] = concat!(
    "{\"abiVersion\":1,\"gameReady\":false,\"simulationStep\":true,",
    "\"gridOwnership\":\"native-stable-soa\",\"gridDescriptorBytes\":496,",
    "\"supportedMessages\":[1092408308,2062421945,-931446686,-1252920804,1108437482,825301935,",
    "-775326397,1078620451,-672538170,1739021608,1818001569,-1348791658,-456116629,",
    "-1338718217,-1586724321,-1993857213,697100730,1445724082,-374911358,-435115907,",
    "1020555667,-1387601379,-912908555,2024405073,1575539738,894417742,-505471181,",
    "403589164,-1524118282,1486783027,-1899123924,468135926,348345681,-1822987624,",
    "-781641650,-1505895314,-503965465,-704259919,-1914877797,377112707],",
    "\"callbacks\":false,\"callbackRecords\":true,\"saveLoad\":true,\"events\":true,",
    "\"flow\":true,\"textures\":false,\"radiationSimulation\":true,\"radiationStorage\":\"native\",",
    "\"tableSemantics\":\"physics-metadata\",\"snapshot\":\"explicit-copy\",",
    "\"storageMutationTiming\":\"frame-command-stage\",\"officialCommandScheduling\":false,",
    "\"execution\":\"synchronous-supported-phases\",\"runtimeValidated\":false,",
    "\"batchPrevalidation\":true,\"externalPointerSynchronization\":\"caller-required\"}\0"
)
.as_bytes();

#[no_mangle]
pub extern "C" fn ONISIM_GetAbiVersion() -> u32 {
    boundary(0, || Ok(1))
}

// These two accessors preserve the previous operation's status and error buffer.
#[no_mangle]
pub extern "C" fn ONISIM_GetLastStatus() -> i32 {
    status::last_status()
}
#[no_mangle]
pub extern "C" fn ONISIM_GetLastError() -> *const c_char {
    status::last_error()
}

#[no_mangle]
pub extern "C" fn ONISIM_GetGeneration() -> u64 {
    boundary(0, || Ok(state::lock()?.generation))
}

#[no_mangle]
pub extern "C" fn ONISIM_GetCapabilities() -> *const c_char {
    boundary(std::ptr::null(), || Ok(CAPABILITIES.as_ptr().cast()))
}

#[no_mangle]
pub extern "C" fn SIM_Initialize(callback: state::Callback) {
    // The inspected game load path calls Initialize again when replacing an
    // existing simulation; its semantics reset the old session.
    boundary((), || state::reset_initialize(callback))
}

#[no_mangle]
pub extern "C" fn ONISIM_TryInitialize(callback: state::Callback) {
    // Unlike the official entry point, this project extension never takes
    // ownership away from an already initialized game simulation.
    boundary((), || state::lock()?.initialize(callback))
}

#[no_mangle]
pub extern "C" fn SIM_Shutdown() {
    boundary((), state::shutdown)
}

fn handle(id: i32, length: i32, count: i32, msg: *const u8) -> Result<*mut c_void> {
    if length < 0 || count < 0 {
        return Err(bytes::invalid(
            "message length and count must be nonnegative",
        ));
    }
    let total = (length as usize)
        .checked_mul(count as usize)
        .ok_or_else(|| Error::new(Status::ResourceLimit, "message batch length overflow"))?;
    if id == frames_plan::PREPARE {
        return prepare(length as usize, count as usize, msg, total);
    }
    let mut state = state::lock()?;
    if total != 0 && state.overlaps_native_storage(msg as usize, total) {
        return Err(bytes::invalid("message input overlaps native grid storage"));
    }
    let input = unsafe { bytes::input(msg, total)? };
    messages::execute(&mut state, id, length as usize, count as usize, input)
}

fn prepare(length: usize, count: usize, msg: *const u8, total: usize) -> Result<*mut c_void> {
    if count == 0 {
        return Ok(std::ptr::null_mut());
    }
    if count != 1 {
        return Err(Error::new(
            Status::Unsupported,
            "batched Prepare is not supported",
        ));
    }
    let (visibility, worker, generation) = {
        let state = state::lock()?;
        if total != 0 && state.overlaps_native_storage(msg as usize, total) {
            return Err(bytes::invalid("message input overlaps native grid storage"));
        }
        let input = unsafe { bytes::input(msg, total)? };
        if input.len() != length {
            return Err(bytes::invalid("Prepare payload length is invalid"));
        }
        let visibility = messages::validate_prepare(&state, input)?;
        let worker = state
            .worker
            .as_ref()
            .ok_or_else(|| Error::new(Status::InvalidState, "Start is required before Prepare"))?
            .handle();
        (visibility, worker, state.generation)
    };

    // Wait without the global FFI state lock. Shutdown can still wake and join
    // the worker while the game thread is waiting for a publication.
    let mut ready = worker.wait_ready()?;
    let descriptor = {
        let mut state = state::lock()?;
        if !state.active || state.generation != generation || state.worker.is_none() {
            return Err(Error::new(
                Status::InvalidState,
                "simulation session changed while Prepare was waiting",
            ));
        }
        state.finish_prepare(ready.data_mut())?
    };
    ready.release_with(move |old| old.visible.copy_from_slice(&visibility))?;
    Ok(descriptor)
}

#[no_mangle]
pub extern "C" fn SIM_HandleMessage(id: i32, length: i32, msg: *const u8) -> *mut c_void {
    boundary(std::ptr::null_mut(), || handle(id, length, 1, msg))
}

#[no_mangle]
pub extern "C" fn SIM_HandleMessages(
    id: i32,
    length: i32,
    count: i32,
    msg: *const u8,
) -> *mut c_void {
    // Official Linux entry point uses length for EACH record and as its stride;
    // count > 0 returns the last single-message result, not a success marker.
    boundary(std::ptr::null_mut(), || handle(id, length, count, msg))
}

#[no_mangle]
pub extern "C" fn ONISIM_GetGrid() -> *const u8 {
    boundary(std::ptr::null(), || Ok(state::lock()?.grid()?.descriptor()))
}

#[no_mangle]
pub extern "C" fn ONISIM_CopyGridImage(destination: *mut u8, capacity: i32) -> i32 {
    boundary(-1, || {
        if capacity < 0 {
            return Err(bytes::invalid("snapshot capacity must be nonnegative"));
        }
        let state = state::lock()?;
        let grid = state.initialized_grid()?;
        let required = grid.image_len();
        if destination.is_null() || capacity == 0 {
            return Ok(required as i32);
        }
        if (capacity as usize) < required {
            return Err(Error::new(
                Status::BufferTooSmall,
                format!("snapshot requires {required} bytes"),
            ));
        }
        if (destination as usize).checked_add(required).is_none()
            || state.overlaps_native_storage(destination as usize, required)
        {
            return Err(bytes::invalid(
                "snapshot destination overflows or overlaps native grid storage",
            ));
        }
        let image = unsafe { std::slice::from_raw_parts_mut(destination, required) };
        grid.write_image(image);
        Ok(required as i32)
    })
}

fn unsupported(operation: &str) -> Error {
    Error::new(
        Status::Unsupported,
        format!("{operation} is not implemented"),
    )
}

#[no_mangle]
pub extern "C" fn SIM_BeginSave(size: *mut i32, x: i32, y: i32) -> *const u8 {
    boundary(std::ptr::null(), || {
        if size.is_null() {
            return Err(bytes::invalid(
                "save size pointer must be writable and nonnull",
            ));
        }
        let mut state = state::lock()?;
        if (size as usize)
            .checked_add(std::mem::size_of::<i32>())
            .is_none()
            || state.overlaps_native_storage(size as usize, std::mem::size_of::<i32>())
        {
            return Err(bytes::invalid(
                "save size pointer overflows or overlaps native grid storage",
            ));
        }
        state.require_active()?;
        state.initialized_grid()?;
        // The official BeginSave releases the previous borrowed save image
        // before starting another serialization pass.
        state.save_image = None;
        unsafe {
            std::ptr::write_unaligned(size, 0);
        }
        if state.fault.is_some() {
            return Err(Error::new(
                Status::InvalidState,
                "simulation is faulted; Shutdown is required before BeginSave",
            ));
        }
        let elements = state.elements.as_ref().cloned().ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                "BeginSave requires registered elements",
            )
        })?;
        let diseases = state.diseases.clone();
        let image = if let Some(world) = state.world.as_mut() {
            save::serialize(world, &elements, diseases.as_deref(), x, y)?
        } else {
            let engine = state.engine.as_ref().cloned().ok_or_else(|| {
                Error::new(
                    Status::InvalidState,
                    "BeginSave requires an active simulation Engine",
                )
            })?;
            let mut engine = engine.lock().map_err(|_| {
                Error::new(
                    Status::InternalFailure,
                    "simulation Engine mutex is poisoned",
                )
            })?;
            save::serialize(&mut engine.world, &elements, diseases.as_deref(), x, y)?
        };
        let length = i32::try_from(image.len()).map_err(|_| {
            Error::new(
                Status::ResourceLimit,
                "save image exceeds official ABI length",
            )
        })?;
        state.save_image = Some(image);
        let save_image = state.save_image.as_ref().unwrap();
        unsafe {
            std::ptr::write_unaligned(size, length);
        }
        Ok(save_image.as_ptr())
    })
}

#[no_mangle]
pub extern "C" fn SIM_EndSave() {
    boundary((), || {
        let mut state = state::lock()?;
        state.require_active()?;
        if state.save_image.take().is_none() {
            return Err(Error::new(Status::InvalidState, "no save image is active"));
        }
        Ok(())
    })
}

#[no_mangle]
pub extern "C" fn SIM_DebugCrash() {
    boundary((), || Err(unsupported("debug crash")))
}

const SYSTEM_INFO: &[u8] =
    b"OniSim experimental storage core; Linux x86_64; ABI 1; simulation unavailable\0";

#[no_mangle]
pub extern "C" fn SYSINFO_Acquire() -> *const c_char {
    boundary(std::ptr::null(), || Ok(SYSTEM_INFO.as_ptr().cast()))
}

#[no_mangle]
pub extern "C" fn SYSINFO_Release() {
    boundary((), || Ok(()))
}

#[cfg(test)]
mod tests {
    use super::{
        ONISIM_GetGeneration, ONISIM_GetLastStatus, ONISIM_TryInitialize, SIM_Initialize,
        SIM_Shutdown,
    };
    use crate::status::Status;

    #[test]
    fn exclusive_initialize_and_official_reset_have_distinct_semantics() {
        SIM_Shutdown();

        ONISIM_TryInitialize(None);
        assert_eq!(ONISIM_GetLastStatus(), 0);
        let first_generation = ONISIM_GetGeneration();

        ONISIM_TryInitialize(None);
        assert_eq!(ONISIM_GetLastStatus(), Status::InvalidState as i32);
        assert_eq!(ONISIM_GetGeneration(), first_generation);

        SIM_Initialize(None);
        assert_eq!(ONISIM_GetLastStatus(), 0);
        assert!(ONISIM_GetGeneration() > first_generation);

        SIM_Shutdown();
    }
}
