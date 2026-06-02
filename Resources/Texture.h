#pragma once
#include <Volk/volk.h>
#include <vma/vk_mem_alloc.h>
#include "Renderer/Device.h"


class Texture
{
public:
	Texture(Device& device, const char* path);
	~Texture();
	Texture(const Texture&) = delete;
	Texture& operator = (const Texture&) = delete;

	VkImageView view() const { return view_; }
	VkSampler sampler() const { return sampler_; }
	int width() const { return width_; }
	int height() const { return height_; }

private:
	Device& device;
	VkImage image = VK_NULL_HANDLE;
	VmaAllocation allocation = VK_NULL_HANDLE;
	VkImageView view_ = VK_NULL_HANDLE;
	VkSampler sampler_ = VK_NULL_HANDLE;
	int width_ = 0;
	int height_ = 0;
};