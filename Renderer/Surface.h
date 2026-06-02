#pragma once
#include <Volk/volk.h>
#include <SDL3/SDL.h>

// Owns the VkSurfaceKHR (the bridge between Vulkan and the SDL window).
// Instance-level object: created from the VkInstance, destroyed before it.
class Surface
{
public:
	Surface(VkInstance instance, SDL_Window* window);
	~Surface();
	Surface(const Surface&) = delete;
	Surface& operator=(const Surface&) = delete;

	VkSurfaceKHR get() const { return surface; }

private:
	VkInstance instance = VK_NULL_HANDLE;   // kept so the destructor can free the surface
	VkSurfaceKHR surface = VK_NULL_HANDLE;
};
