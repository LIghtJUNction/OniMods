use super::*;
use std::{hint::black_box, time::Instant};

fn fixture(width: usize, height: usize) -> (PresentationBuffers, PhysicsWorld, ElementTable) {
    let mut bytes = 3_i32.to_le_bytes().to_vec();
    for (index, hash) in [0xa9360b34_u32, 0x2d39bf75, 0x6d95058c]
        .into_iter()
        .enumerate()
    {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0_u8; 12]);
    let table = ElementTable::parse(&bytes).unwrap();
    let mut world = PhysicsWorld::new(width, height, 7, &table).unwrap();
    for (i, element) in world.write.element.iter_mut().enumerate() {
        *element = (i % table.len()) as u16;
    }
    world
        .world_offsets
        .push([0, 0, width as i32, height as i32]);
    (
        PresentationBuffers::new(width * height).unwrap(),
        world,
        table,
    )
}

// Frozen validator from caf5599. Benchmark the complete production validation
// boundary, including properties, regions and metadata, not just the SIMD loop.
fn original(
    buffers: &PresentationBuffers,
    world: &PhysicsWorld,
    elements: &ElementTable,
) -> Result<()> {
    if world.width.checked_mul(world.height) != Some(buffers.flow.len())
        || world
            .write
            .element
            .iter()
            .any(|&element| element as usize >= elements.len())
        || world.world_offsets.iter().any(|&[x, y, width, height]| {
            let top = y as i64 + height as i64 - 1;
            width > 0
                && (x < 0
                    || y < 0
                    || x as i64 + width as i64 > world.width as i64
                    || top < 0
                    || top >= world.height as i64)
        })
    {
        return Err(invalid(
            "texture images no longer match the initialized world",
        ));
    }
    for element in &elements.elements {
        let record = &element.record;
        if record.gradient_count as usize > record.gradient.len()
            || [record.low_transition, record.high_transition]
                .into_iter()
                .any(|index| index != u16::MAX && index as usize >= elements.len())
        {
            return Err(invalid(
                "texture element gradient or transition index is invalid",
            ));
        }
    }
    if world
        .write
        .properties
        .iter()
        .any(|&flags| flags & 0x30 == 0x30)
        && !elements
            .elements
            .iter()
            .any(|element| element.record.id as u32 == sun::OVERRIDE_HASH)
    {
        return Err(invalid("sunlight material override element is missing"));
    }
    Ok(())
}

fn outcome(result: Result<()>) -> std::result::Result<(), (i32, String)> {
    result.map_err(|error| (error.status as i32, error.message))
}

fn compare(buffers: &PresentationBuffers, world: &PhysicsWorld, elements: &ElementTable) {
    assert_eq!(
        outcome(original(buffers, world, elements)),
        outcome(buffers.validate(world, elements))
    );
}

#[test]
fn validation_matches_scalar_for_padded_cells_vector_tails_and_repeated_changes() {
    for (width, height) in [(1, 1), (2, 2), (3, 5), (17, 19), (96, 96)] {
        let (buffers, mut world, elements) = fixture(width, height);
        compare(&buffers, &world, &elements);
        let count = world.write.element.len();
        // Every position for small grids; block boundaries and both ends for
        // larger grids also catch an invalid padded cell or a scalar tail.
        let positions: Vec<_> = if count < 400 {
            (0..count).collect()
        } else {
            vec![0, 1, 7, 8, 15, 16, count / 2, count - 2, count - 1]
        };
        for cell in positions {
            let previous = world.write.element[cell];
            for invalid in [elements.len() as u16, 32768, u16::MAX] {
                world.write.element[cell] = invalid;
                compare(&buffers, &world, &elements);
                assert!(buffers.validate(&world, &elements).is_err());
            }
            world.write.element[cell] = previous;
            assert!(buffers.validate(&world, &elements).is_ok());
        }
    }
}

#[test]
fn validation_preserves_other_errors_and_their_precedence() {
    let (buffers, mut world, mut elements) = fixture(3, 5);
    world.write.properties[0] = 0x30;
    compare(&buffers, &world, &elements);
    elements.elements[0].record.gradient_count = u8::MAX;
    compare(&buffers, &world, &elements);
    world.write.element[0] = u16::MAX;
    compare(&buffers, &world, &elements);
    world.write.element[0] = 0;
    elements.elements[0].record.gradient_count = 0;
    elements.elements[0].record.low_transition = elements.len() as u16;
    compare(&buffers, &world, &elements);
    elements.elements[0].record.low_transition = u16::MAX;
    elements.elements[0].record.high_transition = u16::MAX;
    world.write.properties[0] = 0;
    for offsets in [
        [-1, 0, 3, 5],
        [0, 0, 4, 5],
        [0, 0, 3, 6],
        [0, 0, 0, 0],
        [0, 1, 3, 0],
    ] {
        world.world_offsets[0] = offsets;
        compare(&buffers, &world, &elements);
    }
    world.width += 1;
    compare(&buffers, &world, &elements);
}

#[test]
#[ignore = "manual complete presentation-validation benchmark; run after correctness tests"]
fn benchmark_validation() {
    const WARMUP: usize = 3;
    const ROUNDS: usize = 17;
    type Validator = fn(&PresentationBuffers, &PhysicsWorld, &ElementTable) -> Result<()>;
    let validators: [Validator; 2] = [original, PresentationBuffers::validate];
    println!(
        "VALIDATION_HEADER,width,height,baseline_median_us,candidate_median_us,baseline_min_us,baseline_max_us,candidate_min_us,candidate_max_us,baseline_samples_us,candidate_samples_us"
    );
    for (width, height) in [(1, 1), (3, 5), (17, 19), (96, 96), (256, 384), (736, 304)] {
        let (buffers, world, elements) = fixture(width, height);
        compare(&buffers, &world, &elements);
        let repeats = (20_000_000 / world.write.element.len()).clamp(8, 250_000);
        let mut samples: [Vec<f64>; 2] = std::array::from_fn(|_| Vec::new());
        for round in 0..WARMUP + ROUNDS {
            // Alternating ABBA / BAAB retains equal sample counts while
            // balancing both monotonic drift and first-in-pair effects.
            for kind in [0, 1, 1, 0].map(|kind| kind ^ (round & 1)) {
                let validate = black_box(validators[kind]);
                let start = Instant::now();
                for _ in 0..repeats {
                    black_box(validate(
                        black_box(&buffers),
                        black_box(&world),
                        black_box(&elements),
                    ))
                    .unwrap();
                }
                if round >= WARMUP {
                    samples[kind].push(start.elapsed().as_secs_f64() * 1e6 / repeats as f64);
                }
            }
        }
        let sorted = samples.clone().map(|mut values| {
            values.sort_by(f64::total_cmp);
            values
        });
        println!(
            "VALIDATION,{width},{height},{:.6},{:.6},{:.6},{:.6},{:.6},{:.6},{:?},{:?}",
            (sorted[0][ROUNDS - 1] + sorted[0][ROUNDS]) * 0.5,
            (sorted[1][ROUNDS - 1] + sorted[1][ROUNDS]) * 0.5,
            sorted[0][0],
            sorted[0][2 * ROUNDS - 1],
            sorted[1][0],
            sorted[1][2 * ROUNDS - 1],
            samples[0],
            samples[1]
        );
    }
}
