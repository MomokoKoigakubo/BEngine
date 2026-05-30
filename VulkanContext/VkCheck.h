#pragma once														
#include <stdexcept>												
#include <string>													
#include <Volk/volk.h>
#include <vulkan/vk_enum_string_helper.h>

#define VK_CHECK(x)													\
do {																\
	VkResult err_ = (x);											\
	if (err_ != VK_SUCCESS)											\
	{																\
		throw std::runtime_error(std::string(#x) +					\
			" failed: VkResult=" + string_VkResult(err_)			\
			+ " at " __FILE__ ":" + std::to_string(__LINE__));		\
	}																\
} while (0)															
