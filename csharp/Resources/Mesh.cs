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

    public void Draw(CommandBuffer cmd)
    {
        var vb = vertexBuffer.Handle;
        ulong offset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, in vb, in offset);
        vk.CmdBindIndexBuffer(cmd, indexBuffer.Handle, 0, IndexType.Uint32);
        vk.CmdDrawIndexed(cmd, indexCount, 1, 0, 0, 0);
    }

    public void Dispose()
    {
        vertexBuffer.Dispose();
        indexBuffer.Dispose();
    }
}
