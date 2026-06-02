#pragma once
#include <Volk/volk.h>
#include <vector>
#include "Renderer/Buffer.h"
#include "Renderer/Vertex.h"
#include "Renderer/Device.h"

class Mesh
{
public:
	Mesh(Device& device, const std::vector<Vertex>& vertices,
		const std::vector<uint32_t>& indices);

	void draw(VkCommandBuffer cmd) const;

private:
	Buffer vertexBuffer;
	Buffer indexBuffer;
	uint32_t indexCount;
};