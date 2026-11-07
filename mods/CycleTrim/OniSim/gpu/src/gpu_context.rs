use std::sync::Arc;

/// Shared Vulkan device/queue for operators that must exchange GPU buffers.
pub struct GpuContext {
    pub(crate) device: Arc<wgpu::Device>,
    pub(crate) queue: Arc<wgpu::Queue>,
    pub adapter: wgpu::AdapterInfo,
}

impl GpuContext {
    pub async fn new() -> Result<Self, String> {
        let instance = wgpu::Instance::new(wgpu::InstanceDescriptor {
            backends: wgpu::Backends::VULKAN,
            ..wgpu::InstanceDescriptor::new_without_display_handle()
        });
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
