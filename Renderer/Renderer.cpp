#include "util/fileIO.h"
#include "Renderer.h"
#include "Scene/Scene.h"
#include "Resources/ResourceManager.h"
#include "VkCheck.h"
#include "SDL3/SDL_vulkan.h"
#include <cstdlib>
#include <iostream>
#include <vector>
#include <optional>
#include <set>
#include <string>
#include <limits>
#include <algorithm>
#include <fstream>
#include <memory>

namespace {
	// constants
	constexpr uint32_t MAX_FRAMES_IN_FLIGHT = 2;

	struct PushConstants
	{
		glm::mat4 mvp;
		float time; //seconds > shader derives the flipbook from from here
	};
}

Renderer::Renderer(SDL_Window* window)
	: surface(instance.get(), window),
	device(instance.get(), surface.get()),
	swapchain(device, surface.get(), window)
{
	this->window = window;
	createCommandPool();

	maxTextures = std::min(4096u, device.maxBindlessTextures());

	flipbookParams.assign(maxTextures, FlipbookParam{});
	flipbookBuffer = std::make_unique<Buffer>(
		device.allocator(),
		sizeof(FlipbookParam) * maxTextures,
		VK_BUFFER_USAGE_STORAGE_BUFFER_BIT,
		VMA_MEMORY_USAGE_AUTO,
		VMA_ALLOCATION_CREATE_HOST_ACCESS_SEQUENTIAL_WRITE_BIT);

	flipbookBuffer->upload(flipbookParams.data(), sizeof(FlipbookParam) * maxTextures);

	createDescriptorSetLayout();
	createPipelineLayout();
	createGraphicsPipeline();
	createCommandBuffer();
	createSyncObjects();

	createDescriptorSet();   // pool + empty bindless set; ResourceManager fills the slots
}

Renderer::~Renderer()
{
	vkDeviceWaitIdle(device.get());
	for (auto semaphore : renderFinishedSemaphores)
	{
		vkDestroySemaphore(device.get(), semaphore, nullptr);
	}

	for (auto semaphore : imageAvailableSemaphores)
	{
		vkDestroySemaphore(device.get(), semaphore, nullptr);
	}

	for (auto fence : inFlightFences)
	{
		vkDestroyFence(device.get(), fence, nullptr);
	}

	vkDestroyCommandPool(device.get(), commandPool, nullptr);
	vkDestroyPipeline(device.get(), graphicsPipeline, nullptr);
	vkDestroyPipelineLayout(device.get(), pipelineLayout, nullptr);
	vkDestroyDescriptorPool(device.get(), descriptorPool, nullptr);
	vkDestroyDescriptorSetLayout(device.get(), descriptorSetLayout, nullptr);
	// swapchain, device, surface, instance members destroy themselves (reverse order) after this body.
}

VkShaderModule Renderer::createShaderModule(const std::vector<char>& code)
{
	VkShaderModuleCreateInfo createInfo{};
	createInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
	createInfo.codeSize = code.size();
	createInfo.pCode = reinterpret_cast<const uint32_t*>(code.data());

	VkShaderModule shaderModule;
	VK_CHECK(vkCreateShaderModule(device.get(),&createInfo, nullptr, &shaderModule));
	return shaderModule;
}

void Renderer::createDescriptorSetLayout()
{
	// bindless: binding 0 = array of combined image samplers (partially bound + update-after-bind)
	VkDescriptorSetLayoutBinding samplerBinding{};
	samplerBinding.binding = 0;
	samplerBinding.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
	samplerBinding.descriptorCount = maxTextures;
	samplerBinding.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
	samplerBinding.pImmutableSamplers = nullptr;

	VkDescriptorSetLayoutBinding flipbookBinding{};
	flipbookBinding.binding = 1;
	flipbookBinding.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
	flipbookBinding.descriptorCount = 1;
	flipbookBinding.stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;

	VkDescriptorSetLayoutBinding bindings[2] = { samplerBinding, flipbookBinding };

	VkDescriptorBindingFlags flags[2] =
	{
		VK_DESCRIPTOR_BINDING_PARTIALLY_BOUND_BIT | VK_DESCRIPTOR_BINDING_UPDATE_AFTER_BIND_BIT,
		0
	};


	VkDescriptorSetLayoutBindingFlagsCreateInfo bindingFlagsInfo{};
	bindingFlagsInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_BINDING_FLAGS_CREATE_INFO;
	bindingFlagsInfo.bindingCount = 2;
	bindingFlagsInfo.pBindingFlags = flags;


	VkDescriptorSetLayoutCreateInfo layoutInfo{};
	layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
	layoutInfo.bindingCount = 2;
	layoutInfo.pBindings = bindings;
	layoutInfo.flags = VK_DESCRIPTOR_SET_LAYOUT_CREATE_UPDATE_AFTER_BIND_POOL_BIT;
	layoutInfo.pNext = &bindingFlagsInfo;
	VK_CHECK(vkCreateDescriptorSetLayout(device.get(), &layoutInfo, nullptr, &descriptorSetLayout));
}

void Renderer::createPipelineLayout()
{
	VkPushConstantRange pushConstantRange{};
	pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;  // frag reads flipbook
	pushConstantRange.offset = 0;
	pushConstantRange.size = sizeof(PushConstants);

	VkPipelineLayoutCreateInfo pipelineLayoutInfo{};
	pipelineLayoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
	pipelineLayoutInfo.setLayoutCount = 1;
	pipelineLayoutInfo.pSetLayouts = &descriptorSetLayout;
	pipelineLayoutInfo.pushConstantRangeCount = 1;
	pipelineLayoutInfo.pPushConstantRanges = &pushConstantRange;
	VK_CHECK(vkCreatePipelineLayout(device.get(), &pipelineLayoutInfo, nullptr, &pipelineLayout));
}

void Renderer::createGraphicsPipeline()
{
	auto shaderCode = fileio::readBinary(SHADER_DIR "/triangle.spv");
	VkShaderModule shaderModule = createShaderModule(shaderCode);

	VkPipelineShaderStageCreateInfo vertStageInfo{};
	vertStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	vertStageInfo.stage = VK_SHADER_STAGE_VERTEX_BIT;
	vertStageInfo.module = shaderModule;
	vertStageInfo.pName = "vertMain";  //slang entry point

	VkPipelineShaderStageCreateInfo fragStageInfo{};
	fragStageInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
	fragStageInfo.stage = VK_SHADER_STAGE_FRAGMENT_BIT;
	fragStageInfo.module = shaderModule;
	fragStageInfo.pName = "fragMain";  //slang frag entry point

	VkPipelineShaderStageCreateInfo shaderStages[] = { vertStageInfo, fragStageInfo };

	auto bindingDescription = Vertex::getBindingDesc();
	auto attributeDescriptions = Vertex::getAttributeDescriptions();

	VkPipelineVertexInputStateCreateInfo vertexInputInfo{};
	vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
	vertexInputInfo.vertexBindingDescriptionCount = 1;
	vertexInputInfo.pVertexBindingDescriptions = &bindingDescription;
	vertexInputInfo.vertexAttributeDescriptionCount = static_cast<uint32_t>(attributeDescriptions.size());
	vertexInputInfo.pVertexAttributeDescriptions = attributeDescriptions.data();

	VkPipelineInputAssemblyStateCreateInfo inputAssembly{};
	inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
	inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
	inputAssembly.primitiveRestartEnable = VK_FALSE;

	std::vector<VkDynamicState> dynamicStates = {
		VK_DYNAMIC_STATE_VIEWPORT,
		VK_DYNAMIC_STATE_SCISSOR
	};

	VkPipelineDynamicStateCreateInfo dynamicState{};
	dynamicState.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
	dynamicState.dynamicStateCount = static_cast<uint32_t>(dynamicStates.size());
	dynamicState.pDynamicStates = dynamicStates.data();

	VkPipelineViewportStateCreateInfo viewportState{};
	viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
	viewportState.viewportCount = 1;
	viewportState.scissorCount = 1;

	VkPipelineRasterizationStateCreateInfo rasterizer{};
	rasterizer.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
	rasterizer.depthClampEnable = VK_FALSE;
	rasterizer.rasterizerDiscardEnable = VK_FALSE;
	rasterizer.polygonMode = VK_POLYGON_MODE_FILL;
	rasterizer.lineWidth = 1.0f;
	rasterizer.cullMode = VK_CULL_MODE_BACK_BIT;
	rasterizer.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
	rasterizer.depthBiasEnable = VK_FALSE;

	VkPipelineMultisampleStateCreateInfo multisampling{};
	multisampling.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
	multisampling.sampleShadingEnable = VK_FALSE;
	multisampling.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

	VkPipelineColorBlendAttachmentState colorBlendAttachment{};
	colorBlendAttachment.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT |
		VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
	colorBlendAttachment.blendEnable = VK_FALSE;

	VkPipelineColorBlendStateCreateInfo colorBlending{};
	colorBlending.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
	colorBlending.logicOpEnable = VK_FALSE;
	colorBlending.attachmentCount = 1;
	colorBlending.pAttachments = &colorBlendAttachment;

	VkPipelineRenderingCreateInfo renderingInfo{};
	renderingInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO;
	renderingInfo.colorAttachmentCount = 1;
	VkFormat colorFormat = swapchain.imageFormat();
	renderingInfo.pColorAttachmentFormats = &colorFormat;
	renderingInfo.depthAttachmentFormat = swapchain.depthFormat();

	VkPipelineDepthStencilStateCreateInfo depthStencil{};
	depthStencil.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
	depthStencil.depthTestEnable = VK_TRUE;
	depthStencil.depthWriteEnable = VK_TRUE;
	depthStencil.depthCompareOp = VK_COMPARE_OP_LESS;
	depthStencil.depthBoundsTestEnable = VK_FALSE;
	depthStencil.stencilTestEnable = VK_FALSE;

	VkGraphicsPipelineCreateInfo pipelineInfo{};
	pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
	pipelineInfo.pNext = &renderingInfo;
	pipelineInfo.stageCount = 2;
	pipelineInfo.pStages = shaderStages;
	pipelineInfo.pInputAssemblyState = &inputAssembly;
	pipelineInfo.pVertexInputState = &vertexInputInfo;
	pipelineInfo.pViewportState = &viewportState;
	pipelineInfo.pRasterizationState = &rasterizer;
	pipelineInfo.pMultisampleState = &multisampling;
	pipelineInfo.pDepthStencilState = &depthStencil;
	pipelineInfo.pDynamicState = &dynamicState;
	pipelineInfo.pColorBlendState = &colorBlending;
	pipelineInfo.layout = pipelineLayout;
	pipelineInfo.renderPass = VK_NULL_HANDLE;
	pipelineInfo.subpass = 0;

	VK_CHECK(vkCreateGraphicsPipelines(device.get(),VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &graphicsPipeline));
	vkDestroyShaderModule(device.get(),shaderModule, nullptr);
}

void Renderer::createCommandPool()
{
	VkCommandPoolCreateInfo poolInfo{};
	poolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
	poolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
	poolInfo.queueFamilyIndex = device.graphicsFamily();

	VK_CHECK(vkCreateCommandPool(device.get(),&poolInfo, nullptr, &commandPool));
}

void Renderer::createCommandBuffer()
{
	commandBuffers.resize(MAX_FRAMES_IN_FLIGHT);

	VkCommandBufferAllocateInfo allocInfo{};
	allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
	allocInfo.commandPool = commandPool;
	allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
	allocInfo.commandBufferCount = MAX_FRAMES_IN_FLIGHT;

	VK_CHECK(vkAllocateCommandBuffers(device.get(),&allocInfo, commandBuffers.data()));
}

void Renderer::createSyncObjects()
{
	VkSemaphoreCreateInfo semaphoreInfo{};
	semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

	VkFenceCreateInfo fenceInfo{};
	fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
	fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT;

	imageAvailableSemaphores.resize(MAX_FRAMES_IN_FLIGHT);
	inFlightFences.resize(MAX_FRAMES_IN_FLIGHT);
	for (size_t i = 0; i < MAX_FRAMES_IN_FLIGHT; i++)
	{
		VK_CHECK(vkCreateSemaphore(device.get(),&semaphoreInfo, nullptr, &imageAvailableSemaphores[i]));
		VK_CHECK(vkCreateFence(device.get(),&fenceInfo, nullptr, &inFlightFences[i]));
	}

	renderFinishedSemaphores.resize(swapchain.imageCount());
	for (size_t i = 0; i < swapchain.imageCount(); i++)
	{
		VK_CHECK(vkCreateSemaphore(device.get(),&semaphoreInfo, nullptr, &renderFinishedSemaphores[i]));
	}
}

void Renderer::createDescriptorSet()
{
	// pool — one combined-image-sampler array (bindless) + one storage buffer (flipbook params)
	VkDescriptorPoolSize poolSizes[2]{};
	poolSizes[0].type = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
	poolSizes[0].descriptorCount = maxTextures;
	poolSizes[1].type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
	poolSizes[1].descriptorCount = 1;

	VkDescriptorPoolCreateInfo poolInfo{};
	poolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
	poolInfo.poolSizeCount = 2;
	poolInfo.pPoolSizes = poolSizes;
	poolInfo.flags = VK_DESCRIPTOR_POOL_CREATE_UPDATE_AFTER_BIND_BIT;
	poolInfo.maxSets = 1;
	VK_CHECK(vkCreateDescriptorPool(device.get(), &poolInfo, nullptr, &descriptorPool));

	// allocate one set with our layout
	VkDescriptorSetAllocateInfo allocInfo{};
	allocInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
	allocInfo.descriptorPool = descriptorPool;
	allocInfo.descriptorSetCount = 1;
	allocInfo.pSetLayouts = &descriptorSetLayout;
	VK_CHECK(vkAllocateDescriptorSets(device.get(), &allocInfo, &descriptorSet));

	// binding 1 -> the flipbook params buffer (written once; the handle never changes,
	// only its contents via upload(), so no update-after-bind needed here)
	VkDescriptorBufferInfo bufInfo{};
	bufInfo.buffer = flipbookBuffer->get();
	bufInfo.offset = 0;
	bufInfo.range  = VK_WHOLE_SIZE;

	VkWriteDescriptorSet flipbookWrite{};
	flipbookWrite.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
	flipbookWrite.dstSet = descriptorSet;
	flipbookWrite.dstBinding = 1;
	flipbookWrite.dstArrayElement = 0;
	flipbookWrite.descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
	flipbookWrite.descriptorCount = 1;
	flipbookWrite.pBufferInfo = &bufInfo;
	vkUpdateDescriptorSets(device.get(), 1, &flipbookWrite, 0, nullptr);
}

uint32_t Renderer::registerBindlessTexture(VkImageView view, VkSampler sampler)
{
	uint32_t slot = nextTextureSlot++;

	VkDescriptorImageInfo imageInfo{};
	imageInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
	imageInfo.imageView = view;
	imageInfo.sampler = sampler;

	VkWriteDescriptorSet write{};
	write.sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
	write.dstSet = descriptorSet;
	write.dstBinding = 0;
	write.dstArrayElement = slot;
	write.descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
	write.descriptorCount = 1;
	write.pImageInfo = &imageInfo;
	vkUpdateDescriptorSets(device.get(), 1, &write, 0, nullptr);

	return slot;
}

void Renderer::setFlipbook(uint32_t slot, float frameCount, float flipRate)
{
	flipbookParams[slot] = { frameCount, flipRate };
	flipbookBuffer->upload(flipbookParams.data(),
		sizeof(FlipbookParam) * flipbookParams.size());
}

void Renderer::recordCommandBuffer(VkCommandBuffer commandBuffer, uint32_t imageIndex, Scene& scene, ResourceManager& resources)
{
	VkCommandBufferBeginInfo beginInfo{};
	beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
	VK_CHECK(vkBeginCommandBuffer(commandBuffer, &beginInfo));

	VkImageMemoryBarrier2 toColorBarrier{};
	toColorBarrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
	toColorBarrier.srcStageMask = VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT;
	toColorBarrier.srcAccessMask = 0;
	toColorBarrier.dstStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
	toColorBarrier.dstAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
	toColorBarrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
	toColorBarrier.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
	toColorBarrier.image = swapchain.images()[imageIndex];
	toColorBarrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
	toColorBarrier.subresourceRange.baseMipLevel = 0;
	toColorBarrier.subresourceRange.levelCount = 1;
	toColorBarrier.subresourceRange.baseArrayLayer = 0;
	toColorBarrier.subresourceRange.layerCount = 1;

	VkImageMemoryBarrier2 toDepthBarrier{};
	toDepthBarrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
	toDepthBarrier.srcStageMask = VK_PIPELINE_STAGE_2_TOP_OF_PIPE_BIT;
	toDepthBarrier.srcAccessMask = 0;
	toDepthBarrier.dstStageMask = VK_PIPELINE_STAGE_2_EARLY_FRAGMENT_TESTS_BIT | VK_PIPELINE_STAGE_2_LATE_FRAGMENT_TESTS_BIT;
	toDepthBarrier.dstAccessMask = VK_ACCESS_2_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
	toDepthBarrier.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED;
	toDepthBarrier.newLayout = VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;
	toDepthBarrier.image = swapchain.depthImage();
	toDepthBarrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_DEPTH_BIT;
	toDepthBarrier.subresourceRange.baseMipLevel = 0;
	toDepthBarrier.subresourceRange.levelCount = 1;
	toDepthBarrier.subresourceRange.baseArrayLayer = 0;
	toDepthBarrier.subresourceRange.layerCount = 1;

	VkImageMemoryBarrier2 barriers[2] = { toColorBarrier, toDepthBarrier };

	VkDependencyInfo dependencyInfo{};
	dependencyInfo.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
	dependencyInfo.imageMemoryBarrierCount = 2;
	dependencyInfo.pImageMemoryBarriers = barriers;

	vkCmdPipelineBarrier2(commandBuffer, &dependencyInfo);

	VkRenderingAttachmentInfo colorAttachment{};
	colorAttachment.sType = VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
	colorAttachment.imageView = swapchain.imageViews()[imageIndex];
	colorAttachment.imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
	colorAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
	colorAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
	colorAttachment.clearValue.color = { 0.0f, 0.0f, 0.0f, 1.0f };

	VkRenderingAttachmentInfo depthAttachment{};
	depthAttachment.sType = VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO;
	depthAttachment.imageView = swapchain.depthView();
	depthAttachment.imageLayout = VK_IMAGE_LAYOUT_DEPTH_ATTACHMENT_OPTIMAL;
	depthAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;
	depthAttachment.storeOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
	depthAttachment.clearValue.depthStencil = { 1.0f, 0 };

	VkRenderingInfo renderingInfo{};
	renderingInfo.sType = VK_STRUCTURE_TYPE_RENDERING_INFO;
	renderingInfo.renderArea.offset = { 0,0 };
	renderingInfo.renderArea.extent = swapchain.extent();
	renderingInfo.layerCount = 1;
	renderingInfo.colorAttachmentCount = 1;
	renderingInfo.pColorAttachments = &colorAttachment;
	renderingInfo.pDepthAttachment = &depthAttachment;

	vkCmdBeginRendering(commandBuffer, &renderingInfo);
	vkCmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, graphicsPipeline);

	vkCmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS,
		pipelineLayout, 0, 1, &descriptorSet, 0, nullptr);

	VkViewport viewport{};
	viewport.x = 0.0f;
	viewport.y = 0.0f;
	viewport.width = (float)swapchain.extent().width;
	viewport.height = (float)swapchain.extent().height;
	viewport.minDepth = 0.0f;
	viewport.maxDepth = 1.0f;
	vkCmdSetViewport(commandBuffer, 0, 1, &viewport);

	VkRect2D scissor{};
	scissor.offset = { 0, 0, };
	scissor.extent = swapchain.extent();
	vkCmdSetScissor(commandBuffer, 0, 1, &scissor);

	float aspect = swapchain.extent().width / (float)swapchain.extent().height;
	glm::mat4 view = scene.camera.viewMatrix();
	glm::mat4 proj = scene.camera.projectionMatrix(aspect);

	for (const Renderable& r : scene.getRenderables())
	{
		PushConstants pc{};
		pc.mvp = proj * view * r.transform;
		pc.time = scene.time();
		vkCmdPushConstants(commandBuffer, pipelineLayout,
			VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(pc), &pc);
		resources.getMesh(r.mesh).draw(commandBuffer);
	}

	vkCmdEndRendering(commandBuffer);

	VkImageMemoryBarrier2 toPresentBarrier{};
	toPresentBarrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER_2;
	toPresentBarrier.srcStageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
	toPresentBarrier.srcAccessMask = VK_ACCESS_2_COLOR_ATTACHMENT_WRITE_BIT;
	toPresentBarrier.dstStageMask = VK_PIPELINE_STAGE_2_BOTTOM_OF_PIPE_BIT;
	toPresentBarrier.dstAccessMask = 0;
	toPresentBarrier.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
	toPresentBarrier.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
	toPresentBarrier.image = swapchain.images()[imageIndex];
	toPresentBarrier.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
	toPresentBarrier.subresourceRange.baseMipLevel = 0;
	toPresentBarrier.subresourceRange.levelCount = 1;
	toPresentBarrier.subresourceRange.baseArrayLayer = 0;
	toPresentBarrier.subresourceRange.layerCount = 1;

	VkDependencyInfo presentDependency{};
	presentDependency.sType = VK_STRUCTURE_TYPE_DEPENDENCY_INFO;
	presentDependency.imageMemoryBarrierCount = 1;
	presentDependency.pImageMemoryBarriers = &toPresentBarrier;

	vkCmdPipelineBarrier2(commandBuffer, &presentDependency);

	VK_CHECK(vkEndCommandBuffer(commandBuffer));
}

void Renderer::drawFrame(Scene& scene, ResourceManager& resources)
{

	int w = 0, h = 0;
	SDL_GetWindowSizeInPixels(window, &w, &h);
	if (w == 0 || h == 0 || (SDL_GetWindowFlags(window) & SDL_WINDOW_MINIMIZED)) return;

	vkWaitForFences(device.get(),1, &inFlightFences[currentFrame], VK_TRUE, UINT64_MAX);

	// Recreate at the TOP (before acquiring) so THIS frame renders at the current
	// window size - avoids the one-frame size lag the compositor stretches (smear).
	if (framebufferResized)
	{
		framebufferResized = false;
		swapchain.recreate();
	}

	uint32_t imageIndex;
	VkResult acquiredResult = vkAcquireNextImageKHR(device.get(), swapchain.get(), UINT64_MAX,
		imageAvailableSemaphores[currentFrame], VK_NULL_HANDLE, &imageIndex);

	if (acquiredResult == VK_ERROR_OUT_OF_DATE_KHR)
	{
		swapchain.recreate();
		return;
	}

	vkResetFences(device.get(),1, &inFlightFences[currentFrame]);

	vkResetCommandBuffer(commandBuffers[currentFrame], 0);
	recordCommandBuffer(commandBuffers[currentFrame], imageIndex, scene, resources);

	VkSemaphoreSubmitInfo waitInfo{};
	waitInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
	waitInfo.semaphore = imageAvailableSemaphores[currentFrame];
	waitInfo.stageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;
	
	VkSemaphoreSubmitInfo signalInfo{};
	signalInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_SUBMIT_INFO;
	signalInfo.semaphore = renderFinishedSemaphores[imageIndex];
	signalInfo.stageMask = VK_PIPELINE_STAGE_2_COLOR_ATTACHMENT_OUTPUT_BIT;

	VkCommandBufferSubmitInfo cmdBufferInfo{};
	cmdBufferInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_SUBMIT_INFO;
	cmdBufferInfo.commandBuffer = commandBuffers[currentFrame];

	VkSubmitInfo2 submitInfo{};
	submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO_2;
	submitInfo.waitSemaphoreInfoCount = 1;
	submitInfo.pWaitSemaphoreInfos = &waitInfo;
	submitInfo.commandBufferInfoCount = 1;
	submitInfo.pCommandBufferInfos = &cmdBufferInfo;
	submitInfo.signalSemaphoreInfoCount = 1;
	submitInfo.pSignalSemaphoreInfos = &signalInfo;

	VK_CHECK(vkQueueSubmit2(device.graphicsQueue(), 1, &submitInfo, inFlightFences[currentFrame]));

	VkPresentInfoKHR presentInfo{};
	presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
	presentInfo.waitSemaphoreCount = 1;
	presentInfo.pWaitSemaphores = &renderFinishedSemaphores[imageIndex];
	presentInfo.swapchainCount = 1;
	VkSwapchainKHR swapchainHandle = swapchain.get();
	presentInfo.pSwapchains = &swapchainHandle;
	presentInfo.pImageIndices = &imageIndex;

	VkResult presentResult = vkQueuePresentKHR(device.presentQueue(), &presentInfo);
	if (presentResult == VK_ERROR_OUT_OF_DATE_KHR || presentResult == VK_SUBOPTIMAL_KHR)
	{
		framebufferResized = true;   // handled at the top of the next drawFrame
	}

	currentFrame = (currentFrame + 1) % MAX_FRAMES_IN_FLIGHT;
}

void Renderer::waitIdle()
{
	vkDeviceWaitIdle(device.get());
}