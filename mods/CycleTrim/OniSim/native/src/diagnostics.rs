//! Official Sim.DLLReportMessageMessage, pack=4, on the pinned managed profile.
//! Message id 1 is a fatal native report, not a general-purpose log callback.
use std::ffi::{CString, c_char, c_void};

use crate::state::Callback;
use crate::status::{Error, Result};

#[repr(C, packed(4))]
struct Report {
    callstack: *const c_char,
    message: *const c_char,
    file: *const c_char,
    line: i32,
}

/// Explicit diagnostic export, not a normal simulation-error path. The pinned
/// Linux AssertFunc raises SIGINT; if its handler returns, DebugCrash reports
/// one fatal message. Release every native lock before either callback path.
pub(crate) fn debug_crash(callback: Callback) {
    unsafe extern "C" {
        fn raise(signal: i32) -> i32;
    }
    eprintln!("Assert failure 'BREAKPT:' at SimBindings.cpp(434)\n");
    unsafe {
        raise(2);
    }
    if let Some(callback) = callback {
        let report = Report {
            callstack: std::ptr::null(),
            message: c"Assert failed: false".as_ptr(),
            file: c"SimBindings.cpp".as_ptr(),
            line: 434,
        };
        callback(1, (&report as *const Report).cast::<c_void>());
    }
}

fn invoke(callback: Callback, operation: &str, error: &Error) {
    if let Some(callback) = callback {
        let text = format!(
            "OniSim {operation} failed (status {}): {}",
            error.status as i32, error.message
        )
        .replace('\0', " ");
        let message = CString::new(text).expect("embedded zeros replaced");
        let report = Report {
            callstack: std::ptr::null(),
            message: message.as_ptr(),
            file: c"OniSim".as_ptr(),
            line: 0,
        };
        callback(1, (&report as *const Report).cast::<c_void>());
    }
}

pub(crate) fn official<T>(
    failure: T,
    operation: impl FnOnce() -> String,
    work: impl FnOnce() -> Result<T>,
) -> T {
    crate::status::boundary(failure, || {
        work().inspect_err(|error| {
            // The operation has already dropped all State/Engine locks. The managed
            // crash reporter may reenter native accessors; never call it under a lock.
            let callback = crate::state::lock().ok().and_then(|mut state| {
                if state.diagnostic_reported {
                    return None;
                }
                let callback = state.callback;
                if callback.is_some() {
                    state.diagnostic_reported = true;
                }
                callback
            });
            if callback.is_some() {
                invoke(callback, &operation(), error);
            }
        })
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::status::Status;
    use std::ffi::CStr;

    extern "C" fn receiver(id: i32, pointer: *const c_void) -> i32 {
        assert_eq!(id, 1);
        let bytes = pointer.cast::<u8>();
        unsafe {
            let message = std::ptr::read_unaligned(bytes.add(8).cast::<*const c_char>());
            let file = std::ptr::read_unaligned(bytes.add(16).cast::<*const c_char>());
            assert_eq!(
                CStr::from_ptr(message).to_str().unwrap(),
                "OniSim message 123 failed (status 2): unknown message"
            );
            assert_eq!(CStr::from_ptr(file).to_bytes(), b"OniSim");
            assert_eq!(std::ptr::read_unaligned(bytes.add(24).cast::<i32>()), 0);
        }
        0
    }

    #[test]
    fn report_matches_official_pack4_callback_layout() {
        assert_eq!(std::mem::size_of::<Report>(), 28);
        invoke(
            Some(receiver),
            "message 123",
            &Error::new(Status::Unsupported, "unknown message"),
        );
    }
}
