using Silk.NET.Vulkan;

namespace IdleL.Rendering;

// A VkBuffer + its hand-rolled VkDeviceMemory (one allocation per buffer). Host-visible buffers map
// + memcpy on Upload; device-local buffers are filled via GpuDevice.UploadToBuffer (staging copy).
unsafe class GpuBuffer : IDisposable
{
    readonly Vk vk;
    readonly Device device;
    public Silk.NET.Vulkan.Buffer Handle;
    public DeviceMemory Memory;
    public ulong Size;

    public GpuBuffer(GpuDevice dev, ulong size, BufferUsageFlags usage, MemoryPropertyFlags memProps)
    {
        vk = dev.Vk;
        device = dev.Device;
        Size = size;

        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };
        VkCheck.Check(vk.CreateBuffer(device, in info, null, out Handle), "vkCreateBuffer");

        vk.GetBufferMemoryRequirements(device, Handle, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = dev.FindMemoryType(req.MemoryTypeBits, memProps)
        };
        VkCheck.Check(vk.AllocateMemory(device, in alloc, null, out Memory), "vkAllocateMemory");
        vk.BindBufferMemory(device, Handle, Memory, 0);
    }

    // host-visible only: map, copy, unmap (memory is coherent so no explicit flush)
    public void Upload<T>(ReadOnlySpan<T> data, ulong offsetBytes = 0) where T : unmanaged
    {
        ulong bytes = (ulong)(data.Length * sizeof(T));
        void* mapped;
        vk.MapMemory(device, Memory, offsetBytes, bytes, 0, &mapped);
        fixed (void* src = data) System.Buffer.MemoryCopy(src, mapped, bytes, bytes);
        vk.UnmapMemory(device, Memory);
    }

    public void Dispose()
    {
        vk.DestroyBuffer(device, Handle, null);
        vk.FreeMemory(device, Memory, null);
    }
}
