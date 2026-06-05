using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using IdleL.Rendering;

namespace IdleL.Resources;

// A drawable mesh: device-local vertex + index buffers, filled via staging copies.
unsafe class Mesh : IDisposable
{
    readonly Vk vk;
    readonly GpuBuffer vertexBuffer;
    readonly GpuBuffer indexBuffer;
    readonly uint indexCount;

    public Mesh(GpuDevice device, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices)
    {
        vk = device.Vk;
        indexCount = (uint)indices.Length;

        vertexBuffer = new GpuBuffer(device, (ulong)(vertices.Length * Unsafe.SizeOf<Vertex>()),
            BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit);
        indexBuffer = new GpuBuffer(device, (ulong)(indices.Length * sizeof(uint)),
            BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit);

        device.UploadToBuffer(vertexBuffer, vertices);
        device.UploadToBuffer(indexBuffer, indices);
    }

    public uint IndexCount => indexCount;

    public void Draw(CommandBuffer cmd, uint instanceCount, uint firstInstance)
    {
        var vb = vertexBuffer.Handle;
        ulong offset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, in vb, in offset);
        vk.CmdBindIndexBuffer(cmd, indexBuffer.Handle, 0, IndexType.Uint32);
        vk.CmdDrawIndexed(cmd, indexCount, instanceCount, 0, 0, firstInstance);   // firstInstance = base of SV_InstanceID
    }

    // bind + one indirect draw; the GPU-filled command supplies instanceCount (count decided by the cull compute)
    public void DrawIndirect(CommandBuffer cmd, Silk.NET.Vulkan.Buffer indirect, ulong offset)
    {
        var vb = vertexBuffer.Handle;
        ulong vbOffset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, in vb, in vbOffset);
        vk.CmdBindIndexBuffer(cmd, indexBuffer.Handle, 0, IndexType.Uint32);
        vk.CmdDrawIndexedIndirect(cmd, indirect, offset, 1, (uint)sizeof(DrawIndexedIndirectCommand));
    }

    public void Dispose()
    {
        vertexBuffer.Dispose();
        indexBuffer.Dispose();
    }
}
