#pragma once
#include <Volk/volk.h>
#include <glm/glm.hpp>
#include <array>

struct Vertex
{
	glm::vec3 pos;
	glm::vec3 normal;
	glm::vec2 uv;
	float texIndex;

	static VkVertexInputBindingDescription getBindingDesc()
	{
		VkVertexInputBindingDescription binding{};
		binding.binding = 0;
		binding.stride = sizeof(Vertex);
		binding.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;
		return binding;
	}

	static std::array<VkVertexInputAttributeDescription, 4> getAttributeDescriptions()
	{	//attrs[0] pos, attrs[1] normal. attrs[2]
		std::array <VkVertexInputAttributeDescription, 4> attrs{};
		attrs[0].binding = 0;
		attrs[0].location = 0; //shader inpput loc 0
		attrs[0].format = VK_FORMAT_R32G32B32_SFLOAT; //vec3
		attrs[0].offset = offsetof(Vertex, pos);

		attrs[1].binding = 0; 
		attrs[1].location = 1; //shader input loc 1
		attrs[1].format = VK_FORMAT_R32G32B32_SFLOAT; //vec3
		attrs[1].offset = offsetof(Vertex, normal);

		attrs[2].binding = 0;
		attrs[2].location = 2; //shader input loc 2
		attrs[2].format = VK_FORMAT_R32G32_SFLOAT; //vec2
		attrs[2].offset = offsetof(Vertex, uv);

		attrs[3].binding = 0;
		attrs[3].location = 3; //shader input loc 3
		attrs[3].format = VK_FORMAT_R32_SFLOAT; //1 32bit float
		attrs[3].offset = offsetof(Vertex, texIndex);

		return attrs;
	}
};