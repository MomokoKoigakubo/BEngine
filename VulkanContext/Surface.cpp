#include "Surface.h"
#include "VkCheck.h"
#include "SDL3/SDL_vulkan.h"
#include <stdexcept>

Surface::Surface(VkInstance instance, SDL_Window* window)
	: instance(instance)
{
	if (!SDL_Vulkan_CreateSurface(window, instance, nullptr, &surface))
	{
		throw std::runtime_error("Failed to create window surface!");
	}
}

Surface::~Surface()
{
	vkDestroySurfaceKHR(instance, surface, nullptr);
}
