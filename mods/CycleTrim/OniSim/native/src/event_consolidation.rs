//! Step-tail ore event reduction observed at official ELF 0x58600.
use crate::events::EventQueues;

impl EventQueues {
    /// Consolidate the unpublished Step batch, before exposing its descriptor.
    /// Dig events are a separate native vector and must not enter this reduction.
    pub(crate) fn consolidate_spawn_ore(&mut self) {
        let entries = &mut self.spawn_ore_info;
        if entries.is_empty() {
            return;
        }
        crate::event_sort::sort_spawn_ore(entries);
        let mut output = 0;
        for input in 1..entries.len() {
            let next = entries[input];
            let mut current = entries[output];
            if current.cell_idx == next.cell_idx && current.elem_idx == next.elem_idx {
                let total = next.mass + current.mass;
                current.temperature = mixed_temperature(
                    current.mass,
                    current.temperature,
                    next.mass,
                    next.temperature,
                    total,
                );
                current.mass = total;
                // The native reduction changes mass and temperature only. In
                // particular it does not add disease counts or select a new id.
                entries[output] = current;
            } else {
                output += 1;
                entries[output] = next;
            }
        }
        entries.truncate(output + 1);
    }
}

fn mixed_temperature(ma: f32, ta: f32, mb: f32, tb: f32, total: f32) -> f32 {
    if total <= 0.0 {
        return 0.0;
    }
    let average = (ma * ta + mb * tb) / total;
    // Preserve the native ordered comparisons, including unordered float cases.
    if ta <= tb {
        let upper = if average <= tb { average } else { tb };
        if ta <= average { upper } else { ta }
    } else if tb <= average {
        if average <= ta { average } else { ta }
    } else {
        tb
    }
}
