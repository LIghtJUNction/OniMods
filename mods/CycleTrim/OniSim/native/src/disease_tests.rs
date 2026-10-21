use super::*;
use crate::world::Cells;

fn table() -> DiseaseTable {
    let mut bytes = Vec::new();
    bytes.extend_from_slice(&2_i32.to_le_bytes());
    bytes.extend_from_slice(&1_i32.to_le_bytes());
    for id in 0..2_i32 {
        bytes.extend_from_slice(&0_i32.to_le_bytes());
        let mut header = [0_u8; 76];
        header[..4].copy_from_slice(&id.to_le_bytes());
        for (index, value) in [
            1.0_f32, 100.0, 200.0, 400.0, 500.0, 1.0, 2.0, 3.0, 4.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
            0.0, 0.0, 0.25,
        ]
        .into_iter()
        .enumerate()
        {
            header[4 + index * 4..8 + index * 4].copy_from_slice(&value.to_le_bytes());
        }
        bytes.extend_from_slice(&header);
        for value in [2.0_f32, 10.0, -2.0, 1.0, 0.5, 1000.0] {
            bytes.extend_from_slice(&value.to_le_bytes());
        }
        bytes.extend_from_slice(&1_i32.to_le_bytes());
        bytes.push(0);
    }
    DiseaseTable::parse(&bytes, 1).unwrap()
}

fn fixture(length: usize) -> Cells {
    let mut cells = Cells::new(length).unwrap_or_else(|error| panic!("{}", error.message));
    for cell in 0..length {
        cells.element[cell] = 0;
        cells.disease_idx[cell] = [0, 1, 255][cell % 3];
        cells.disease_count[cell] = [0, 1, -10, 100, i32::MAX, i32::MIN][cell % 6];
        cells.disease_infestation[cell] = (cell % 256) as u8;
        cells.disease_growth[cell] = [
            0.0,
            0.25,
            -0.9,
            f32::from_bits(1),
            f32::INFINITY,
            f32::from_bits(0x7fc12345),
        ][cell % 6];
        cells.temperature[cell] = [0.0, 150.0, 300.0, 450.0, 1000.0][cell % 5];
        cells.mass[cell] = (cell % 11) as f32;
        cells.radiation[cell] = (cell % 17) as f32;
    }
    cells
}

fn equal(actual: &Cells, expected: &Cells) {
    assert_eq!(actual.disease_idx, expected.disease_idx);
    assert_eq!(actual.disease_count, expected.disease_count);
    assert_eq!(actual.disease_infestation, expected.disease_infestation);
    for (left, right) in [
        (&actual.disease_growth, &expected.disease_growth),
        (&actual.temperature, &expected.temperature),
        (&actual.mass, &expected.mass),
        (&actual.radiation, &expected.radiation),
    ] {
        assert!(
            left.iter()
                .zip(right)
                .all(|(a, b)| a.to_bits() == b.to_bits())
        );
    }
    assert_eq!(actual.element, expected.element);
    assert_eq!(actual.properties, expected.properties);
    assert_eq!(actual.insulation, expected.insulation);
    assert_eq!(actual.strength, expected.strength);
}

#[test]
fn parallel_growth_matches_serial_bits_with_partial_rows_and_repeated_updates() {
    let diseases = table();
    let stride = 259;
    let height = 137;
    for workers in [1, 2, 4] {
        let pool = rayon::ThreadPoolBuilder::new()
            .num_threads(workers)
            .build()
            .unwrap();
        for radiation in [false, true] {
            for bounds in [
                (0, stride, 0, height),
                (3, 251, 2, 131),
                (1, 2, 1, height),
                (0, 0, 0, height),
                (0, stride, 5, 5),
            ] {
                let mut serial = fixture(stride * height);
                let mut parallel = serial.clone();
                for _ in 0..4 {
                    post_process_cells(
                        &mut serial,
                        &diseases,
                        stride,
                        height,
                        radiation,
                        bounds,
                        None,
                    )
                    .unwrap_or_else(|error| panic!("{}", error.message));
                    post_process_cells(
                        &mut parallel,
                        &diseases,
                        stride,
                        height,
                        radiation,
                        bounds,
                        Some(&pool),
                    )
                    .unwrap_or_else(|error| panic!("{}", error.message));
                    equal(&parallel, &serial);
                }
            }
        }
    }
}

#[test]
fn parallel_growth_rejects_bad_bounds_before_writing() {
    let mut cells = fixture(259 * 137);
    let original = cells.clone();
    let pool = rayon::ThreadPoolBuilder::new()
        .num_threads(2)
        .build()
        .unwrap();
    for bounds in [(0, 260, 0, 137), (3, 2, 0, 137), (0, 259, 0, 138)] {
        assert!(
            post_process_cells(&mut cells, &table(), 259, 137, false, bounds, Some(&pool)).is_err()
        );
        equal(&cells, &original);
    }
}
