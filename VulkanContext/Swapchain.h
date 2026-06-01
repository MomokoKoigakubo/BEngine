#pragma once
#include <Volk/volk.h>
#include <SDL3/SDL.h>
#include <vector>
#include "Device.h"
#include <vma/vk_mem_alloc.h>

// Owns the swapchain, its images + image views, and the chosen format/extent.
// Uses (but does not own) a Device. Rebuilds itself on resize via recreate().
class Swapchain
{
public:
	Swapchain(Device& device, VkSurfaceKHR surface, SDL_Window* window);
	~Swapchain();
	Swapchain(const Swapchain&) = delete;
	Swapchain& operator=(const Swapchain&) = delete;

	void recreate();

	VkExtent2D extent() const { return extent_; }
	VkFormat depthFormat() const { return depthFormat_; }
	VkFormat imageFormat() const { return imageFormat_; }
	VkImage depthImage() const { return depthImage_; }
	VkImageView depthView() const { return depthView_; }
	
	VkSwapchainKHR get() const { return swapchain; }

	uint32_t imageCount() const { return static_cast<uint32_t>(images_.size()); }
	const std::vector<VkImage>& images() const { return images_; }
	const std::vector<VkImageView>& imageViews() const { return imageViews_; }

private:
	Device& device;            // reference: used, not owned
	VkSurfaceKHR surface = VK_NULL_HANDLE;
	SDL_Window* window = nullptr;

	VkSwapchainKHR swapchain = VK_NULL_HANDLE;
	std::vector<VkImage> images_;
	std::vector<VkImageView> imageViews_;
	VmaAllocation depthAllocation_ = VK_NULL_HANDLE;
	VkExtent2D extent_{};
	VkFormat depthFormat_ = VK_FORMAT_D32_SFLOAT;
	VkImageView depthView_ = VK_NULL_HANDLE;
	VkFormat imageFormat_{};
	VkImage depthImage_ = VK_NULL_HANDLE;

	void createDepthResources();
	void createSwapchain();
	void createImageViews();
	void cleanup();   
};
