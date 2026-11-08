use std::sync::Arc;

pub(crate) fn new_vulkan_instance() -> Result<wgpu::Instance, String> {
    let enabled = wgpu::Instance::enabled_backend_features();
    // Instance::new panics when no backend was compiled for the target. GPU
    // availability must remain a Result so the native engine can use its CPU path.
    if !enabled.contains(wgpu::Backends::VULKAN) {
        return Err(format!(
            "Vulkan backend is not enabled for target {}-{} (wgpu backends: {enabled:?})",
            std::env::consts::ARCH,
            std::env::consts::OS,
        ));
    }
    Ok(wgpu::Instance::new(wgpu::InstanceDescriptor {
        backends: wgpu::Backends::VULKAN,
        ..wgpu::InstanceDescriptor::new_without_display_handle()
    }))
}

/// Shared Vulkan device/queue for operators that must exchange GPU buffers.
pub struct GpuContext {
    pub(crate) device: Arc<wgpu::Device>,
    pub(crate) queue: Arc<wgpu::Queue>,
    pub adapter: wgpu::AdapterInfo,
}

impl GpuContext {
    pub fn command_encoder(&self) -> wgpu::CommandEncoder {
        self.device.create_command_encoder(&Default::default())
    }

    pub fn submit(&self, encoder: wgpu::CommandEncoder) -> wgpu::SubmissionIndex {
        self.queue.submit([encoder.finish()])
    }

    pub async fn new() -> Result<Self, String> {
        let instance = new_vulkan_instance()?;
        let mut adapters = instance.enumerate_adapters(wgpu::Backends::VULKAN).await;
        let preferred = adapters.iter().position(|a| a.get_info().device_type == wgpu::DeviceType::DiscreteGpu)
            .or_else(|| adapters.iter().position(|a| a.get_info().device_type == wgpu::DeviceType::IntegratedGpu))
            .ok_or("no hardware Vulkan adapter available")?;
        let adapter = adapters.swap_remove(preferred);
        let info = adapter.get_info();
        let (device, queue) = adapter.request_device(&wgpu::DeviceDescriptor {
            label: Some("OniSim shared Vulkan context"),
            required_limits: wgpu::Limits::default(),
            ..Default::default()
        }).await.map_err(|error| error.to_string())?;
        Ok(Self { device: Arc::new(device), queue: Arc::new(queue), adapter: info })
    }
}

#[cfg(all(test, target_vendor = "apple"))]
mod tests {
    use super::GpuContext;

    #[test]
    fn unavailable_vulkan_backend_returns_errors_from_both_constructors() {
        // The crate enables native Vulkan, without Apple's portability backend.
        // Exercise that real build configuration rather than mocked feature bits.
        assert!(!wgpu::Instance::enabled_backend_features().contains(wgpu::Backends::VULKAN));
        let context_error = pollster::block_on(GpuContext::new())
            .err()
            .expect("unsupported Vulkan context must return an error");
        let flow_error = pollster::block_on(crate::FlowGpu::new(1, 1, ""))
            .err()
            .expect("unsupported Vulkan flow operator must return an error");
        for error in [context_error, flow_error] {
            assert!(error.contains("Vulkan backend is not enabled"), "{error}");
        }
    }
}
