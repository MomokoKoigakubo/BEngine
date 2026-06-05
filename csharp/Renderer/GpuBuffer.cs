using Silk.NET.Vulkan;

namespace IdleL.Rendering;

// A VkBuffer backed by a suballocation from the shared GpuAllocator (not its own vkAllocateMemory).
// Host-visible buffers map + memcpy on Upload; device-local ones are filled via UploadToBuffer (staging).
unsafe class GpuBuffer : IDisposable
{
    readonly Vk vk;
    readonly Device device;
    readonly GpuAllocator allocator;
    Allocation allocation;
    public Silk.NET.Vulkan.Buffer Handle;
    public ulong Size;

    public GpuBuffer(GpuDevice dev, ulong size, BufferUsageFlags usage, MemoryPropertyFlags memProps)
    {
        vk = dev.Vk;
        device = dev.Device;
        allocator = dev.Allocator;
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
        allocation = allocator.Allocate(req.Size, req.Alignment, dev.FindMemoryType(req.MemoryTypeBits, memProps));
        vk.BindBufferMemory(device, Handle, allocation.MemOrigin, allocation.MemOffset);
    }

    // host-visible only: map, copy, unmap (memory is coherent so no explicit flush)
    public void Upload<T>(ReadOnlySpan<T> data, ulong offsetBytes = 0) where T : unmanaged
    {
        ulong bytes = (ulong)(data.Length * sizeof(T));
        void* mapped;
        vk.MapMemory(device, allocation.MemOrigin, allocation.MemOffset + offsetBytes, bytes, 0, &mapped);
        fixed (void* src = data) System.Buffer.MemoryCopy(src, mapped, bytes, bytes);
        vk.UnmapMemory(device, allocation.MemOrigin);
    }

    public void Dispose()
    {
        vk.DestroyBuffer(device, Handle, null);
        allocator.Free(allocation);
    }
}
