#pragma once
#include <Volk/volk.h>
#include <vma/vk_mem_alloc.h>

class Buffer
{
public:
	Buffer(VmaAllocator allocator, VkDeviceSize size,
		VkBufferUsageFlags usage, VmaMemoryUsage memoryUsage,
		VmaAllocationCreateFlags flags = 0);

	~Buffer();
	Buffer(const Buffer&) = delete;
	Buffer& operator=(const Buffer&) = delete;
	void upload(const void* data, VkDeviceSize size);
	VkBuffer get() const { return buffer; }
	VmaAllocation allocation() const { return allocation_; }
private:
	VmaAllocator allocator = VK_NULL_HANDLE;
	VkBuffer buffer = VK_NULL_HANDLE;
	VmaAllocation allocation_ = VK_NULL_HANDLE;
};