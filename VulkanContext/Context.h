#pragma once
#include <Volk/volk.h> //volk needs to load first, vulkan loader
#include <vma/vk_mem_alloc.h>
#include <SDL3/SDL.h>
#include <vector>
#include "Instance.h"
#include "Surface.h"
#include "Device.h"
#include "Swapchain.h"
#include "Buffer.h"
#include "Vertex.h"

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
	// Core RAII-owned objects (declaration order = construction order, built in the init list)
	Instance instance;
	Surface surface;
	Device device;
	// Swapchain (RAII - owns swapchain, images, views, format, extent)
	Swapchain swapchain;
	Buffer vertexBuffer;
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
	void createGraphicsPipeline();
	void createCommandPool();
	void createCommandBuffer();
	void createSyncObjects();
	void recordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex);

	// Helpers
	VkShaderModule createShaderModule(const std::vector<char>& code);
};

