#pragma once
#include <Volk/volk.h>
#include <SDL3/SDL.h>
#include <vector>

class Context
{
public:
	Context(SDL_Window* window);
	~Context();
	Context(const Context&) = delete;
	Context& operator = (const Context&) = delete;

private:
	VkInstance instance = VK_NULL_HANDLE;
	VkDebugUtilsMessengerEXT debugMessenger = VK_NULL_HANDLE;
	VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;
	VkQueue graphicsQueue = VK_NULL_HANDLE;
	VkDevice device = VK_NULL_HANDLE;
	uint32_t graphicsQueueFamilyIndex = UINT32_MAX;
	uint32_t presentQueueFamilyIndex = UINT32_MAX;
	VkQueue presentQueue = VK_NULL_HANDLE;
	VkSurfaceKHR surface = VK_NULL_HANDLE;
	VkSwapchainKHR swapchain = VK_NULL_HANDLE;
	std::vector<VkImage> swapchainImages;
	std::vector<VkImageView> swapchainImageViews;
	VkFormat swapchainImageFormat;
	VkExtent2D swapchainExtent;
	SDL_Window* window = nullptr;
	VkShaderModule createShaderModule(const std::vector<char>& code);
	VkPipelineLayout pipelineLayout = VK_NULL_HANDLE;
	VkPipeline graphicsPipeline = VK_NULL_HANDLE;
	void createInstance();
	void setupDebugMessenger();
	void pickPhysicalDevice();
	void createLogicalDevice();
	void createSurface(SDL_Window* window);
	void createSwapchain();
	void createImageViews();
	void createGraphicsPipeline();
};

