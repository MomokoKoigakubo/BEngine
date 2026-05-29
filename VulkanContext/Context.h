#pragma once
#include <Volk/volk.h>

class Context
{
public:
	Context();
	~Context();
	Context(const Context&) = delete;
	Context& operator = (const Context&) = delete;
private:
	VkInstance instance = VK_NULL_HANDLE;
	VkDebugUtilsMessengerEXT debugMessenger = VK_NULL_HANDLE;
	VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;

	void createInstance();
	void setupDebugMessenger();
	void pickPhysicalDevice();
};

