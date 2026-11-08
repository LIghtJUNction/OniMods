//! Frozen pre-optimization coordinate solve for matched cost samples only.
//! This is compiled only in tests; it is not another production liquid path.
use super::*;

impl PressureScratch {
    pub(super) fn solve_coordinate_reference(&self, i: usize) -> f64 {
        let node = &self.nodes[i];
        let mut budget = 0.0;
        for &j in &node.neighbors {
            if j != NONE {
                budget += node.viscosity.min(self.nodes[j].viscosity);
            }
        }
        if budget == 0.0 {
            return node.head;
        }
        let mut low = head((node.mass - budget).max(0.0), node.capacity, node.elevation);
        if node.mass <= budget {
            for &j in &node.neighbors {
                if j != NONE {
                    let other = &self.nodes[j];
                    low = low.min(
                        other.head
                            - node.viscosity.min(other.viscosity)
                                / node.capacity.min(other.capacity),
                    );
                }
            }
        }
        let mut high = head(node.mass + budget, node.capacity, node.elevation);
        let mut value = node.head.clamp(low, high);
        for _ in 0..12 {
            let (mass, mut slope) = storage(value, node.capacity, node.elevation);
            let mut residual = mass - node.mass;
            for &j in &node.neighbors {
                if j == NONE {
                    continue;
                }
                let other = &self.nodes[j];
                let conductance = node.capacity.min(other.capacity);
                let limit = node.viscosity.min(other.viscosity);
                let flux = conductance * (value - other.head);
                residual += flux.clamp(-limit, limit);
                if flux.abs() < limit {
                    slope += conductance;
                }
            }
            if residual.abs() < node.capacity * 1.0e-10 {
                break;
            }
            if residual > 0.0 {
                high = value;
            } else {
                low = value;
            }
            let newton = value - residual / slope;
            value = if newton > low && newton < high {
                newton
            } else {
                (low + high) * 0.5
            };
        }
        value
    }
}
