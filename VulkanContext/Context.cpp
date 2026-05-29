#include "Context.h"
#include "SDL3/SDL_vulkan.h"
#include <cstdlib>
#include <iostream>

void initVulkan() {
    
}

void Context::createInstance()
{
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
	//extensions
	uint32_t countInstanceExtensions{ 0 };
	const char* const* instance_extensions = SDL_Vulkan_GetInstanceExtensions(&countInstanceExtensions);
	createInfo.ppEnabledExtensionNames = instance_extensions;
	createInfo.enabledExtensionCount = countInstanceExtensions;

	if (countInstanceExtensions == NULL) {std::abort();}

	VkResult result = vkCreateInstance(&createInfo, nullptr, &instance);
	if (result != VK_SUCCESS) { std::abort(); }

}

Context::Context() {
	VkResult result = volkInitialize();
	if (result != VK_SUCCESS)
	{
		std::cout << "Vulkan Error: " << result << std::endl;
		std::abort();
	}
	createInstance();
	volkLoadInstance(instance);
}

Context::~Context() {
	vkDestroyInstance(instance, nullptr);
}