use super::*;
use crate::numeric::{smax, smin};
use std::io::Write;

// ProcessFrame ELF 0x4aee1..0x4af6a, exceptional clamp path 0x4d4a8..0x4d4e9.
// The diagnostic record id is used only by the exceptional stderr output.
pub(super) fn modify(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    command: Energy,
) {
    let cell = command.cell;
    let element = &elements.elements[world.write.element[cell] as usize].record;
    if element.state & 3 == 0 {
        return;
    }
    let mass = world.write.mass[cell];
    let maximum = command.max_temperature;
    // These are native COMISS/JAE gates: NaNs pass both comparisons.
    if f32::from_bits(0x3a83126f) >= mass || 0.0 >= maximum {
        return;
    }
    let old = world.write.temperature[cell];
    let delta = command.kilojoules / (element.specific_heat_capacity * mass);
    let limit = smax(maximum, old);
    let mut temperature = smin(delta + old, limit);
    if mass > 0.0 {
        if temperature <= 0.0 || 10000.0 < temperature {
            let _ = writeln!(
                std::io::stderr().lock(),
                "INVALID TEMPERATURE FOR CELL ENERGY MODIFICATION:\nID({}), kj({:.6}), maxT({:.6}), cell({}), mass({:.6}), deg. chg({:.6}), temp({:.6})",
                command.diagnostic_id,
                command.kilojoules,
                maximum,
                cell,
                mass,
                delta,
                old,
            );
            temperature = smax(smin(temperature, 10000.0), 1.0);
            if !(temperature > 0.0) {
                return;
            }
        }
    } else if !(temperature > 0.0) {
        return;
    }
    world.write.temperature[cell] = temperature;
    // Unlike the later temperature task, this dispatcher passes the element
    // table entry selected from active storage before applying this command.
    crate::transition::do_state_transition_from_active(world, elements, events, cell);
}
