#include "Texture.h"
#include "Buffer.h"
#include "VkCheck.h"
#include "third_party/stb_image.h"

Texture::Texture(Device& device, const char* path)
	: device(device)
{
	//decode to rgba pixels
	int w, h, channels;
	stbi_uc* pixels = stbi_load(path, &w, &h, &channels, STBI_rgb_alpha);
	VkDeviceSize imageSize = (VkDeviceSize)w * h * 4; //RGBA = 4 bytes a pixel

	//host stage buffer, copy pixels, free cpu copy
	Buffer staging(device.allocator(), imageSize, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
		VMA_MEMORY_USAGE_AUTO, VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT);
	staging.upload(pixels, imageSize);
	stbi_image_free(pixels);

	VkImageCreateInfo imageInfo{};
	imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
	imageInfo.imageType = VK_IMAGE_TYPE_2D;
	imageInfo.extent = { (uint32_t)w, (uint32_t)h, 1 };
	imageInfo.mipLevels = 1;
	imageInfo.arrayLayers = 1;
	imageInfo.format = VK_FORMAT_R8G8B8A8_SRGB;
	imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
	imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
	imageInfo.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
	imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
	imageInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;

	VmaAllocationCreateInfo allocInfo{};
	allocInfo.usage = VMA_MEMORY_USAGE_AUTO;
	VK_CHECK(vmaCreateImage(device.allocator(), &imageInfo, &allocInfo, &image, &allocation, nullptr));

    device.immediateSubmit([&](VkCommandBuffer cmd) {
        // 1. UNDEFINED -> TRANSFER_DST_OPTIMAL (ready to receive the copy)
        VkImageMemoryBarrier2 toDst{};
        toDst.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        toDst.srcStageMask = VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT;
        toDst.srcAccessMask = 0;
        toDst.dstStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
        toDst.dstAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
        toDst.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        toDst.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        toDst.image = image;
        toDst.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        toDst.subresourceRange.levelCount = 1;
        toDst.subresourceRange.layerCount = 1;

        VkDependencyInfo dep1{};
        dep1.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dep1.imageMemoryBarrierCount = 1;
        dep1.pImageMemoryBarriers = &toDst;
        vkCmdPipelineBarrier2(cmd, &dep1);

        // 2. copy staging buffer > image
        VkBufferImageCopy region{};
        region.bufferOffset = 0;
        region.bufferRowLength = 0;     // 0 = tightly packed
        region.bufferImageHeight = 0;
        region.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        region.imageSubresource.mipLevel = 0;
        region.imageSubresource.baseArrayLayer = 0;
        region.imageSubresource.layerCount = 1;
        region.imageOffset = { 0, 0, 0 };
        region.imageExtent = { (uint32_t)w, (uint32_t)h, 1 };
        vkCmdCopyBufferToImage(cmd, staging.get(), image,
            VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);

        // 3. TRANSFER_DST > SHADER_READ_ONLY_OPTIMAL (ready for the fragment shader)
        VkImageMemoryBarrier2 toRead{};
        toRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        toRead.srcStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
        toRead.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
        toRead.dstStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
        toRead.dstAccessMask = VK_ACCESS_2_SHADER_READ_BIT;
        toRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        toRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        toRead.image = image;
        toRead.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        toRead.subresourceRange.levelCount = 1;
        toRead.subresourceRange.layerCount = 1;

        VkDependencyInfo dep2{};
        dep2.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        dep2.imageMemoryBarrierCount = 1;
        dep2.pImageMemoryBarriers = &toRead;
        vkCmdPipelineBarrier2(cmd, &dep2);
        });

        // image view
        VkImageViewCreateInfo viewInfo{};
        viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        viewInfo.image = image;
        viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
        viewInfo.format = VK_FORMAT_R8G8B8A8_SRGB;
        viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        viewInfo.subresourceRange.levelCount = 1;
        viewInfo.subresourceRange.layerCount = 1;
        VK_CHECK(vkCreateImageView(device.get(), &viewInfo, nullptr, &view_));

        // sampler — how the shader reads texels
        VkSamplerCreateInfo samplerInfo{};
        samplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        samplerInfo.magFilter = VK_FILTER_NEAREST;     // crisp pixels (Blockbench/AC look)
        samplerInfo.minFilter = VK_FILTER_NEAREST;
        samplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        samplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        samplerInfo.minLod = 0.0f;
        samplerInfo.maxLod = 0.0f;
        samplerInfo.anisotropyEnable = VK_FALSE;
        samplerInfo.borderColor = VK_BORDER_COLOR_INT_OPAQUE_BLACK;
        VK_CHECK(vkCreateSampler(device.get(), &samplerInfo, nullptr, &sampler_));
}

Texture::~Texture()
{
    vkDestroySampler(device.get(), sampler_, nullptr);
    vkDestroyImageView(device.get(), view_, nullptr);
    vmaDestroyImage(device.allocator(), image, allocation);
}