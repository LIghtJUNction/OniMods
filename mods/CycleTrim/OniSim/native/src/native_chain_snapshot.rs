//! Test-only, pointer-independent snapshots for complete native batches.
//! Every float is copied by bits; map keys are sorted before serialization.

use crate::events::EventQueues;
use crate::frames_data::GameData;
use crate::grid::Grid;
use crate::world::{Cells, PhysicsWorld};

#[derive(Default)]
pub(crate) struct Image(pub(crate) Vec<u8>);

impl Image {
    pub(crate) fn bytes(&mut self, name: &str, bytes: &[u8]) {
        self.0.extend_from_slice(&(name.len() as u64).to_le_bytes());
        self.0.extend_from_slice(name.as_bytes());
        self.0.extend_from_slice(&(bytes.len() as u64).to_le_bytes());
        self.0.extend_from_slice(bytes);
    }

    fn slice<T: Copy>(&mut self, name: &str, values: &[T]) {
        // All callers below use primitives, primitive arrays, or the explicitly
        // packed event/OMC records whose complete byte layout is initialized.
        // No Rust owner, pointer, enum, or implicitly padded struct is serialized.
        let bytes = unsafe {
            std::slice::from_raw_parts(values.as_ptr().cast::<u8>(), std::mem::size_of_val(values))
        };
        self.bytes(name, bytes);
    }
}

fn cells(image: &mut Image, label: &str, cells: &Cells) {
    image.bytes("cells", label.as_bytes());
    macro_rules! fields {
        ($($field:ident),* $(,)?) => { $(image.slice(stringify!($field), &cells.$field);)* };
    }
    fields!(element, temperature, mass, properties, insulation, strength,
        disease_idx, disease_count, disease_infestation, disease_growth, radiation);
}

pub(crate) fn world(world: &PhysicsWorld) -> Image {
    let mut image = Image::default();
    cells(&mut image, "read", &world.read);
    cells(&mut image, "write", &world.write);
    macro_rules! slices {
        ($($field:ident),* $(,)?) => { $(image.slice(stringify!($field), &world.$field);)* };
    }
    slices!(backwall_element, backwall_mass, backwall_temperature, flow,
        accumulated_flow, radiation_transmission, dirty, dirty_flags, world_offsets,
        active_mask, mixture_element, mixture_mass, species_mass,
        gas_channel_of_element, gas_element_of_channel, element_phase);
    macro_rules! scalars {
        ($($field:ident),* $(,)?) => { $(image.slice(stringify!($field), &[world.$field]);)* };
    }
    scalars!(width, height, stride, padded_height, rng, scan_direction,
        gas_scan_direction, sink_element, vacuum_element, boundary_element,
        tick_seed, simulate_all_active, falling_disabled, radiation_enabled,
        saved_options, legacy_thermal_boundaries, building_heat_transfer_scale,
        building_contact_heat_transfer_scale, gas_channel_count);
    image.slice("inactive_present", &[world.inactive_mask.is_some()]);
    if let Some(mask) = &world.inactive_mask {
        image.slice("inactive_mask", mask);
    }
    for region in &world.regions {
        image.slice("region_bounds", &[region.min_x, region.max_x, region.min_y, region.max_y]);
        image.slice("region_light", &[region.sunlight, region.cosmic_radiation]);
    }
    let params = world.radiation_params;
    image.slice("radiation_params", &[params.dose_scale, params.mass_scale,
        params.absorption_base, params.absorption_mass_scale, params.absorption_solid]);
    let mut keys: Vec<_> = world.extra_mixtures.keys().copied().collect();
    keys.sort_unstable();
    for key in keys {
        image.slice("extra_cell", &[key]);
        for &(element, mass) in &world.extra_mixtures[&key] {
            image.slice("extra_element", &[element]);
            image.slice("extra_mass", &[mass]);
        }
    }
    image
}

pub(crate) fn events(events: &EventQueues) -> Image {
    let mut image = Image::default();
    macro_rules! fields {
        ($($field:ident),* $(,)?) => { $(image.slice(stringify!($field), &events.$field);)* };
    }
    fields!(solid_info, liquid_change_info, solid_substance_change_info,
        substance_change_info, callback_info, spawn_falling_liquid_info, dig_info,
        spawn_ore_info, spawn_fx_info, unstable_cell_info, world_damage_info,
        building_temperatures, mass_consumed_callbacks, mass_emitted_callbacks,
        disease_consumption_callbacks, component_state_changed_messages,
        removed_mass_entries, emitted_mass_entries, element_chunk_infos,
        element_chunk_melted_infos, building_overheat_infos,
        building_no_longer_overheated_infos, building_melted_infos,
        cell_melted_infos, backwall_element_changed_infos,
        backwall_should_transition_infos, disease_emitted_infos,
        disease_consumed_infos, radiation_consumed_callbacks);
    image
}

pub(crate) fn grid(grid: &Grid) -> Image {
    let mut image = Image::default();
    let mut saved = vec![0; grid.image_len()];
    grid.write_image(&mut saved);
    image.bytes("grid_image", &saved);
    image.slice("radiation", &grid.radiation);
    image.slice("accumulated_flow", &grid.accumulated_flow);
    // Only frame count is scalar descriptor data; all remaining descriptor
    // words are pointers or event spans encoded independently below.
    image.bytes("frames", unsafe { std::slice::from_raw_parts(grid.descriptor(), 4) });
    image
}

pub(crate) fn publication(grid: &Grid, data: &GameData, generation: u64) -> Image {
    let mut image = self::grid(grid);
    image.bytes("events", &events(&data.events).0);
    let descriptor = unsafe {
        std::slice::from_raw_parts(grid.descriptor(), crate::grid::DESCRIPTOR_SIZE)
    };
    for (slot, size) in [8, 4, 4, 8, 4, 20, 20, 20, 12, 20, 8, 8, 20, 20,
        12, 8, 20, 16, 8, 4, 4, 4, 4, 4, 4, 4, 8, 8, 12].into_iter().enumerate()
    {
        let at = 100 + slot * 12;
        let count = i32::from_le_bytes(descriptor[at..at + 4].try_into().unwrap());
        assert!(count >= 0);
        let address = u64::from_le_bytes(descriptor[at + 4..at + 12].try_into().unwrap());
        let bytes = if count == 0 { &[][..] } else {
            assert_ne!(address, 0);
            unsafe { std::slice::from_raw_parts(address as *const u8, count as usize * size) }
        };
        image.bytes(&format!("published_event_span_{slot}"), bytes);
    }
    for (index, (pointer, width)) in data.presentation.pointers().into_iter()
        .zip([8, 4, 4, 4, 1]).enumerate()
    {
        let at = 456 + index * 8;
        assert_eq!(u64::from_le_bytes(descriptor[at..at + 8].try_into().unwrap()), pointer as u64);
        // The ready lease owns these initialized fixed-size arrays for the
        // whole observation; no worker may reuse them until it is returned.
        image.bytes(&format!("texture_{index}"), unsafe {
            std::slice::from_raw_parts(pointer as *const u8, grid.len() * width)
        });
    }
    let view = data.multicomponent.descriptor(generation)
        .unwrap_or_else(|error| panic!("{}", error.message));
    image.slice("omc_scalars", &[view.version, view.flags, view.cell_count, view.total_species_records]);
    image.slice("omc_generation", &[view.generation]);
    image.slice("omc_cells", unsafe {
        std::slice::from_raw_parts(view.cells, view.cell_count as usize)
    });
    image.slice("omc_species", unsafe {
        std::slice::from_raw_parts(view.species, view.total_species_records as usize)
    });
    image.slice("visible", &data.visible);
    image
}

pub(crate) fn assert_equal(label: &str, a: &Image, b: &Image) {
    assert_eq!(a.0.len(), b.0.len(), "{label}: serialized lengths differ");
    if let Some(index) = a.0.iter().zip(&b.0).position(|(a, b)| a != b) {
        panic!("{label}: first byte difference at {index}: {} != {}", a.0[index], b.0[index]);
    }
}
