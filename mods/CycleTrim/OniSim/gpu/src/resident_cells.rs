//! Shared structure-of-arrays buffers for chaining simulation operators.
use std::sync::Arc;
use crate::gpu_context::GpuContext;

/// All buffers use 32-bit lanes for direct WGSL access. Indices and compact
/// byte fields are widened at the boundary until their producers move to GPU.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
#[repr(usize)]
pub enum CellField {
    Element, Mass, Temperature, Properties, Insulation, Strength,
    Disease, DiseaseCount, DiseaseInfestation, DiseaseGrowth, Radiation,
    MixtureElement, MixtureMass, WallElement, WallMass, WallTemperature,
    FlowLeft, FlowRight, FlowDown, FlowUp,
}
const FIELD_COUNT: usize = 20;

/// A buffer becomes device-owned after a GPU pass writes it. A caller must
/// explicitly acknowledge a readback before reusing a CPU snapshot as input.
pub struct ResidentCells {
    context: Arc<GpuContext>,
    buffers: Vec<wgpu::Buffer>,
    cells: usize,
    device_owned: [bool; FIELD_COUNT],
}

impl ResidentCells {
    pub fn new(context: Arc<GpuContext>, cells: usize) -> Result<Self, String> {
        let bytes = cells.checked_mul(4).ok_or("resident grid size overflow")?;
        if bytes == 0 || bytes as u64 > context.device.limits().max_storage_buffer_binding_size as u64 {
            return Err("resident grid exceeds device storage limit".into());
        }
        let buffers = (0..FIELD_COUNT).map(|_| context.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("OniSim resident cell field"), size: bytes as u64,
            usage: wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::COPY_SRC,
            mapped_at_creation: false,
        })).collect();
        Ok(Self { context, buffers, cells, device_owned: [false; FIELD_COUNT] })
    }

    pub fn cells(&self) -> usize { self.cells }
    pub fn buffer(&self, field: CellField) -> &wgpu::Buffer { &self.buffers[field as usize] }
    pub fn is_device_owned(&self, field: CellField) -> bool { self.device_owned[field as usize] }

    /// Upload full 32-bit lanes. Reject stale host data after a GPU write.
    pub fn upload(&mut self, field: CellField, data: &[u32]) -> Result<(), String> {
        if data.len() != self.cells { return Err("resident upload length mismatch".into()); }
        if self.is_device_owned(field) { return Err("resident field has unsynchronized GPU writes".into()); }
        self.context.queue.write_buffer(self.buffer(field), 0, bytemuck::cast_slice(data));
        Ok(())
    }

    /// Record ownership after encoding a pass which writes a field.
    pub fn mark_device_written(&mut self, field: CellField) { self.device_owned[field as usize] = true; }

    /// Copy a GPU-owned field to a caller-owned MAP_READ buffer. The caller
    /// must wait for its submission and then apply the bytes to the host state.
    pub fn encode_readback(&self, encoder: &mut wgpu::CommandEncoder, field: CellField,
        destination: &wgpu::Buffer) -> Result<(), String> {
        let bytes = (self.cells * 4) as u64;
        if destination.size() < bytes || !destination.usage().contains(wgpu::BufferUsages::COPY_DST) {
            return Err("resident readback destination is invalid".into());
        }
        encoder.copy_buffer_to_buffer(self.buffer(field), 0, destination, 0, bytes);
        Ok(())
    }

    /// Only call after a completed readback has been applied to the host.
    pub fn acknowledge_readback(&mut self, field: CellField) { self.device_owned[field as usize] = false; }
}
