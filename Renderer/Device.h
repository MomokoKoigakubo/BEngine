#pragma once
#include <Volk/volk.h>
#include <vma/vk_mem_alloc.h>
#include <vector>
#include <functional>
// Result of querying what a physical device + surface support for a swapchain.
// Public because the Renderer's swapchain creation reads it too.
struct SwapChainSupportDetails
{
	VkSurfaceCapabilitiesKHR capabilities;
	std::vector<VkSurfaceFormatKHR> formats;
	std::vector<VkPresentModeKHR> presentModes;
};

class Buffer;

// Owns the physical + logical device, the graphics/present queues, the queue
// family indices, and the VMA allocator. Built from an instance + surface.
class Device
{
public:
	Device(VkInstance instance, VkSurfaceKHR surface);
	~Device();
	Device(const Device&) = delete;
	Device& operator=(const Device&) = delete;

	VkPhysicalDevice physical() const { return physicalDevice; }
	VkDevice get() const { return device; }
	VkQueue graphicsQueue() const { return graphicsQueue_; }
	VkQueue presentQueue() const { return presentQueue_; }
	uint32_t graphicsFamily() const { return graphicsQueueFamilyIndex; }
	uint32_t presentFamily() const { return presentQueueFamilyIndex; }
	VmaAllocator allocator() const { return allocator_; }
	uint32_t maxBindlessTextures() const { return maxBindlessTextures_; }

	SwapChainSupportDetails querySwapchainSupport(VkSurfaceKHR surface) const;
	void uploadToBuffer(Buffer& dst, const void* data, VkDeviceSize size);
	void immediateSubmit(const std::function<void(VkCommandBuffer)>& record);

private:
	VkInstance instance = VK_NULL_HANDLE;
	VkSurfaceKHR surface = VK_NULL_HANDLE;

	VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;
	VkDevice device = VK_NULL_HANDLE;
	VkQueue graphicsQueue_ = VK_NULL_HANDLE;
	VkQueue presentQueue_ = VK_NULL_HANDLE;
	uint32_t graphicsQueueFamilyIndex = UINT32_MAX;
	uint32_t presentQueueFamilyIndex = UINT32_MAX;
	VmaAllocator allocator_ = VK_NULL_HANDLE;
	uint32_t maxBindlessTextures_ = 0;

	void pickPhysicalDevice();
	void createLogicalDevice();
	void createAllocator();
};
