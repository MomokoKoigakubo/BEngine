#include "ResourceManager.h"
#include "Renderer/Renderer.h"   // full type needed here (forward-declared in the header)

ResourceManager::ResourceManager(Renderer& renderer) : renderer(renderer) {}

MeshHandle ResourceManager::createMesh(const std::vector<Vertex>& vertices,
                                       const std::vector<uint32_t>& indices)
{
	uint32_t id = (uint32_t)meshes.size();
	meshes.push_back(std::make_unique<Mesh>(renderer.getDevice(), vertices, indices));
	return MeshHandle{ id };
}

TextureHandle ResourceManager::loadTexture(const std::string& path)
{
	auto tex = std::make_unique<Texture>(renderer.getDevice(), path.c_str());
	uint32_t slot = renderer.registerBindlessTexture(tex->view(), tex->sampler());
	textures.push_back(std::move(tex));
	return TextureHandle{ slot };
}

void ResourceManager::registerFlipbook(TextureHandle h, float frameCount, float flipRate)
{
	renderer.setFlipbook(h.id, frameCount, flipRate);
}

const Mesh& ResourceManager::getMesh(MeshHandle h) const
{
	return *meshes[h.id];
}
