#include "Buffer.h"
#include "Device.h"
#include "VkCheck.h"
#include <vector>
#include <set>
#include <string>
#include <optional>
#include <stdexcept>

namespace {
	const std::vector<const char*> deviceExtensions =
	{
		VK_KHR_SWAPCHAIN_EXTENSION_NAME
	};

	struct QueueFamilyIndices
	{
		std::optional<uint32_t> presentFamily;
		std::optional<uint32_t> graphicsFamily;

		bool isComplete()
		{
			return graphicsFamily.has_value() && presentFamily.has_value();
		}
	};

	QueueFamilyIndices findQueueFamilies(VkPhysicalDevice device, VkSurfaceKHR surface)
	{
		QueueFamilyIndices indices;
		uint32_t queueFamilyCount = 0;
		vkGetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, nullptr);
		std::vector<VkQueueFamilyProperties>queueFamilies(queueFamilyCount);
		vkGetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, queueFamilies.data());

		int i = 0;
		for (const auto& queueFamily : queueFamilies)
		{
			if (indices.isComplete())
			{
				break;
			}

			if (queueFamily.queueFlags & VK_QUEUE_GRAPHICS_BIT)
			{
				indices.graphicsFamily = i;
			}
			VkBool32 presentSupport = false;
			vkGetPhysicalDeviceSurfaceSupportKHR(device, i, surface, &presentSupport);
			if (presentSupport)
			{
				indices.presentFamily = i;
			}
			i++;
		}

		return indices;
	}

	bool checkDeviceExtensionSupport(VkPhysicalDevice device)
	{
		uint32_t extensionCount;
		vkEnumerateDeviceExtensionProperties(device, nullptr, &extensionCount, nullptr);

		std::vector<VkExtensionProperties> availableExtensions(extensionCount);
		vkEnumerateDeviceExtensionProperties(device, nullptr, &extensionCount, availableExtensions.data());

		std::set<std::string> requiredExtensions(deviceExtensions.begin(), deviceExtensions.end());

		for (const auto& extension : availableExtensions)
		{
			requiredExtensions.erase(extension.extensionName);
		}

		return requiredExtensions.empty();
	}

	SwapChainSupportDetails querySupport(VkPhysicalDevice device, VkSurfaceKHR surface)
	{
		SwapChainSupportDetails details;
		vkGetPhysicalDeviceSurfaceCapabilitiesKHR(device, surface, &details.capabilities);
		uint32_t formatCount;
		vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface, &formatCount, nullptr);
		if (formatCount != 0)
		{
			details.formats.resize(formatCount);
			vkGetPhysicalDeviceSurfaceFormatsKHR(device, surface, &formatCount, details.formats.data());
		}
		uint32_t presentModeCount;
		vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface, &presentModeCount, nullptr);
		if (presentModeCount != 0)
		{
			details.presentModes.resize(presentModeCount);
			vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface, &presentModeCount, details.presentModes.data());
		}

		return details;
	}

	bool isDeviceSuitable(VkPhysicalDevice device, VkSurfaceKHR surface)
	{
		VkPhysicalDeviceProperties2 deviceProperties{};
		deviceProperties.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2;

		VkPhysicalDeviceFeatures2 deviceFeatures{};
		deviceFeatures.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;

		vkGetPhysicalDeviceProperties2(device, &deviceProperties);
		vkGetPhysicalDeviceFeatures2(device, &deviceFeatures);

		bool extensionsSupported = checkDeviceExtensionSupport(device);

		bool swapChainAdequate = false;
		if (extensionsSupported)
		{
			SwapChainSupportDetails swapChainSupport = querySupport(device, surface);
			swapChainAdequate = !swapChainSupport.formats.empty() && !swapChainSupport.presentModes.empty();
		}

		return (deviceProperties.properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU || deviceProperties.properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU)
			&& findQueueFamilies(device, surface).isComplete() && extensionsSupported && swapChainAdequate;
	}
}

Device::Device(VkInstance instance, VkSurfaceKHR surface)
	: instance(instance), surface(surface)
{
	pickPhysicalDevice();
	createLogicalDevice();
	createAllocator();
}

Device::~Device()
{
	vmaDestroyAllocator(allocator_);
	vkDestroyDevice(device, nullptr);
}

SwapChainSupportDetails Device::querySwapchainSupport(VkSurfaceKHR surface) const
{
	return querySupport(physicalDevice, surface);
}

void Device::pickPhysicalDevice()
{
	uint32_t deviceCount = 0;
	vkEnumeratePhysicalDevices(instance, &deviceCount, nullptr);
	if (deviceCount == 0)
	{
		throw std::runtime_error("Failed to find GPU with vulkan support.");
	}
	std::vector<VkPhysicalDevice>devices(deviceCount);
	vkEnumeratePhysicalDevices(instance, &deviceCount, devices.data());
	for (const auto& candidate : devices)
	{
		if (isDeviceSuitable(candidate, surface))
		{
			physicalDevice = candidate;
			break;
		}
	}

	if (physicalDevice == VK_NULL_HANDLE)
	{
		throw std::runtime_error("Failed to find a suitable GPU device.");
	}

	QueueFamilyIndices indices = findQueueFamilies(physicalDevice, surface);
	graphicsQueueFamilyIndex = indices.graphicsFamily.value();
	presentQueueFamilyIndex = indices.presentFamily.value();
}

void Device::createLogicalDevice()
{
	std::vector<VkDeviceQueueCreateInfo> queueCreateInfos;
	std::set<uint32_t> uniqueQueueFamilies = { graphicsQueueFamilyIndex, presentQueueFamilyIndex };

	float queuePriority = 1.0f;
	for (uint32_t queueFamily : uniqueQueueFamilies)
	{
		VkDeviceQueueCreateInfo queueCreateInfo{};
		queueCreateInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
		queueCreateInfo.queueFamilyIndex = queueFamily;
		queueCreateInfo.queueCount = 1;
		queueCreateInfo.pQueuePriorities = &queuePriority;
		queueCreateInfos.push_back(queueCreateInfo);
	}

	VkPhysicalDeviceFeatures deviceFeatures{};

	VkPhysicalDeviceVulkan11Features features11{};
	features11.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES;
	features11.shaderDrawParameters = VK_TRUE;

	VkPhysicalDeviceVulkan13Features features13{};
	features13.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
	features13.dynamicRendering = VK_TRUE;
	features13.synchronization2 = VK_TRUE;
	features13.pNext = &features11;

	VkDeviceCreateInfo createInfo{};
	createInfo.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
	createInfo.pNext = &features13;
	createInfo.pQueueCreateInfos = queueCreateInfos.data();
	createInfo.queueCreateInfoCount = static_cast<uint32_t>(queueCreateInfos.size());
	createInfo.pEnabledFeatures = &deviceFeatures;
	createInfo.enabledExtensionCount = static_cast<uint32_t>(deviceExtensions.size());
	createInfo.ppEnabledExtensionNames = deviceExtensions.data();

	if (vkCreateDevice(physicalDevice, &createInfo, nullptr, &device) != VK_SUCCESS)
	{
		throw std::runtime_error("failed to create logical device!");
	}

	volkLoadDevice(device);
	vkGetDeviceQueue(device, graphicsQueueFamilyIndex, 0, &graphicsQueue_);
	vkGetDeviceQueue(device, presentQueueFamilyIndex, 0, &presentQueue_);
}

void Device::uploadToBuffer(Buffer& dst, const void* data, VkDeviceSize size)
{
	Buffer staging(allocator_, size, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
		VMA_MEMORY_USAGE_AUTO, VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT);
	staging.upload(data, size);

	immediateSubmit([&](VkCommandBuffer cmd) {
		VkBufferCopy copyRegion{};
		copyRegion.size = size;
		vkCmdCopyBuffer(cmd, staging.get(), dst.get(), 1, &copyRegion);
		});
}

void Device::immediateSubmit(const std::function<void(VkCommandBuffer)>& record)
{
	VkCommandPoolCreateInfo poolInfo{};
	poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
	poolInfo.flags = VK_COMMAND_POOL_CREATE_TRANSIENT_BIT;
	poolInfo.queueFamilyIndex = graphicsQueueFamilyIndex;
	VkCommandPool pool;
	VK_CHECK(vkCreateCommandPool(device, &poolInfo, nullptr, &pool));

	VkCommandBufferAllocateInfo allocInfo{};
	allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
	allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
	allocInfo.commandPool = pool;
	allocInfo.commandBufferCount = 1;
	VkCommandBuffer cmd;
	VK_CHECK(vkAllocateCommandBuffers(device, &allocInfo, &cmd));

	VkCommandBufferBeginInfo beginInfo{};
	beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
	beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
	vkBeginCommandBuffer(cmd, &beginInfo);

	record(cmd);

	vkEndCommandBuffer(cmd);

	VkSubmitInfo submitInfo{};
	submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
	submitInfo.commandBufferCount = 1;
	submitInfo.pCommandBuffers = &cmd;
	VK_CHECK(vkQueueSubmit(graphicsQueue_, 1, &submitInfo, VK_NULL_HANDLE));
	vkQueueWaitIdle(graphicsQueue_);

	vkDestroyCommandPool(device, pool, nullptr);
}

void Device::createAllocator()
{
	VmaAllocatorCreateInfo allocatorInfo{};
	allocatorInfo.physicalDevice = physicalDevice;
	allocatorInfo.device = device;
	allocatorInfo.instance = instance;
	allocatorInfo.vulkanApiVersion = VK_API_VERSION_1_3;

	VmaVulkanFunctions vulkanFunctions{};
	vmaImportVulkanFunctionsFromVolk(&allocatorInfo, &vulkanFunctions);
	allocatorInfo.pVulkanFunctions = &vulkanFunctions;

	VK_CHECK(vmaCreateAllocator(&allocatorInfo, &allocator_));
}
