use std::ffi::c_void;
use std::sync::{Mutex, MutexGuard, OnceLock};

use crate::conduit::ConduitManager;
use crate::status::{Error, Result, Status};
use crate::tables::ElementRecord;

fn singleton() -> &'static Mutex<Option<ConduitManager>> {
    static MANAGER: OnceLock<Mutex<Option<ConduitManager>>> = OnceLock::new();
    MANAGER.get_or_init(|| Mutex::new(None))
}

fn lock() -> Result<MutexGuard<'static, Option<ConduitManager>>> {
    singleton().lock().map_err(|_| {
        Error::new(
            Status::InternalFailure,
            "conduit temperature mutex is poisoned",
        )
    })
}

fn manager(guard: &mut Option<ConduitManager>) -> Result<&mut ConduitManager> {
    guard.as_mut().ok_or_else(|| {
        Error::new(
            Status::InvalidState,
            "ConduitTemperatureManager_Initialize is required",
        )
    })
}

fn element(hash: i32) -> Result<ElementRecord> {
    let state = crate::state::lock()?;
    let table = state.elements.as_ref().ok_or_else(|| {
        Error::new(
            Status::InvalidState,
            "element table is required for conduit contents",
        )
    })?;
    table
        .elements
        .iter()
        .rev()
        .find(|element| element.record.id == hash)
        .map(|element| element.record.clone())
        .ok_or_else(|| {
            Error::new(
                Status::InvalidArgument,
                "conduit contents element hash is absent from the element table",
            )
        })
}

fn contents_temperature(value: f32) -> Result<()> {
    // Official Set emits fatal diagnostic id 1 before replacing values above
    // 10000 K. Reject before mutation so ignored managed return values cannot
    // hide that failure. Other bit patterns retain the recovered kernel rules.
    if value > 10000.0 {
        return Err(Error::new(
            Status::InvalidArgument,
            format!("contents have invalid temperature {value}"),
        ));
    }
    Ok(())
}

pub(crate) fn shutdown() -> Result<()> {
    let mut guard = singleton()
        .lock()
        .unwrap_or_else(|poison| poison.into_inner());
    *guard = None;
    singleton().clear_poison();
    Ok(())
}

// Sim::Main releases the opposite removal queue immediately before each Step.
pub(crate) fn release_queued_handles() -> Result<()> {
    if let Some(manager) = lock()?.as_mut() {
        manager.release_queued_handles()?;
    }
    Ok(())
}

#[unsafe(no_mangle)]
pub extern "C" fn ConduitTemperatureManager_Initialize() {
    crate::diagnostics::official(
        (),
        || "ConduitTemperatureManager_Initialize".into(),
        || {
            *lock()? = Some(ConduitManager::default());
            Ok(())
        },
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn ConduitTemperatureManager_Shutdown() {
    crate::diagnostics::official((), || "ConduitTemperatureManager_Shutdown".into(), shutdown)
}

#[unsafe(no_mangle)]
pub extern "C" fn ConduitTemperatureManager_Add(
    temperature: f32,
    mass: f32,
    contents_element_hash: i32,
    structure_temperature_handle: i32,
    conduit_heat_capacity: f32,
    conduit_thermal_conductivity: f32,
    insulated: bool,
) -> i32 {
    crate::diagnostics::official(
        -1,
        || "ConduitTemperatureManager_Add".into(),
        || {
            contents_temperature(temperature)?;
            let element = element(contents_element_hash)?;
            manager(&mut *lock()?)?.add(
                temperature,
                mass,
                &element,
                structure_temperature_handle,
                conduit_heat_capacity,
                conduit_thermal_conductivity,
                insulated,
            )
        },
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn ConduitTemperatureManager_Set(
    handle: i32,
    temperature: f32,
    mass: f32,
    contents_element_hash: i32,
) -> i32 {
    crate::diagnostics::official(
        -1,
        || "ConduitTemperatureManager_Set".into(),
        || {
            contents_temperature(temperature)?;
            let element = element(contents_element_hash)?;
            manager(&mut *lock()?)?.set(handle, temperature, mass, &element)?;
            // The managed ABI declares int but discards it; the official C++ body is void.
            Ok(0)
        },
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn ConduitTemperatureManager_Remove(handle: i32) {
    crate::diagnostics::official(
        (),
        || "ConduitTemperatureManager_Remove".into(),
        || manager(&mut *lock()?)?.remove(handle),
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn ConduitTemperatureManager_Clear() {
    crate::diagnostics::official(
        (),
        || "ConduitTemperatureManager_Clear".into(),
        || {
            manager(&mut *lock()?)?.clear();
            Ok(())
        },
    )
}

#[unsafe(no_mangle)]
/// # Safety
/// A nonnull `building_data` must retain readable BuildingTemperatureInfo
/// records for every building index registered with this manager.
pub unsafe extern "C" fn ConduitTemperatureManager_Update(
    dt: f32,
    building_data: *const c_void,
) -> *const c_void {
    crate::diagnostics::official(
        std::ptr::null(),
        || "ConduitTemperatureManager_Update".into(),
        || {
            // The global lock order is State -> Engine -> Conduit. The worker can
            // release handles under Engine -> Conduit, but never obtains State.
            let mut state = crate::state::lock()?;
            state.require_conduit_update()?;
            let mut guard = lock()?;
            let manager = manager(&mut guard)?;
            let plan = unsafe { manager.prepare_update(dt, building_data.cast())? };
            state.queue_conduit_energy(&plan.energy)?;
            // Queue admission is atomic. A rejected batch leaves the live manager,
            // its published arrays, and every building energy queue unchanged.
            Ok(manager.commit_update(plan).cast())
        },
    )
}

#[cfg(test)]
mod tests {
    use super::contents_temperature;

    #[test]
    fn fatal_temperature_boundary_preserves_other_native_bit_patterns() {
        assert!(contents_temperature(10000.0).is_ok());
        assert!(contents_temperature(f32::from_bits(10000.0_f32.to_bits() + 1)).is_err());
        assert!(contents_temperature(f32::INFINITY).is_err());
        assert!(contents_temperature(0.0).is_ok());
        assert!(contents_temperature(-1.0).is_ok());
        assert!(contents_temperature(f32::NAN).is_ok());
    }
}
