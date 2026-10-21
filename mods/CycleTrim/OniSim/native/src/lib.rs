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
mod diagnostics;
mod diamond;
mod disease;
mod disease_emitters;
mod disease_fields;
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
mod interactions;
mod mass_components;
mod messages;
mod messages_cells;
#[cfg(test)]
mod messages_tests;
mod messages_world;
mod numeric;
mod offgas;
mod parallel;
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

use status::{Error, Result, Status, boundary};

#[cfg(not(all(target_os = "linux", target_arch = "x86_64", target_endian = "little")))]
compile_error!("this experimental ABI currently supports Linux x86_64 little-endian only");

const CAPABILITIES: &[u8] = concat!(
    "{\"abiVersion\":1,\"gameReady\":false,\"simulationStep\":true,",
    "\"gridOwnership\":\"native-stable-soa\",\"gridDescriptorBytes\":496,",
    "\"supportedMessages\":[1092408308,2062421945,-931446686,-1252920804,1108437482,825301935,-775326397,",
    "1078620451,-672538170,1739021608,1818001569,-1348791658,-456116629,-1338718217,",
    "-1586724321,-1993857213,697100730,1445724082,-374911358,-435115907,1020555667,",
    "-1387601379,-912908555,2024405073,1575539738,894417742,-505471181,403589164,",
    "-1524118282,1486783027,-1899123924,468135926,348345681,-1822987624,-781641650,",
    "-1505895314,-503965465,-704259919,-1914877797,377112707,-930289787,-457308393,",
    "-449718014,-1836204275,-895846551,-752676153,-1167792921,833038498,818320644,",
    "-898773121,1593243982,-469311643,-1683118492,1727657959,797274363,-605421515,",
    "-1019841536,-1853671274,1154135737],",
    "\"ignoredMessages\":[-563057023,-409964931,-727746602],",
    "\"callbacks\":false,\"callbackRecords\":true,\"saveLoad\":true,\"events\":true,",
    "\"flow\":true,\"textures\":false,\"radiationSimulation\":true,\"radiationStorage\":\"native\",",
    "\"tableSemantics\":\"physics-metadata\",\"snapshot\":\"explicit-copy\",",
    "\"storageMutationTiming\":\"frame-command-stage\",\"officialCommandScheduling\":false,",
    "\"execution\":\"asynchronous-frame-worker\",\"diagnosticCallbacks\":true,\"debugAssert\":true,\"systemInfo\":\"unavailable-linux\",\"runtimeValidated\":false,",
    "\"batchPrevalidation\":true,\"readyObservation\":true,\"externalPointerSynchronization\":\"caller-required\"}\0"
)
.as_bytes();

#[unsafe(no_mangle)]
pub extern "C" fn ONISIM_GetAbiVersion() -> u32 {
    boundary(0, || Ok(1))
}

// These two accessors preserve the previous operation's status and error buffer.
#[unsafe(no_mangle)]
pub extern "C" fn ONISIM_GetLastStatus() -> i32 {
    status::last_status()
}
#[unsafe(no_mangle)]
pub extern "C" fn ONISIM_GetLastError() -> *const c_char {
    status::last_error()
}

#[unsafe(no_mangle)]
pub extern "C" fn ONISIM_GetGeneration() -> u64 {
    boundary(0, || Ok(state::lock()?.generation))
}

#[unsafe(no_mangle)]
pub extern "C" fn ONISIM_GetCapabilities() -> *const c_char {
    boundary(std::ptr::null(), || Ok(CAPABILITIES.as_ptr().cast()))
}

#[unsafe(no_mangle)]
pub extern "C" fn SIM_Initialize(callback: state::Callback) {
    // The inspected game load path calls Initialize again when replacing an
    // existing simulation; its semantics reset the old session.
    boundary((), || state::reset_initialize(callback))
}

#[unsafe(no_mangle)]
pub extern "C" fn ONISIM_TryInitialize(callback: state::Callback) {
    // Unlike the official entry point, this project extension never takes
    // ownership away from an already initialized game simulation.
    boundary((), || state::lock()?.initialize(callback))
}

#[unsafe(no_mangle)]
pub extern "C" fn ONISIM_IsReady() -> i32 {
    boundary(-1, || {
        let worker = {
            let state = state::lock()?;
            state.require_active()?;
            state
                .worker
                .as_ref()
                .ok_or_else(|| {
                    Error::new(
                        Status::InvalidState,
                        "Start is required before observing readiness",
                    )
                })?
                .handle()
        };
        Ok(i32::from(worker.is_ready()?))
    })
}

#[unsafe(no_mangle)]
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
    if count > bytes::MAX_BATCH {
        return Err(Error::new(
            Status::ResourceLimit,
            "batch exceeds 4096 messages",
        ));
    }
    let (visibilities, worker, generation) = {
        let state = state::lock()?;
        if total != 0 && state.overlaps_native_storage(msg as usize, total) {
            return Err(bytes::invalid("message input overlaps native grid storage"));
        }
        let input = unsafe { bytes::input(msg, total)? };
        if input.len() != total {
            return Err(bytes::invalid("Prepare payload length is invalid"));
        }
        let mut visibilities = Vec::new();
        visibilities
            .try_reserve_exact(count)
            .map_err(|_| Error::new(Status::ResourceLimit, "could not reserve Prepare batch"))?;
        for index in 0..count {
            visibilities.push(messages::validate_prepare(
                &state,
                &input[index * length..(index + 1) * length],
            )?);
        }
        let worker = state
            .worker
            .as_ref()
            .ok_or_else(|| Error::new(Status::InvalidState, "Start is required before Prepare"))?
            .handle();
        (visibilities, worker, state.generation)
    };

    // Wait without the global FFI state lock. Shutdown can still wake and join
    // the worker while the game thread is waiting for a publication.
    let mut descriptor = std::ptr::null_mut();
    for visibility in visibilities {
        let mut ready = worker.wait_ready()?;
        descriptor = {
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
    }
    Ok(descriptor)
}

#[unsafe(no_mangle)]
/// # Safety
/// For a positive length, `msg` must remain readable and unmodified for that
/// many bytes until this call returns, including pointer-bearing payloads.
pub unsafe extern "C" fn SIM_HandleMessage(id: i32, length: i32, msg: *const u8) -> *mut c_void {
    diagnostics::official(
        std::ptr::null_mut(),
        || format!("message {id}"),
        || handle(id, length, 1, msg),
    )
}

#[unsafe(no_mangle)]
/// # Safety
/// `msg` must remain readable and unmodified for `length * count` bytes until
/// this call returns. Embedded pointers must satisfy their message contracts.
pub unsafe extern "C" fn SIM_HandleMessages(
    id: i32,
    length: i32,
    count: i32,
    msg: *const u8,
) -> *mut c_void {
    // Official Linux entry point uses length for EACH record and as its stride;
    // count > 0 returns the last single-message result, not a success marker.
    diagnostics::official(
        std::ptr::null_mut(),
        || format!("batch {id}"),
        || handle(id, length, count, msg),
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn ONISIM_GetGrid() -> *const u8 {
    boundary(std::ptr::null(), || Ok(state::lock()?.grid()?.descriptor()))
}

#[unsafe(no_mangle)]
/// # Safety
/// A nonnull destination with positive capacity must designate that many
/// writable bytes exclusively for this call. Null requests only the size.
pub unsafe extern "C" fn ONISIM_CopyGridImage(destination: *mut u8, capacity: i32) -> i32 {
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

#[unsafe(no_mangle)]
/// # Safety
/// `size` must designate a writable i32 exclusively for this call. The returned
/// image is borrowed until the next BeginSave, EndSave or session shutdown.
pub unsafe extern "C" fn SIM_BeginSave(size: *mut i32, x: i32, y: i32) -> *const u8 {
    diagnostics::official(
        std::ptr::null(),
        || "BeginSave".into(),
        || {
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
        },
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn SIM_EndSave() {
    diagnostics::official(
        (),
        || "EndSave".into(),
        || {
            let mut state = state::lock()?;
            // Official 0x324b0 frees a retained buffer only when present.
            // Empty and repeated releases, including after Shutdown, are no-ops.
            state.save_image = None;
            Ok(())
        },
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn SIM_DebugCrash() {
    boundary((), || {
        let callback = state::lock()?.callback;
        diagnostics::debug_crash(callback);
        Ok(())
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn SYSINFO_Acquire() -> *const c_char {
    // Pinned Linux GetJSONSysInfo (ELF 0x33e70) returns null. A textual
    // placeholder falsely promises an acquired JSON document to managed code.
    boundary(std::ptr::null(), || Ok(std::ptr::null()))
}

#[unsafe(no_mangle)]
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
        assert_eq!(super::ONISIM_IsReady(), -1);
        assert_eq!(ONISIM_GetLastStatus(), Status::InvalidState as i32);
        super::SIM_EndSave();
        assert_eq!(ONISIM_GetLastStatus(), 0);
        assert!(super::SYSINFO_Acquire().is_null());
        assert_eq!(ONISIM_GetLastStatus(), 0);
        super::SYSINFO_Release();
        super::SYSINFO_Release();
        assert_eq!(ONISIM_GetLastStatus(), 0);
        super::SIM_EndSave();
        assert_eq!(ONISIM_GetLastStatus(), 0);

        ONISIM_TryInitialize(None);
        assert_eq!(ONISIM_GetLastStatus(), 0);
        let first_generation = ONISIM_GetGeneration();

        ONISIM_TryInitialize(None);
        assert_eq!(ONISIM_GetLastStatus(), Status::InvalidState as i32);
        assert_eq!(ONISIM_GetGeneration(), first_generation);

        SIM_Initialize(None);
        assert_eq!(ONISIM_GetLastStatus(), 0);
        assert!(ONISIM_GetGeneration() > first_generation);

        static REPORTS: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
        extern "C" fn receiver(_: i32, _: *const std::ffi::c_void) -> i32 {
            // Reentry proves the fatal report runs after releasing FFI locks.
            ONISIM_GetGeneration();
            REPORTS.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
            0
        }
        SIM_Initialize(Some(receiver));
        crate::conduit_ffi::ConduitTemperatureManager_Initialize();
        assert_eq!(
            crate::conduit_ffi::ConduitTemperatureManager_Add(10001.0, 1.0, 0, 0, 1.0, 1.0, false),
            -1
        );
        assert_eq!(ONISIM_GetLastStatus(), Status::InvalidArgument as i32);
        assert_eq!(REPORTS.load(std::sync::atomic::Ordering::Relaxed), 1);
        // A failed Add allocated no slot, and a second error does not spam the
        // registered managed crash reporter in the same simulation session.
        assert_eq!(
            crate::conduit_ffi::ConduitTemperatureManager_Set(0, 10001.0, 1.0, 0),
            -1
        );
        assert_eq!(REPORTS.load(std::sync::atomic::Ordering::Relaxed), 1);
        // SAFETY: null selects the manager-only path and reads no foreign records.
        let output =
            unsafe { crate::conduit_ffi::ConduitTemperatureManager_Update(0.2, std::ptr::null()) };
        assert!(!output.is_null());
        assert_eq!(unsafe { std::ptr::read_unaligned(output.cast::<i32>()) }, 0);

        SIM_Shutdown();
    }
}

mod id_validation;
