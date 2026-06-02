#pragma once
#include "Handles.h"
#include "Mesh.h"
#include "Texture.h"
#include "Renderer/Vertex.h"
#include <vector>
#include <memory>
#include <string>

class Renderer;

class ResourceManager
{
public:
	ResourceManager(Renderer& renderer);
	MeshHandle createMesh(const std::vector<Vertex>& vertices,
		const std::vector<uint32_t>& indices);
	TextureHandle loadTexture(const std::string& path);
	const Mesh& getMesh(MeshHandle h) const;			
	uint32_t textureWidth(TextureHandle h)  const { return textures[h.id]->width(); }
	uint32_t textureHeight(TextureHandle h) const { return textures[h.id]->height(); }
	void registerFlipbook(TextureHandle h, float frameCount, float flipRate);
private:
	Renderer& renderer;                                  
	std::vector<std::unique_ptr<Mesh>>    meshes;
	std::vector<std::unique_ptr<Texture>> textures;
};