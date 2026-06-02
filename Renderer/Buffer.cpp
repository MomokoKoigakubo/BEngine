#include "Buffer.h"
#include "VkCheck.h"

Buffer::Buffer(VmaAllocator allocator, VkDeviceSize size, VkBufferUsageFlags usage, VmaMemoryUsage memoryUsage, VmaAllocationCreateFlags flags)
	: allocator(allocator)
{
	VkBufferCreateInfo bufferInfo{};
	bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
	bufferInfo.size = size;
	bufferInfo.usage = usage;
	bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

	VmaAllocationCreateInfo allocInfo{};
	allocInfo.usage = memoryUsage;
	allocInfo.flags = flags;
	VK_CHECK(vmaCreateBuffer(allocator, &bufferInfo, &allocInfo, &buffer, &allocation_, nullptr));
}

void Buffer::upload(const void* data, VkDeviceSize size)
{
	VK_CHECK(vmaCopyMemoryToAllocation(allocator, data, allocation_, 0, size));
}

Buffer::~Buffer()
{
	vmaDestroyBuffer(allocator, buffer, allocation_);
}