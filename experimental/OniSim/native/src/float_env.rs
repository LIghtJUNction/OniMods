//! Sim::Main (official ELF 0x5a100) enables only the MXCSR FTZ bit on its
//! simulation thread. Preserve all inherited rounding, exception, and DAZ bits.

use crate::status::Result;
#[cfg(not(target_arch = "x86_64"))]
use crate::status::{Error, Status};

pub(crate) struct FloatEnvGuard {
    #[cfg(target_arch = "x86_64")]
    saved: u32,
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
