// Independently declared from the official managed Pack=4 output records.
// No record contains a managed object, allocation owner or native pointer.
macro_rules! record {
    ($name:ident, $size:expr, { $($field:ident: $kind:ty),* $(,)? }) => {
        #[derive(Clone, Copy, Debug, Default)]
        #[repr(C, packed(4))]
        pub(crate) struct $name { $(pub(crate) $field: $kind),* }
        const _: [(); $size] = [(); std::mem::size_of::<$name>()];
    };
}

record!(SolidInfo, 8, { cell_idx: i32, is_solid: i32 });
record!(LiquidChangeInfo, 4, { cell_idx: i32 });
record!(SolidSubstanceChangeInfo, 4, { cell_idx: i32 });
record!(SubstanceChangeInfo, 8, {
    cell_idx: i32, old_elem_idx: u16, new_elem_idx: u16,
});
record!(CallbackInfo, 4, { callback_idx: i32 });
record!(SpawnFallingLiquidInfo, 20, {
    cell_idx: i32, elem_idx: u16, disease_idx: u8, pad0: u8,
    mass: f32, temperature: f32, disease_count: i32,
});
record!(SpawnOreInfo, 20, {
    cell_idx: i32, elem_idx: u16, disease_idx: u8, pad0: u8,
    mass: f32, temperature: f32, disease_count: i32,
});
record!(SpawnFXInfo, 12, { cell_idx: i32, fx_hash: i32, rotation: f32 });
record!(UnstableCellInfo, 20, {
    cell_idx: i32, elem_idx: u16, falling_info: u8, disease_idx: u8,
    mass: f32, temperature: f32, disease_count: i32,
});
record!(WorldDamageInfo, 8, { game_cell: i32, damage_source_offset: i32 });
record!(BuildingTemperatureInfo, 8, { handle: i32, temperature: f32 });
record!(MassConsumedCallback, 20, {
    callback_idx: i32, elem_idx: u16, disease_idx: u8, pad0: u8,
    mass: f32, temperature: f32, disease_count: i32,
});
record!(MassEmittedCallback, 20, {
    callback_idx: i32, elem_idx: u16, succeeded: u8, disease_idx: u8,
    mass: f32, temperature: f32, disease_count: i32,
});
record!(DiseaseConsumptionCallback, 12, {
    callback_idx: i32, disease_idx: u8, pad0: u8, pad1: u8, pad2: u8,
    disease_count: i32,
});
record!(ComponentStateChangedMessage, 8, { callback_idx: i32, sim_handle: i32 });
record!(ConsumedMassInfo, 20, {
    sim_handle: i32, removed_elem_idx: u16, disease_idx: u8, pad0: u8,
    mass: f32, temperature: f32, disease_count: i32,
});
record!(EmittedMassInfo, 16, {
    elem_idx: u16, disease_idx: u8, pad0: u8,
    mass: f32, temperature: f32, disease_count: i32,
});
record!(ElementChunkInfo, 8, { temperature: f32, delta_kj: f32 });
record!(MeltedInfo, 4, { handle: i32 });
record!(CellMeltedInfo, 4, { game_cell: i32 });
record!(BackwallElementChangedInfo, 4, { game_cell: i32 });
record!(BackwallShouldTransitionInfo, 4, { game_cell: i32 });
record!(DiseaseEmittedInfo, 8, {
    disease_idx: u8, pad0: u8, pad1: u8, pad2: u8, count: i32,
});
record!(DiseaseConsumedInfo, 8, {
    disease_idx: u8, pad0: u8, pad1: u8, pad2: u8, count: i32,
});
record!(ConsumedRadiationCallback, 12, {
    callback_idx: i32, game_cell: i32, radiation: f32,
});
