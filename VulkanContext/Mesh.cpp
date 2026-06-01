#include "Mesh.h"

Mesh::Mesh(Device& device, const std::vector<Vertex>& vertices,
    const std::vector<uint32_t>& indices)
    : vertexBuffer(device.allocator(), sizeof(Vertex)* vertices.size(),
        VK_BUFFER_USAGE_VERTEX_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT,
        VMA_MEMORY_USAGE_AUTO),
    indexBuffer(device.allocator(), sizeof(uint32_t)* indices.size(),
        VK_BUFFER_USAGE_INDEX_BUFFER_BIT | VK_BUFFER_USAGE_TRANSFER_DST_BIT,
        VMA_MEMORY_USAGE_AUTO),
    indexCount(static_cast<uint32_t>(indices.size()))
{
    device.uploadToBuffer(vertexBuffer, vertices.data(), sizeof(Vertex) * vertices.size());
    device.uploadToBuffer(indexBuffer, indices.data(), sizeof(uint32_t) * indices.size());
}

void Mesh::draw(VkCommandBuffer cmd) const
{
    VkBuffer vertexBuffers[] = { vertexBuffer.get() };
    VkDeviceSize offsets[] = { 0 };
    vkCmdBindVertexBuffers(cmd, 0, 1, vertexBuffers, offsets);
    vkCmdBindIndexBuffer(cmd, indexBuffer.get(), 0, VK_INDEX_TYPE_UINT32);
    vkCmdDrawIndexed(cmd, indexCount, 1, 0, 0, 0);
}