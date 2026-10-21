//! Sim::Main (official ELF 0x5a100) enables only the MXCSR FTZ bit on its
//! simulation thread. Preserve all inherited rounding, exception, and DAZ bits.

use crate::status::Result;
#[cfg(not(target_arch = "x86_64"))]
use crate::status::{Error, Status};

pub(crate) struct FloatEnvGuard {
    #[cfg(target_arch = "x86_64")]
    saved: u32,
}

/// Opaque snapshot: worker tasks inherit the submitting thread's MXCSR,
/// including rounding and DAZ, rather than the pool creator's old settings.
#[derive(Clone, Copy)]
pub(crate) struct FloatEnv(u32);

impl FloatEnv {
    pub(crate) fn capture() -> Self {
        let mut value = 0_u32;
        #[cfg(target_arch = "x86_64")]
        unsafe {
            std::arch::asm!("stmxcsr [{pointer}]", pointer = in(reg) &mut value,
                options(nostack, preserves_flags));
        }
        Self(value)
    }

    /// The explicit register input makes the producing f32 arithmetic complete
    /// before its exception flags are read; a memory clobber alone is not an
    /// ordering dependency for independent register-only floating operations.
    pub(crate) fn capture_after(completed: f32) -> Self {
        let mut value = 0_u32;
        #[cfg(target_arch = "x86_64")]
        unsafe {
            std::arch::asm!(
                "/* completed {completed} */",
                "stmxcsr [{pointer}]",
                completed = in(xmm_reg) completed,
                pointer = in(reg) &mut value,
                options(nostack, preserves_flags),
            );
        }
        #[cfg(not(target_arch = "x86_64"))]
        let _ = completed;
        Self(value)
    }

    pub(crate) fn masks_all_exceptions(self) -> bool {
        self.0 & 0x1f80 == 0x1f80
    }

    /// A skipped bounded calculation must retain its previous sticky effects
    /// and every rounding/DAZ/FTZ/mask control. Never suppress unmasked traps.
    pub(crate) fn retains_arithmetic_effects(self, after: Self) -> bool {
        self.masks_all_exceptions()
            && self.0 & !0x3f == after.0 & !0x3f
            && (self.0 & after.0 & 0x3f) == after.0 & 0x3f
    }

    pub(crate) fn install(self) -> FloatEnvGuard {
        let saved = Self::capture().0;
        #[cfg(target_arch = "x86_64")]
        unsafe {
            std::arch::asm!("ldmxcsr [{pointer}]", pointer = in(reg) &self.0,
                options(nostack, preserves_flags));
        }
        FloatEnvGuard { saved }
    }
}

impl FloatEnvGuard {
    pub(crate) fn enter() -> Result<Self> {
        #[cfg(target_arch = "x86_64")]
        {
            let mut saved = 0_u32;
            unsafe {
                std::arch::asm!("stmxcsr [{pointer}]", pointer = in(reg) &mut saved,
                    options(nostack, preserves_flags));
                let enabled = saved | 0x8000;
                std::arch::asm!("ldmxcsr [{pointer}]", pointer = in(reg) &enabled,
                    options(nostack, preserves_flags));
            }
            Ok(Self { saved })
        }
        #[cfg(not(target_arch = "x86_64"))]
        {
            Err(Error::new(
                Status::Unsupported,
                "official simulation floating-point environment requires x86_64",
            ))
        }
    }
}

impl Drop for FloatEnvGuard {
    fn drop(&mut self) {
        #[cfg(target_arch = "x86_64")]
        unsafe {
            std::arch::asm!("ldmxcsr [{pointer}]", pointer = in(reg) &self.saved,
                options(nostack, preserves_flags));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use rayon::prelude::*;

    #[test]
    fn tasks_inherit_submitter_rounding_and_restore_pool_environment() {
        let pool = rayon::ThreadPoolBuilder::new()
            .num_threads(2)
            .build()
            .unwrap();
        let before = pool.broadcast(|_| FloatEnv::capture().0);
        for mode in [0, 0x2000, 0x4000, 0x6000] {
            let original = FloatEnv::capture();
            let requested = FloatEnv((original.0 & !0x6000) | mode | 0x8000);
            let _caller = requested.install();
            let inherited = FloatEnv::capture();
            pool.install(|| {
                (0..32).into_par_iter().for_each(|_| {
                    let _task = inherited.install();
                    assert_eq!(FloatEnv::capture().0, inherited.0);
                });
            });
        }
        assert_eq!(pool.broadcast(|_| FloatEnv::capture().0), before);
    }
}
