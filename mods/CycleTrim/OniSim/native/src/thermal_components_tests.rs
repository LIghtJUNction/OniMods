use super::*;

fn ok<T>(result: Result<T>) -> T {
    result.unwrap_or_else(|error| panic!("{}", error.message))
}

fn rejects(result: Result<()>, expected: Status) {
    let error = result.err().expect("command must be rejected");
    assert_eq!(error.status as i32, expected as i32);
}

fn fixture() -> (PhysicsWorld, ElementTable) {
    let mut bytes = 3_i32.to_le_bytes().to_vec();
    for (index, hash) in [0xa9360b34_u32, 0x2d39bf75, 0x6d95058c]
        .into_iter()
        .enumerate()
    {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[4..8].fill(255);
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = if index == 2 { 3 } else { 0 };
        record[12..16].copy_from_slice(&1_f32.to_le_bytes());
        record[60..64].copy_from_slice(&10_000_f32.to_le_bytes());
        record[84..88].fill(255);
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0; 12]);
    let table = ElementTable::parse(&bytes).unwrap();
    (ok(PhysicsWorld::new(4, 4, 0, &table)), table)
}

fn building(callback: i32, temperature: f32) -> [u8; 44] {
    let mut bytes = [0; 44];
    bytes[..4].copy_from_slice(&callback.to_le_bytes());
    bytes[4..6].copy_from_slice(&2_u16.to_le_bytes());
    bytes[8..12].copy_from_slice(&1_f32.to_le_bytes());
    bytes[12..16].copy_from_slice(&temperature.to_le_bytes());
    bytes[20..24].copy_from_slice(&1000_f32.to_le_bytes());
    bytes[36..40].copy_from_slice(&1_i32.to_le_bytes());
    bytes[40..44].copy_from_slice(&1_i32.to_le_bytes());
    bytes
}

fn energy(delta: f32) -> [u8; 16] {
    let mut bytes = [0; 16]; // building handle 0
    bytes[4..8].copy_from_slice(&delta.to_le_bytes());
    bytes[8..12].copy_from_slice(&80_f32.to_le_bytes());
    bytes[12..16].copy_from_slice(&250_f32.to_le_bytes());
    bytes
}

#[test]
fn every_thermal_payload_is_owned_and_wrong_sizes_leave_the_queue_unchanged() {
    let specs = [
        (ADD_BUILDING, 44),
        (MODIFY_BUILDING, 44),
        (BUILDING_ENERGY, 16),
        (REMOVE_BUILDING, 8),
        (ADD_CONTACTS, 8),
        (REMOVE_CONTACT, 8),
        (ADD_CONTACT, 12),
        (REMOVE_CONTACTS, 8),
        (ADD_CHUNK, 32),
        (MOVE_CHUNK, 8),
        (MODIFY_CHUNK, 12),
        (CHUNK_ENERGY, 8),
        (CHUNK_ADJUSTER, 16),
        (REMOVE_CHUNK, 8),
    ];
    let mut components = ThermalComponents::default();
    for (index, (id, size)) in specs.into_iter().enumerate() {
        let mut bytes: Vec<u8> = (0..size).map(|at| (index * 17 + at) as u8).collect();
        rejects(
            components.queue(id, &bytes[..size - 1]),
            Status::InvalidArgument,
        );
        rejects(components.queue(id, &[0; 45]), Status::InvalidArgument);
        assert_eq!(components.pending.len(), index);
        ok(components.queue(id, &bytes));
        bytes.fill(0);
    }
    rejects(components.queue(0, &[]), Status::InvalidArgument);
    let commands = components.take_pending();
    assert!(components.pending.is_empty());
    assert_eq!(commands.len(), specs.len());
    for (index, (command, (id, size))) in commands.0.iter().zip(specs).enumerate() {
        assert_eq!(command.id, id);
        let expected: Vec<u8> = (0..size).map(|at| (index * 17 + at) as u8).collect();
        assert_eq!(&command.bytes[..size], expected);
    }
}

#[test]
fn thermal_batch_replay_keeps_type_order_clamps_and_backwall_boundary() {
    let (world, table) = fixture();
    let mut components = ThermalComponents::default();
    let mut removal = [0; 8]; // chunk handle 0, created later in arrival order
    removal[4..8].copy_from_slice(&30_i32.to_le_bytes());
    ok(components.queue(REMOVE_CHUNK, &removal));
    ok(components.queue(BUILDING_ENERGY, &energy(200.0)));
    ok(components.queue(ADD_BUILDING, &building(10, 100.0)));
    let mut chunk = [0; 32];
    chunk[4..8].copy_from_slice(&20_i32.to_le_bytes());
    chunk[8..12].copy_from_slice(&1_f32.to_le_bytes());
    chunk[12..16].copy_from_slice(&300_f32.to_le_bytes());
    chunk[16..20].copy_from_slice(&1_f32.to_le_bytes());
    chunk[20..24].copy_from_slice(&1_f32.to_le_bytes());
    chunk[28..30].copy_from_slice(&2_u16.to_le_bytes());
    ok(components.queue(ADD_CHUNK, &chunk));

    let mut staged = ThermalComponents::default();
    let mut records = [energy(-100.0), energy(30.0)];
    ok(staged.queue_energy_batch(&records));
    records.fill([0; 16]);
    ok(components.append_pending(staged));
    let mut commands = components.take_pending();
    let mut events = Events::default();
    ok(components.process_before_backwall(&mut commands, &world, &table, &mut events));

    // Register first, then execute 100 + 200 -> 250 (clamped), -100 -> 150,
    // +30 -> 180. Reordering, aggregating, or dropping a batch changes the result.
    assert_eq!(
        ok(components.buildings.get(0)).temperature.to_bits(),
        180_f32.to_bits()
    );
    assert!(components.chunks.contains(0));
    assert_eq!(commands.len(), 1);
    assert_eq!(commands.0[0].id, REMOVE_CHUNK);
    let callbacks = |events: &Events| {
        events
            .component_state_changed_messages
            .iter()
            .map(|event| (event.callback_idx, event.sim_handle))
            .collect::<Vec<_>>()
    };
    assert_eq!(callbacks(&events), [(10, 0), (20, 0)]);
    ok(components.process_after_backwall(&mut commands, &world, &table, &mut events));
    assert!(commands.is_empty());
    assert!(!components.chunks.contains(0));
    assert_eq!(callbacks(&events), [(10, 0), (20, 0), (30, -1)]);
}

#[test]
fn building_replay_preserves_nonfinite_signed_zero_and_subnormal_wire_words() {
    let (world, table) = fixture();
    let mut components = ThermalComponents::default();
    let mut bytes = building(-1, f32::from_bits(0x7f80_1234));
    bytes[20..24].copy_from_slice(&0x8000_0000_u32.to_le_bytes());
    bytes[24..28].copy_from_slice(&1_u32.to_le_bytes());
    ok(components.queue(ADD_BUILDING, &bytes));
    bytes.fill(0);
    let commands = components.take_pending();
    let mut events = Events::default();
    ok(components.process_pending(commands, &world, &table, &mut events));
    let actual = ok(components.buildings.get(0));
    assert_eq!(actual.temperature.to_bits(), 0x7f80_1234);
    assert_eq!(actual.overheat_temperature.to_bits(), 0x8000_0000);
    assert_eq!(actual.operating_kw.to_bits(), 1);
    assert_eq!(actual.extents, [1, 1, 2, 2]);
    assert!(events.component_state_changed_messages.is_empty());
}

#[test]
fn thermal_energy_batch_limit_rejects_all_records_before_mutation() {
    let mut components = ThermalComponents::default();
    let record = energy(30.0);
    let limit = crate::frames::MAX_PENDING_COMMANDS;
    ok(components.queue_energy_batch(&vec![record; limit - 1]));
    rejects(
        components.queue_energy_batch(&[record, record]),
        Status::ResourceLimit,
    );
    assert_eq!(components.pending.len(), limit - 1);
    ok(components.queue_energy_batch(&[record]));
    ok(components.queue_energy_batch(&[]));
    rejects(
        components.queue_energy_batch(&[record]),
        Status::ResourceLimit,
    );
    rejects(
        components.queue(BUILDING_ENERGY, &record),
        Status::ResourceLimit,
    );
    rejects(
        components.queue(BUILDING_ENERGY, &[]),
        Status::InvalidArgument,
    );
    assert_eq!(components.pending.len(), limit);
    for command in &components.pending {
        assert_eq!(command.id, BUILDING_ENERGY);
        assert_eq!(&command.bytes[..16], &record);
    }
}
