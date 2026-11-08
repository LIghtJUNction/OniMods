use std::sync::{Arc, Mutex, OnceLock};
use onisim_gpu::gpu_context::GpuContext;
use onisim_gpu::gpu_pathfinder::{ChoreCandidate, GpuPathfinder};
use crate::status::{Error, Result, Status, boundary};

fn get_pathfinder() -> Result<&'static Mutex<GpuPathfinder>> {
    static PATHFINDER: OnceLock<Mutex<GpuPathfinder>> = OnceLock::new();
    if let Some(pf) = PATHFINDER.get() {
        return Ok(pf);
    }
    let context = Arc::new(
        onisim_gpu::pollster::block_on(GpuContext::new())
            .map_err(|e| Error::new(Status::InternalFailure, format!("GPU init failed: {}", e)))?,
    );
    let pf = GpuPathfinder::new(context);
    let _ = PATHFINDER.set(Mutex::new(pf));
    Ok(PATHFINDER.get().unwrap())
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ONISIM_GpuPathfinder_InitGrid(
    width: u32,
    height: u32,
    nav_mask_ptr: *const u32,
    count: u32,
) -> i32 {
    boundary(-1, || {
        if nav_mask_ptr.is_null() || count != width * height {
            return Err(Error::new(Status::InvalidArgument, "invalid nav grid buffer"));
        }
        let nav_mask = unsafe { std::slice::from_raw_parts(nav_mask_ptr, count as usize) };
        let mutex = get_pathfinder()?;
        let mut pf = mutex.lock().map_err(|_| Error::new(Status::InternalFailure, "pathfinder poisoned"))?;
        pf.ensure_grid(width, height, nav_mask)
            .map_err(|e| Error::new(Status::InternalFailure, e))?;
        Ok(0)
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn ONISIM_GpuPathfinder_EvaluateChores(
    origin_cell: u32,
    chores_ptr: *mut ChoreCandidate,
    count: u32,
) -> i32 {
    boundary(-1, || {
        if chores_ptr.is_null() || count == 0 {
            return Ok(0);
        }
        let chores = unsafe { std::slice::from_raw_parts_mut(chores_ptr, count as usize) };
        let mutex = get_pathfinder()?;
        let mut pf = mutex.lock().map_err(|_| Error::new(Status::InternalFailure, "pathfinder poisoned"))?;
        pf.evaluate_chores(origin_cell, chores)
            .map_err(|e| Error::new(Status::InternalFailure, e))?;
        Ok(0)
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_gpu_pathfinder_ffi() {
        let width = 4u32;
        let height = 4u32;
        let nav_grid: [u32; 16] = [
            1, 1, 1, 1,
            1, 0, 0, 1,
            1, 1, 1, 1,
            1, 1, 1, 1,
        ];
        let rc = unsafe {
            ONISIM_GpuPathfinder_InitGrid(width, height, nav_grid.as_ptr(), 16)
        };
        assert_eq!(rc, 0);

        let mut chores = [
            ChoreCandidate { chore_id: 1, target_cell: 2, priority: 5, cost: 0 },
            ChoreCandidate { chore_id: 2, target_cell: 5, priority: 10, cost: 0 }, // obstacle
        ];
        let rc2 = unsafe {
            ONISIM_GpuPathfinder_EvaluateChores(0, chores.as_mut_ptr(), 2)
        };
        assert_eq!(rc2, 0);
        assert_eq!(chores[0].cost, 2);
        assert_eq!(chores[1].cost, -1);
    }
}
