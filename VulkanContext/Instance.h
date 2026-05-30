#pragma once
#include <Volk/volk.h>

class Instance
{
public:
	Instance();
	~Instance();
	Instance(const Instance&) = delete;
	Instance& operator = (const Instance&) = delete;

	VkInstance get() const { return instance; }

private:
	VkInstance instance = VK_NULL_HANDLE;
	VkDebugUtilsMessengerEXT debugMessenger = VK_NULL_HANDLE;

	void createInstance();
	void setupDebugMessenger();
};