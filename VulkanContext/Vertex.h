#pragma once
#include <Volk/volk.h>
#include <glm/glm.hpp>
#include <array>

struct Vertex
{
	glm::vec2 pos;
	glm::vec3 colour;

	static VkVertexInputBindingDescription getBindingDesc()
	{
		VkVertexInputBindingDescription binding{};
		binding.binding = 0;
		binding.stride = sizeof(Vertex);
		binding.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;
		return binding;
	}

	static std::array<VkVertexInputAttributeDescription, 2> getAttributeDescriptions()
	{
		std::array <VkVertexInputAttributeDescription, 2> attrs{};
		attrs[0].binding = 0;
		attrs[0].location = 0; //shader inpput loc 0
		attrs[0].format = VK_FORMAT_R32G32_SFLOAT; //vec2
		attrs[0].offset = offsetof(Vertex, pos);

		attrs[1].binding = 0; 
		attrs[1].location = 1; //shader input loc 1
		attrs[1].format = VK_FORMAT_R32G32B32_SFLOAT; //vec3
		attrs[1].offset = offsetof(Vertex, colour);

		return attrs;
	}
};