#include "Texture.h"
#include "Renderer/Buffer.h"
#include "Renderer/VkCheck.h"
#include "util/third_party/stb_image.h"
#include <algorithm>
#include <cmath>

Texture::Texture(Device& device, const char* path)
	: device(device)
{
	//decode to rgba pixels
	int w, h, channels;
	stbi_uc* pixels = stbi_load(path, &w, &h, &channels, STBI_rgb_alpha);
	VkDeviceSize imageSize = (VkDeviceSize)w * h * 4; //RGBA = 4 bytes a pixel
	uint32_t mipLevels = (uint32_t)std::floor(std::log2(std::max(w, h))) + 1; //full mip chain

    width_ = w;
    height_ = h;

	//host stage buffer, copy pixels, free cpu copy
	Buffer staging(device.allocator(), imageSize, VK_BUFFER_USAGE_TRANSFER_SRC_BIT,
		VMA_MEMORY_USAGE_AUTO, VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT);
	staging.upload(pixels, imageSize);
	stbi_image_free(pixels);

	VkImageCreateInfo imageInfo{};
	imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
	imageInfo.imageType = VK_IMAGE_TYPE_2D;
	imageInfo.extent = { (uint32_t)w, (uint32_t)h, 1 };
	imageInfo.mipLevels = mipLevels;
	imageInfo.arrayLayers = 1;
	imageInfo.format = VK_FORMAT_R8G8B8A8_SRGB;
	imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
	imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
	imageInfo.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
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
        toDst.subresourceRange.levelCount = mipLevels;
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

        // 3. generate mips: blit each level down into the next, moving finished levels to SHADER_READ
        int32_t mipW = w, mipH = h;
        for (uint32_t i = 1; i < mipLevels; i++)
        {
            // level i-1: TRANSFER_DST -> TRANSFER_SRC (it's the blit source for level i)
            VkImageMemoryBarrier2 toSrc{};
            toSrc.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
            toSrc.srcStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
            toSrc.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
            toSrc.dstStageMask = VK_PIPELINE_STAGE_2_BLIT_BIT;
            toSrc.dstAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;
            toSrc.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            toSrc.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
            toSrc.image = image;
            toSrc.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            toSrc.subresourceRange.baseMipLevel = i - 1;
            toSrc.subresourceRange.levelCount = 1;
            toSrc.subresourceRange.layerCount = 1;

            VkDependencyInfo depSrc{};
            depSrc.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
            depSrc.imageMemoryBarrierCount = 1;
            depSrc.pImageMemoryBarriers = &toSrc;
            vkCmdPipelineBarrier2(cmd, &depSrc);

            // blit level i-1 (full) -> level i (half), linear filter
            VkImageBlit blit{};
            blit.srcSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            blit.srcSubresource.mipLevel = i - 1;
            blit.srcSubresource.baseArrayLayer = 0;
            blit.srcSubresource.layerCount = 1;
            blit.srcOffsets[0] = { 0, 0, 0 };
            blit.srcOffsets[1] = { mipW, mipH, 1 };
            blit.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            blit.dstSubresource.mipLevel = i;
            blit.dstSubresource.baseArrayLayer = 0;
            blit.dstSubresource.layerCount = 1;
            blit.dstOffsets[0] = { 0, 0, 0 };
            blit.dstOffsets[1] = { mipW > 1 ? mipW / 2 : 1, mipH > 1 ? mipH / 2 : 1, 1 };
            vkCmdBlitImage(cmd, image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &blit, VK_FILTER_LINEAR);

            // level i-1 done -> SHADER_READ
            VkImageMemoryBarrier2 toRead{};
            toRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
            toRead.srcStageMask = VK_PIPELINE_STAGE_2_BLIT_BIT;
            toRead.srcAccessMask = VK_ACCESS_2_TRANSFER_READ_BIT;
            toRead.dstStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
            toRead.dstAccessMask = VK_ACCESS_2_SHADER_READ_BIT;
            toRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
            toRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            toRead.image = image;
            toRead.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            toRead.subresourceRange.baseMipLevel = i - 1;
            toRead.subresourceRange.levelCount = 1;
            toRead.subresourceRange.layerCount = 1;

            VkDependencyInfo depRead{};
            depRead.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
            depRead.imageMemoryBarrierCount = 1;
            depRead.pImageMemoryBarriers = &toRead;
            vkCmdPipelineBarrier2(cmd, &depRead);

            if (mipW > 1) mipW /= 2;
            if (mipH > 1) mipH /= 2;
        }

        // last level is still TRANSFER_DST -> SHADER_READ
        VkImageMemoryBarrier2 lastRead{};
        lastRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
        lastRead.srcStageMask = VK_PIPELINE_STAGE_2_COPY_BIT;
        lastRead.srcAccessMask = VK_ACCESS_2_TRANSFER_WRITE_BIT;
        lastRead.dstStageMask = VK_PIPELINE_STAGE_2_FRAGMENT_SHADER_BIT;
        lastRead.dstAccessMask = VK_ACCESS_2_SHADER_READ_BIT;
        lastRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        lastRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        lastRead.image = image;
        lastRead.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        lastRead.subresourceRange.baseMipLevel = mipLevels - 1;
        lastRead.subresourceRange.levelCount = 1;
        lastRead.subresourceRange.layerCount = 1;

        VkDependencyInfo depLast{};
        depLast.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
        depLast.imageMemoryBarrierCount = 1;
        depLast.pImageMemoryBarriers = &lastRead;
        vkCmdPipelineBarrier2(cmd, &depLast);
        });

        // image view
        VkImageViewCreateInfo viewInfo{};
        viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        viewInfo.image = image;
        viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
        viewInfo.format = VK_FORMAT_R8G8B8A8_SRGB;
        viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        viewInfo.subresourceRange.levelCount = mipLevels;
        viewInfo.subresourceRange.layerCount = 1;
        VK_CHECK(vkCreateImageView(device.get(), &viewInfo, nullptr, &view_));

        // sampler � how the shader reads texels
        VkSamplerCreateInfo samplerInfo{};
        samplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        samplerInfo.magFilter = VK_FILTER_NEAREST;     // crisp pixels (Blockbench/AC look)
        samplerInfo.minFilter = VK_FILTER_NEAREST;
        samplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        samplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_REPEAT;
        samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;   // trilinear between mips (cuts distant shimmer)
        samplerInfo.minLod = 0.0f;
        samplerInfo.maxLod = (float)mipLevels;
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