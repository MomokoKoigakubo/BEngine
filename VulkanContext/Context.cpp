#include "../util/fileIO.h"
#include "Context.h"
#include "VkCheck.h"
#include "SDL3/SDL_vulkan.h"
#include <cstdlib>
#include <iostream>
#include <vector>
#include <optional>
#include <set>
#include <string>
#include <limits>
#include <algorithm>
#include <fstream>

namespace {
	// constants
#ifdef NDEBUG
	constexpr bool enableValidationLayers = false;
#else
	constexpr bool enableValidationLayers = true;
#endif

	const std::vector<const char*> validationLayers =
	{
		"VK_LAYER_KHRONOS_validation"
	};

	const std::vector<const char*> deviceExtensions =
	{
		VK_KHR_SWAPCHAIN_EXTENSION_NAME
	};

	constexpr uint32_t MAX_FRAMES_IN_FLIGHT = 2;
	// end constants
	// structs
	struct QueueFamilyIndices
	{
		std::optional<uint32_t> presentFamily;
		std::optional<uint32_t> graphicsFamily;

		bool isComplete()
		{
			return graphicsFamily.has_value() && presentFamily.has_value();
		}
	};

	struct SwapChainSupportDetails
	{
		VkSurfaceCapabilitiesKHR capabilities;
		std::vector<VkSurfaceFormatKHR> formats;
		std::vector<VkPresentModeKHR> presentModes;
	};
	//end structs

	//helpers

	static bool checkValidationLayerSupport()
	{
		uint32_t layerCount;
		vkEnumerateInstanceLayerProperties(&layerCount, nullptr);

		std::vector<VkLayerProperties> availableLayers(layerCount);
		vkEnumerateInstanceLayerProperties(&layerCount, availableLayers.data());

		for (const char* layerName : validationLayers) {
			bool layerFound = false;

			for (const auto& layerProperties : availableLayers)
			{
				if (strcmp(layerName, layerProperties.layerName) == 0)
				{
					layerFound = true;
					break;
				}
			}

			if (!layerFound)
			{
				return false;
			}
		}

		return true;
	}

	//beginng of message call back windows specific since linux auto sends to terminal
	//windows sends this to msvc terminal not powershell will always configuration
	static std::vector<const char*> getRequiredExtensions()
	{
		uint32_t SDLExtensionCount = 0;
		const char* const* SDLExtensions;
		SDLExtensions = SDL_Vulkan_GetInstanceExtensions(&SDLExtensionCount);

		std::vector<const char*> extensions(SDLExtensions, SDLExtensions + SDLExtensionCount);

		if (enableValidationLayers)
		{
			extensions.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
		}

		return extensions;
	}

	static VKAPI_ATTR VkBool32 VKAPI_CALL debugCallback(
		VkDebugUtilsMessageSeverityFlagBitsEXT messageSeverity,
		VkDebugUtilsMessageTypeFlagsEXT messageType,
		const VkDebugUtilsMessengerCallbackDataEXT* pCallbackData,
		void* pUserData)
	{
		static std::ofstream log("validation.log", std::ios::trunc);
		log << pCallbackData->pMessage << "\n\n";
		log.flush();

		std::cerr << "validation layer: " << pCallbackData->pMessage << std::endl;

		return VK_FALSE;
	}

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

		// Assign index to queue families that could be found
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

	SwapChainSupportDetails querySwapChainSupport(VkPhysicalDevice device, VkSurfaceKHR surface) {
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

			vkGetPhysicalDeviceSurfacePresentModesKHR(device, surface, &presentModeCount,
				details.presentModes.data());
		}

		return details;
	}

	static bool isDeviceSuitable(VkPhysicalDevice device, VkSurfaceKHR surface)
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
			SwapChainSupportDetails swapChainSupport = querySwapChainSupport(device, surface);
			swapChainAdequate = !swapChainSupport.formats.empty() && !swapChainSupport.presentModes.empty();
		}

		return (deviceProperties.properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU || deviceProperties.properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU)
			&& findQueueFamilies(device, surface).isComplete() && extensionsSupported && swapChainAdequate;
	}

	VkSurfaceFormatKHR chooseSwapSurfaceFormat(const std::vector<VkSurfaceFormatKHR>& availableFormats) {
		for (const auto& availableFormat : availableFormats)
		{
			if (availableFormat.format == VK_FORMAT_B8G8R8A8_SRGB && availableFormat.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR)
			{
				return availableFormat;
			}
		}

		return availableFormats[0];
	}

	VkPresentModeKHR chooseSwapPresentMode(const std::vector<VkPresentModeKHR>& availablePresentModes) {
		for (const auto& availablePresentMode : availablePresentModes)
		{
			if (availablePresentMode == VK_PRESENT_MODE_MAILBOX_KHR)
			{
				return availablePresentMode;
			}
		}

		return VK_PRESENT_MODE_FIFO_KHR;
	}

	VkExtent2D chooseSwapExtent(const VkSurfaceCapabilitiesKHR& capabilities, SDL_Window* window) {
		if (capabilities.currentExtent.width != std::numeric_limits<uint32_t>::max())
		{
			return capabilities.currentExtent;
		}
		else
		{

			int width, height;
			SDL_GetWindowSizeInPixels(window, &width, &height);

			VkExtent2D actualExtent =
			{
				static_cast<uint32_t>(width),
				static_cast<uint32_t>(height)
			};
			actualExtent.width = std::clamp(actualExtent.width, capabilities.minImageExtent.width, capabilities.maxImageExtent.width);
			actualExtent.height = std::clamp(actualExtent.height, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);

			return actualExtent;
		}
	}
}

Context::Context(SDL_Window* window)
{
	this->window = window;
	VK_CHECK(volkInitialize());
	createInstance();
	volkLoadInstance(instance);
	createSurface(window);
	setupDebugMessenger();
	pickPhysicalDevice();
	createLogicalDevice();
	createAllocator();
	createSwapchain();
	createImageViews();
	createGraphicsPipeline();
	createCommandPool();
	createCommandBuffer();
	createSyncObjects();

}

Context::~Context()
{
	vkDeviceWaitIdle(device);
	for (auto imageView : swapchainImageViews)
	{
		vkDestroyImageView(device, imageView, nullptr);
	}
	for (auto semaphore : renderFinishedSemaphores)
	{
		vkDestroySemaphore(device, semaphore, nullptr);
	}

	for (auto semaphore : imageAvailableSemaphores)
	{
		vkDestroySemaphore(device, semaphore, nullptr);
	}

	for (auto fence : inFlightFences)
	{
		vkDestroyFence(device, fence, nullptr);
	}

	vkDestroyCommandPool(device, commandPool, nullptr);
	vkDestroyPipeline(device, graphicsPipeline, nullptr);
	vkDestroyPipelineLayout(device, pipelineLayout, nullptr);
	vkDestroySwapchainKHR(device, swapchain, nullptr);
	vmaDestroyAllocator(allocator);
	vkDestroyDevice(device, nullptr);
	vkDestroySurfaceKHR(instance, surface, nullptr);

	if (enableValidationLayers)
	{
		vkDestroyDebugUtilsMessengerEXT(instance, debugMessenger, nullptr);
	}

	vkDestroyInstance(instance, nullptr);
}

void Context::createInstance()
{
	if (enableValidationLayers && !checkValidationLayerSupport())
	{
		throw std::runtime_error("validation layers requested, but not available!");
	}
	//Application Information
	VkApplicationInfo appInfo{};
	appInfo.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
	appInfo.pApplicationName = "IdleL";
	appInfo.pEngineName = "BEngine";
	appInfo.applicationVersion = VK_MAKE_VERSION(0, 0, 1);
	appInfo.engineVersion = VK_MAKE_VERSION(0, 0, 1);
	appInfo.apiVersion = VK_API_VERSION_1_3;

	//Instance Create Info
	VkInstanceCreateInfo createInfo{};
	createInfo.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
	createInfo.pApplicationInfo = &appInfo;

	if (enableValidationLayers)
	{
		createInfo.enabledLayerCount = static_cast<uint32_t>(validationLayers.size());
		createInfo.ppEnabledLayerNames = validationLayers.data();
	}
	else
	{
		createInfo.enabledLayerCount = 0;
	}

	//extensions
	std::vector<const char*> SDLextensions = getRequiredExtensions();
	createInfo.enabledExtensionCount = static_cast<uint32_t>(SDLextensions.size());
	createInfo.ppEnabledExtensionNames = SDLextensions.data();

	VK_CHECK(vkCreateInstance(&createInfo, nullptr, &instance));
}

void Context::createSurface(SDL_Window* window)
{
	if (!SDL_Vulkan_CreateSurface(window, instance, nullptr, &surface))
	{
		throw std::runtime_error("Failed to create window for the application!");
	}
}

void Context::setupDebugMessenger()
{
	if (!enableValidationLayers) return;

	VkDebugUtilsMessengerCreateInfoEXT createInfo{};
	createInfo.sType = VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT;
	createInfo.messageSeverity = VK_DEBUG_UTILS_MESSAGE_SEVERITY_VERBOSE_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
	createInfo.messageType = VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT | VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
	createInfo.pfnUserCallback = debugCallback;
	createInfo.pUserData = nullptr;

	VK_CHECK(vkCreateDebugUtilsMessengerEXT(instance, &createInfo, nullptr, &debugMessenger));
}

void Context::pickPhysicalDevice()
{
	uint32_t deviceCount = 0;
	vkEnumeratePhysicalDevices(instance, &deviceCount, nullptr);
	if (deviceCount == 0)
	{
		throw std::runtime_error("Failed to find GPU with vulkan support.");
	}
	std::vector<VkPhysicalDevice>devices(deviceCount);
	vkEnumeratePhysicalDevices(instance, &deviceCount, devices.data());
	for (const auto& device : devices)
	{
		if (isDeviceSuitable(device, surface))
		{
			physicalDevice = device;
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

void Context::createLogicalDevice() {
	std::vector<VkDeviceQueueCreateInfo> queueCreateInfos;
	std::set<uint32_t> uniqueQueueFamilies =
	{
		graphicsQueueFamilyIndex, presentQueueFamilyIndex
	};

	float queuePriority = 1.0f;
	for (uint32_t queueFamily : uniqueQueueFamilies)
	{
		VkDeviceQueueCreateInfo queueCreateInfo{};
		queueCreateInfo.sType =
			VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
		queueCreateInfo.queueFamilyIndex = queueFamily;
		queueCreateInfo.queueCount = 1;
		queueCreateInfo.pQueuePriorities =
			&queuePriority;
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
	createInfo.pQueueCreateInfos = queueCreateInfos.data();
	createInfo.queueCreateInfoCount = static_cast<uint32_t>(queueCreateInfos.size());
	createInfo.pEnabledFeatures = &deviceFeatures;
	createInfo.enabledExtensionCount = static_cast<uint32_t>(deviceExtensions.size());
	createInfo.pNext = &features13;
	createInfo.ppEnabledExtensionNames = deviceExtensions.data();

	if (vkCreateDevice(physicalDevice, &createInfo, nullptr, &device) != VK_SUCCESS)
	{
		throw std::runtime_error("failed to create logical device!");
	}

	volkLoadDevice(device);
	vkGetDeviceQueue(device, graphicsQueueFamilyIndex, 0, &graphicsQueue);
	vkGetDeviceQueue(device, presentQueueFamilyIndex, 0, &presentQueue);
}

void Context::createAllocator()
{
	VmaAllocatorCreateInfo allocatorInfo{};
	allocatorInfo.physicalDevice = physicalDevice;
	allocatorInfo.device = device;
	allocatorInfo.instance = instance;
	allocatorInfo.vulkanApiVersion = VK_API_VERSION_1_3;

	VmaVulkanFunctions vulkanFunctions{};
	vmaImportVulkanFunctionsFromVolk(&allocatorInfo, &vulkanFunctions);
	allocatorInfo.pVulkanFunctions = &vulkanFunctions;

	VK_CHECK(vmaCreateAllocator(&allocatorInfo, &allocator));
}

void Context::createSwapchain()
{
	SwapChainSupportDetails swapChainSupport = querySwapChainSupport(physicalDevice, surface);
	VkSurfaceFormatKHR surfaceFormat = chooseSwapSurfaceFormat(swapChainSupport.formats);
	VkPresentModeKHR presentMode = chooseSwapPresentMode(swapChainSupport.presentModes);
	VkExtent2D extent = chooseSwapExtent(swapChainSupport.capabilities, window);

	uint32_t imageCount = swapChainSupport.capabilities.minImageCount + 1;
	if (swapChainSupport.capabilities.maxImageCount > 0 && imageCount > swapChainSupport.capabilities.maxImageCount)
	{
		imageCount = swapChainSupport.capabilities.maxImageCount;
	}

	VkSwapchainCreateInfoKHR createInfo{};
	createInfo.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
	createInfo.surface = surface;
	createInfo.minImageCount = imageCount;
	createInfo.imageFormat = surfaceFormat.format;
	createInfo.imageColorSpace = surfaceFormat.colorSpace;
	createInfo.imageExtent = extent;
	createInfo.imageArrayLayers = 1;
	createInfo.imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;

	uint32_t queueFamilyIndices[] = { graphicsQueueFamilyIndex, presentQueueFamilyIndex };
	if (graphicsQueueFamilyIndex != presentQueueFamilyIndex)
	{
		createInfo.imageSharingMode = VK_SHARING_MODE_CONCURRENT;
		createInfo.queueFamilyIndexCount = 2;
		createInfo.pQueueFamilyIndices = queueFamilyIndices;
	}
	else
	{
		createInfo.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
	}

	createInfo.preTransform = swapChainSupport.capabilities.currentTransform;
	createInfo.compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
	createInfo.presentMode = presentMode;
	createInfo.clipped = VK_TRUE;
	createInfo.oldSwapchain = VK_NULL_HANDLE;

	VK_CHECK(vkCreateSwapchainKHR(device, &createInfo, nullptr, &swapchain));
	vkGetSwapchainImagesKHR(device, swapchain, &imageCount, nullptr);
	swapchainImages.resize(imageCount);
	vkGetSwapchainImagesKHR(device, swapchain, &imageCount, swapchainImages.data());

	swapchainImageFormat = surfaceFormat.format;
	swapchainExtent = extent;
}

void Context::createImageViews()
{
	swapchainImageViews.resize(swapchainImages.size());

	for (size_t i = 0; i < swapchainImages.size(); i++)
	{
		VkImageViewCreateInfo createInfo{};
		createInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
		createInfo.image = swapchainImages[i];
		createInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
		createInfo.format = swapchainImageFormat;
		createInfo.components.r = VK_COMPONENT_SWIZZLE_IDENTITY;
		createInfo.components.g = VK_COMPONENT_SWIZZLE_IDENTITY;
		createInfo.components.b = VK_COMPONENT_SWIZZLE_IDENTITY;
		createInfo.components.a = VK_COMPONENT_SWIZZLE_IDENTITY;
		createInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
		createInfo.subresourceRange.baseMipLevel = 0;
		createInfo.subresourceRange.levelCount = 1;
		createInfo.subresourceRange.baseArrayLayer = 0;
		createInfo.subresourceRange.layerCount = 1;

		VK_CHECK(vkCreateImageView(device, &createInfo, nullptr,
			&swapchainImageViews[i]));
	}
}

void Context::recreateSwapchain()
{
	int width = 0, height = 0;
	SDL_GetWindowSizeInPixels(window, &width, &height);
	while (width == 0 || height == 0)
	{
		SDL_GetWindowSizeInPixels(window, &width, &height);
		SDL_WaitEvent(nullptr);
	}
	vkDeviceWaitIdle(device);

	for (auto imageView : swapchainImageViews)
	{
		vkDestroyImageView(device, imageView, nullptr);
	}
	vkDestroySwapchainKHR(device, swapchain, nullptr);

	createSwapchain();
	createImageViews();
}

VkShaderModule Context::createShaderModule(const std::vector<char>& code)
{
	VkShaderModuleCreateInfo createInfo{};
	createInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
	createInfo.codeSize = code.size();
	createInfo.pCode = reinterpret_cast<const uint32_t*>(code.data());

	VkShaderModule shaderModule;
	VK_CHECK(vkCreateShaderModule(device, &createInfo, nullptr, &shaderModule));
	return shaderModule;
}

void Context::createGraphicsPipeline()
{
	auto shaderCode = fileio::readBinary(SHADER_DIR "/triangle.spv");
	VkShaderModule shaderModule = createShaderModule(shaderCode);

	VkPipelineShaderStageCreateInfo vertStageInfo{};
	vertStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	vertStageInfo.stage = VK_SHADER_STAGE_VERTEX_BIT;
	vertStageInfo.module = shaderModule;
	vertStageInfo.pName = "vertMain";  //slang entry point

	VkPipelineShaderStageCreateInfo fragStageInfo{};
	fragStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	fragStageInfo.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
	fragStageInfo.module = shaderModule;
	fragStageInfo.pName = "fragMain";  //slang frag entry point

	VkPipelineShaderStageCreateInfo shaderStages[] = { vertStageInfo, fragStageInfo };

	VkPipelineVertexInputStateCreateInfo vertexInputInfo{};
	vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
	vertexInputInfo.vertexBindingDescriptionCount = 0;
	vertexInputInfo.vertexAttributeDescriptionCount = 0;

	VkPipelineInputAssemblyStateCreateInfo inputAssembly{};
	inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
	inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
	inputAssembly.primitiveRestartEnable = VK_FALSE;

	std::vector<VkDynamicState> dynamicStates = {
		VK_DYNAMIC_STATE_VIEWPORT,
		VK_DYNAMIC_STATE_SCISSOR
	};

	VkPipelineDynamicStateCreateInfo dynamicState{};
	dynamicState.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
	dynamicState.dynamicStateCount = static_cast<uint32_t>(dynamicStates.size());
	dynamicState.pDynamicStates = dynamicStates.data();

	VkPipelineViewportStateCreateInfo viewportState{};
	viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
	viewportState.viewportCount = 1;
	viewportState.scissorCount = 1;

	VkPipelineRasterizationStateCreateInfo rasterizer{};
	rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
	rasterizer.depthClampEnable = VK_FALSE;
	rasterizer.rasterizerDiscardEnable = VK_FALSE;
	rasterizer.polygonMode = VK_POLYGON_MODE_FILL;
	rasterizer.lineWidth = 1.0f;
	rasterizer.cullMode = VK_CULL_MODE_BACK_BIT;
	rasterizer.frontFace = VK_FRONT_FACE_CLOCKWISE;
	rasterizer.depthBiasEnable = VK_FALSE;

	VkPipelineMultisampleStateCreateInfo multisampling{};
	multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
	multisampling.sampleShadingEnable = VK_FALSE;
	multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

	VkPipelineColorBlendAttachmentState colorBlendAttachment{};
	colorBlendAttachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT |
		VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
	colorBlendAttachment.blendEnable = VK_FALSE;

	VkPipelineColorBlendStateCreateInfo colorBlending{};
	colorBlending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
	colorBlending.logicOpEnable = VK_FALSE;
	colorBlending.attachmentCount = 1;
	colorBlending.pAttachments = &colorBlendAttachment;

	VkPushConstantRange pushConstantRange{};
	pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT;
	pushConstantRange.offset = 0;
	pushConstantRange.size = sizeof(float);

	VkPipelineLayoutCreateInfo pipelineLayoutInfo{};
	pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
	pipelineLayoutInfo.setLayoutCount = 0;
	pipelineLayoutInfo.pushConstantRangeCount = 1;
	pipelineLayoutInfo.pPushConstantRanges = &pushConstantRange;

	VK_CHECK(vkCreatePipelineLayout(device, &pipelineLayoutInfo, nullptr, &pipelineLayout));

	VkPipelineRenderingCreateInfo renderingInfo{};
	renderingInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO;
	renderingInfo.colorAttachmentCount = 1;
	renderingInfo.pColorAttachmentFormats = &swapchainImageFormat;

	VkGraphicsPipelineCreateInfo pipelineInfo{};
	pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
	pipelineInfo.pNext = &renderingInfo;
	pipelineInfo.stageCount = 2;
	pipelineInfo.pStages = shaderStages;
	pipelineInfo.pInputAssemblyState = &inputAssembly;
	pipelineInfo.pVertexInputState = &vertexInputInfo;
	pipelineInfo.pViewportState = &viewportState;
	pipelineInfo.pRasterizationState = &rasterizer;
	pipelineInfo.pMultisampleState = &multisampling;
	pipelineInfo.pDynamicState = &dynamicState;
	pipelineInfo.pColorBlendState = &colorBlending;
	pipelineInfo.layout = pipelineLayout;
	pipelineInfo.renderPass = VK_NULL_HANDLE;
	pipelineInfo.subpass = 0;

	VK_CHECK(vkCreateGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &graphicsPipeline));
	vkDestroyShaderModule(device, shaderModule, nullptr);
}

void Context::createCommandPool()
{
	VkCommandPoolCreateInfo poolInfo{};
	poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
	poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
	poolInfo.queueFamilyIndex = graphicsQueueFamilyIndex;

	VK_CHECK(vkCreateCommandPool(device, &poolInfo, nullptr, &commandPool));
}

void Context::createCommandBuffer()
{
	commandBuffers.resize(MAX_FRAMES_IN_FLIGHT);

	VkCommandBufferAllocateInfo allocInfo{};
	allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
	allocInfo.commandPool = commandPool;
	allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
	allocInfo.commandBufferCount = MAX_FRAMES_IN_FLIGHT;

	VK_CHECK(vkAllocateCommandBuffers(device, &allocInfo, commandBuffers.data()));
}

void Context::createSyncObjects()
{
	VkSemaphoreCreateInfo semaphoreInfo{};
	semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

	VkFenceCreateInfo fenceInfo{};
	fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
	fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT;

	imageAvailableSemaphores.resize(MAX_FRAMES_IN_FLIGHT);
	inFlightFences.resize(MAX_FRAMES_IN_FLIGHT);
	for (size_t i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
	{
		VK_CHECK(vkCreateSemaphore(device, &semaphoreInfo, nullptr, &imageAvailableSemaphores[i]));
		VK_CHECK(vkCreateFence(device, &fenceInfo, nullptr, &inFlightFences[i]));
	}

	renderFinishedSemaphores.resize(swapchainImages.size());
	for (size_t i = 0; i < swapchainImages.size(); i++)
	{
		VK_CHECK(vkCreateSemaphore(device, &semaphoreInfo, nullptr, &renderFinishedSemaphores[i]));
	}
}

void Context::recordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex)
{
	VkCommandBufferBeginInfo beginInfo{};
	beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
	VK_CHECK(vkBeginCommandBuffer(commandBuffer, &beginInfo));

	VkImageMemoryBarrier2 toColorBarrier{};
	toColorBarrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
	toColorBarrier.srcStageMask = VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT;
	toColorBarrier.srcAccessMask = 0;
	toColorBarrier.dstStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
	toColorBarrier.dstAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
	toColorBarrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
	toColorBarrier.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
	toColorBarrier.image = swapchainImages[imageIndex];
	toColorBarrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
	toColorBarrier.subresourceRange.baseMipLevel = 0;
	toColorBarrier.subresourceRange.levelCount = 1;
	toColorBarrier.subresourceRange.baseArrayLayer = 0;
	toColorBarrier.subresourceRange.layerCount = 1;

	VkDependencyInfo dependencyInfo{};
	dependencyInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
	dependencyInfo.imageMemoryBarrierCount = 1;
	dependencyInfo.pImageMemoryBarriers = &toColorBarrier;

	vkCmdPipelineBarrier2(commandBuffer, &dependencyInfo);

	VkRenderingAttachmentInfo colorAttachment{};
	colorAttachment.sType = VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
	colorAttachment.imageView = swapchainImageViews[imageIndex];
	colorAttachment.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
	colorAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
	colorAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
	colorAttachment.clearValue.color = { 0.0f, 0.0f, 0.0f, 1.0f };

	VkRenderingInfo renderingInfo{};
	renderingInfo.sType = VK_STRUCTURE_TYPE_RENDERING_INFO;
	renderingInfo.renderArea.offset = { 0,0 };
	renderingInfo.renderArea.extent = swapchainExtent;
	renderingInfo.layerCount = 1;
	renderingInfo.colorAttachmentCount = 1;
	renderingInfo.pColorAttachments = &colorAttachment;

	vkCmdBeginRendering(commandBuffer, &renderingInfo);
	vkCmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, graphicsPipeline);

	VkViewport viewport{};
	viewport.x = 0.0f;
	viewport.y = 0.0f;
	viewport.width = (float)swapchainExtent.width;
	viewport.height = (float)swapchainExtent.height;
	viewport.minDepth = 0.0f;
	viewport.maxDepth = 1.0f;
	vkCmdSetViewport(commandBuffer, 0, 1, &viewport);

	VkRect2D scissor{};
	scissor.offset = { 0, 0, };
	scissor.extent = swapchainExtent;
	vkCmdSetScissor(commandBuffer, 0, 1, &scissor);


	float aspectScaleX = (float)swapchainExtent.height / (float)swapchainExtent.width;
	vkCmdPushConstants(commandBuffer, pipelineLayout, VK_SHADER_STAGE_VERTEX_BIT, 0, sizeof(float), &aspectScaleX);

	vkCmdDraw(commandBuffer, 3, 1, 0, 0);

	vkCmdEndRendering(commandBuffer);

	VkImageMemoryBarrier2 toPresentBarrier{};
	toPresentBarrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
	toPresentBarrier.srcStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
	toPresentBarrier.srcAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
	toPresentBarrier.dstStageMask = VK_PIPELINE_STAGE_2_BOTTOM_OF_PIPE_BIT;
	toPresentBarrier.dstAccessMask = 0;
	toPresentBarrier.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
	toPresentBarrier.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
	toPresentBarrier.image = swapchainImages[imageIndex];
	toPresentBarrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
	toPresentBarrier.subresourceRange.baseMipLevel = 0;
	toPresentBarrier.subresourceRange.levelCount = 1;
	toPresentBarrier.subresourceRange.baseArrayLayer = 0;
	toPresentBarrier.subresourceRange.layerCount = 1;

	VkDependencyInfo presentDependency{};
	presentDependency.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
	presentDependency.imageMemoryBarrierCount = 1;
	presentDependency.pImageMemoryBarriers = &toPresentBarrier;

	vkCmdPipelineBarrier2(commandBuffer, &presentDependency);

	VK_CHECK(vkEndCommandBuffer(commandBuffer));
}

void Context::drawFrame()
{
	vkWaitForFences(device, 1, &inFlightFences[currentFrame], VK_TRUE, UINT64_MAX);

	// Recreate at the TOP (before acquiring) so THIS frame renders at the current
	// window size - avoids the one-frame size lag the compositor stretches (smear).
	if (framebufferResized)
	{
		framebufferResized = false;
		recreateSwapchain();
	}

	uint32_t imageIndex;
	VkResult acquiredResult = vkAcquireNextImageKHR(device, swapchain, UINT64_MAX,
		imageAvailableSemaphores[currentFrame], VK_NULL_HANDLE, &imageIndex);

	if (acquiredResult == VK_ERROR_OUT_OF_DATE_KHR)
	{
		recreateSwapchain();
		return;
	}

	vkResetFences(device, 1, &inFlightFences[currentFrame]);

	vkResetCommandBuffer(commandBuffers[currentFrame], 0);
	recordCommandBuffer(commandBuffers[currentFrame], imageIndex);

	VkSemaphoreSubmitInfo waitInfo{};
	waitInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
	waitInfo.semaphore = imageAvailableSemaphores[currentFrame];
	waitInfo.stageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
	
	VkSemaphoreSubmitInfo signalInfo{};
	signalInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
	signalInfo.semaphore = renderFinishedSemaphores[imageIndex];
	signalInfo.stageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;

	VkCommandBufferSubmitInfo cmdBufferInfo{};
	cmdBufferInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO;
	cmdBufferInfo.commandBuffer = commandBuffers[currentFrame];

	VkSubmitInfo2 submitInfo{};
	submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO_2;
	submitInfo.waitSemaphoreInfoCount = 1;
	submitInfo.pWaitSemaphoreInfos = &waitInfo;
	submitInfo.commandBufferInfoCount = 1;
	submitInfo.pCommandBufferInfos = &cmdBufferInfo;
	submitInfo.signalSemaphoreInfoCount = 1;
	submitInfo.pSignalSemaphoreInfos = &signalInfo;

	VK_CHECK(vkQueueSubmit2(graphicsQueue, 1, &submitInfo, inFlightFences[currentFrame]));

	VkPresentInfoKHR presentInfo{};
	presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
	presentInfo.waitSemaphoreCount = 1;
	presentInfo.pWaitSemaphores = &renderFinishedSemaphores[imageIndex];
	presentInfo.swapchainCount = 1;
	presentInfo.pSwapchains = &swapchain;
	presentInfo.pImageIndices = &imageIndex;

	VkResult presentResult = vkQueuePresentKHR(presentQueue, &presentInfo);
	if (presentResult == VK_ERROR_OUT_OF_DATE_KHR || presentResult == VK_SUBOPTIMAL_KHR)
	{
		framebufferResized = true;   // handled at the top of the next drawFrame
	}

	currentFrame = (currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
}