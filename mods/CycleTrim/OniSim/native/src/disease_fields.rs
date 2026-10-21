//! One-cell disease mutation, shared by serial and disjoint parallel writes.
use crate::fluid_helpers::calculate_disease;
use crate::tables::DiseaseTable;

#[inline]
pub(crate) fn add_disease_fields(
    tables: &DiseaseTable,
    current_id: &mut u8,
    current_count: &mut i32,
    infestation: &mut u8,
    growth: &mut f32,
    disease: u8,
    count: i32,
) {
    let old_id = *current_id;
    let (id, count) = calculate_disease(tables, old_id, *current_count, disease, count);
    *current_id = id;
    *current_count = count;
    if count <= 0 {
        *current_id = 255;
        *current_count = 0;
        *infestation = 0;
        *growth = 0.0;
    } else if id != old_id {
        *infestation = 0;
    }
}
