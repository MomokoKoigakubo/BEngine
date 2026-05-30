#pragma once
#include <Volk/volk.h> //volk needs to load first, vulkan loader
#include <vma/vk_mem_alloc.h>
#include <SDL3/SDL.h>
#include <vector>

class Context
{
public:
	Context(SDL_Window* window);
	~Context();
	Context(const Context&) = delete;
	Context& operator = (const Context&) = delete;
	void drawFrame();

	void setFrameBufferResized()
	{
		framebufferResized = true;
	}

private:
	// Window
	SDL_Window* window = nullptr;

	bool framebufferResized = false;
	// Instance
	VkInstance instance = VK_NULL_HANDLE;
	VkDebugUtilsMessengerEXT debugMessenger = VK_NULL_HANDLE;
	VmaAllocator allocator = VK_NULL_HANDLE;
	// Device and queue init
	VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;
	VkDevice device = VK_NULL_HANDLE;
	VkQueue graphicsQueue = VK_NULL_HANDLE;
	VkQueue presentQueue = VK_NULL_HANDLE;
	uint32_t graphicsQueueFamilyIndex = UINT32_MAX;
	uint32_t presentQueueFamilyIndex = UINT32_MAX;
	// Surface and swapchain
	VkSurfaceKHR surface = VK_NULL_HANDLE;
	VkSwapchainKHR swapchain = VK_NULL_HANDLE;
	std::vector<VkImage> swapchainImages;
	std::vector<VkImageView> swapchainImageViews;
	VkFormat swapchainImageFormat;
	VkExtent2D swapchainExtent;
	// Pipeline
	VkPipelineLayout pipelineLayout = VK_NULL_HANDLE;
	VkPipeline graphicsPipeline = VK_NULL_HANDLE;
	//command buffers
	uint32_t currentFrame = 0;
	VkCommandPool commandPool = VK_NULL_HANDLE;
	std::vector<VkCommandBuffer> commandBuffers;
	std::vector<VkSemaphore> imageAvailableSemaphores;
	std::vector<VkSemaphore> renderFinishedSemaphores;
	std::vector<VkFence> inFlightFences;
	// Initialization steps/constructors calls
	void createInstance();
	void createAllocator();
	void setupDebugMessenger();
	void pickPhysicalDevice();
	void createLogicalDevice();
	void createSurface(SDL_Window* window);
	void createSwapchain();
	void createImageViews();
	void createGraphicsPipeline();
	void createCommandPool();
	void createCommandBuffer();
	void createSyncObjects();
	void recordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex);
	void recreateSwapchain();

	// Helpers
	VkShaderModule createShaderModule(const std::vector<char>& code);
};

