#pragma once
#include <Volk/volk.h> //volk needs to load first, vulkan loader
#include <vma/vk_mem_alloc.h>
#include <SDL3/SDL.h>
#include <vector>
#include <memory>
#include "Instance.h"
#include "Surface.h"
#include "Device.h"
#include "Swapchain.h"
#include "Buffer.h"
#include "Vertex.h"

class Scene;            // forward decls — drawFrame takes references, no include needed
class ResourceManager;

struct FlipbookParam
{
	float frameCount = 1.0f;
	float flipRate = 1.0f;
};

class Renderer
{
public:
	Renderer(SDL_Window* window);
	~Renderer();
	Renderer(const Renderer&) = delete;
	Renderer& operator = (const Renderer&) = delete;
	void drawFrame(Scene& scene, ResourceManager& resources);
	void waitIdle();   // block until the GPU is idle (call before tearing down resources)
	void setFrameBufferResized()
	{
		framebufferResized = true;
	}

	// Used by ResourceManager to create GPU objects + fill the bindless texture array.
	Device& getDevice() { return device; }
	uint32_t registerBindlessTexture(VkImageView view, VkSampler sampler);
	void setFlipbook(uint32_t slot, float frameCount, float flipRate);

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

	// Pipeline
	VkPipelineLayout pipelineLayout = VK_NULL_HANDLE;
	VkDescriptorSetLayout descriptorSetLayout = VK_NULL_HANDLE;
	VkDescriptorPool descriptorPool = VK_NULL_HANDLE;
	VkDescriptorSet  descriptorSet  = VK_NULL_HANDLE;   
	VkPipeline graphicsPipeline = VK_NULL_HANDLE;
	//command buffers
	uint32_t currentFrame = 0;
	uint32_t maxTextures = 0; //clamp bindless array size -> may need to still do an atlas fallback eventually for older pcs
	uint32_t nextTextureSlot = 0; //next free bindless slot
	VkCommandPool commandPool = VK_NULL_HANDLE;
	std::vector<VkCommandBuffer> commandBuffers;
	std::vector<VkSemaphore> imageAvailableSemaphores;
	std::vector<VkSemaphore> renderFinishedSemaphores;
	std::vector<VkFence> inFlightFences;

	// Flipbook params: static per-texture {frameCount, flipRate}, indexed by texIndex.
	std::unique_ptr<Buffer>    flipbookBuffer;   // storage buffer the shader reads
	std::vector<FlipbookParam> flipbookParams;   // CPU mirror, re-uploaded on change

	// Initialization steps/constructors calls
	void createDescriptorSetLayout();
	void createPipelineLayout();
	void createGraphicsPipeline();
	void createCommandPool();
	void createCommandBuffer();
	void createSyncObjects();
	void createDescriptorSet();
	void recordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex, Scene& scene, ResourceManager& resources);

	// Helpers
	VkShaderModule createShaderModule(const std::vector<char>& code);
};

