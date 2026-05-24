// IdleL.cpp : Defines the entry point for the application.
//

#define VOLK_IMPLEMENTATION
#define GLM_FORCE_RADIANS
#define GLM_FORCE_DEPTH_ZERO_TO_ONE
#define VMA_IMPLEMENTATION
  

#include "IdleL.h"
#include <vulkan/vulkan.h>
#include <volk/volk.h>
#include <SDL3/SDL.h>
#include <SDL3/SDL_vulkan.h>
#include <iostream>
#include <vector>
#include <array>
#include <string>
#include <filesystem>
#include <vma/vk_mem_alloc.h>
#include <glm/glm.hpp>
#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtc/quaternion.hpp>
#include "slang/slang.h"
#include "slang/slang-com-ptr.h"


//vulkan 
#define chk(x)													   \
    do {                                                           \
        VkResult err = x;                                          \
        if (err != VK_SUCCESS) {                                   \
            std::cerr << "Vulkan error: " << err << std::endl;     \
            std::abort();                                          \
        }                                                          \
    } while (0)



using namespace std;

int main(int argc, char** argv)
{
	chk(volkInitialize());
	VkInstance instance{};

	VkApplicationInfo appInfo{
	.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO,
	.pApplicationName = "BEngine",
	.apiVersion = VK_API_VERSION_1_3
	};

	uint32_t instanceExtensionsCount{ 0 };
	if(SDL_Init(SDL_INIT_VIDEO) == false) {
		std::cerr << "SDL_Init Error: " << SDL_GetError() << std::endl;
		std::abort();
	}
	char const* const* instanceExtensions{ SDL_Vulkan_GetInstanceExtensions(&instanceExtensionsCount) };



	VkInstanceCreateInfo instanceCI{
	.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,
	.pApplicationInfo = &appInfo,
	.enabledExtensionCount = instanceExtensionsCount,
	.ppEnabledExtensionNames = instanceExtensions,
	};

	chk(vkCreateInstance(&instanceCI, nullptr, &instance));
	volkLoadInstance(instance);



	uint32_t deviceCount{ 0 };
	chk(vkEnumeratePhysicalDevices(instance, &deviceCount, nullptr));
	std::vector<VkPhysicalDevice> devices(deviceCount);
	chk(vkEnumeratePhysicalDevices(instance, &deviceCount, devices.data()));

	uint32_t deviceIndex{ 0 };
	if (argc > 1) {
		deviceIndex = std::stoi(argv[1]);
		assert(deviceIndex < deviceCount);
	}

	VkPhysicalDeviceProperties2 deviceProperties{ .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2 };
	vkGetPhysicalDeviceProperties2(devices[deviceIndex], &deviceProperties);
	std::cout << "Selected device: " << deviceProperties.properties.deviceName << "\n";
}
