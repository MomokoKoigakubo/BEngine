#pragma once
#include <Volk/volk.h>
#include <SDL3/SDL.h>
#include <vector>
#include "Device.h"

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

	VkSwapchainKHR get() const { return swapchain; }
	VkFormat imageFormat() const { return imageFormat_; }
	VkExtent2D extent() const { return extent_; }
	const std::vector<VkImage>& images() const { return images_; }
	const std::vector<VkImageView>& imageViews() const { return imageViews_; }
	uint32_t imageCount() const { return static_cast<uint32_t>(images_.size()); }

private:
	Device& device;            // reference: used, not owned
	VkSurfaceKHR surface = VK_NULL_HANDLE;
	SDL_Window* window = nullptr;

	VkSwapchainKHR swapchain = VK_NULL_HANDLE;
	std::vector<VkImage> images_;
	std::vector<VkImageView> imageViews_;
	VkFormat imageFormat_{};
	VkExtent2D extent_{};

	void createSwapchain();
	void createImageViews();
	void cleanup();   // destroy image views + swapchain (used by recreate + destructor)
};
