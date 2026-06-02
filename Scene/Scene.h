#pragma once
#include "util/math/math.h"
#include "Resources/Handles.h"
#include "OrbitCamera.h"
#include <vector>


// One thing to draw: a mesh (by handle) at a world transform.
struct Renderable
{
	MeshHandle mesh;
	glm::mat4 transform{ 1.0f };
};

// Pure game state: what to draw + the view. No Vulkan. Terrain/entities populate this.
class Scene
{
public:
	OrbitCamera camera;   // the view into the scene

	void add(MeshHandle mesh, const glm::mat4& transform = glm::mat4(1.0f))
	{
		renderables.push_back({ mesh, transform });
	}

	void update(float dt) { time_ += dt; }
	float time() const { return time_; }
	const std::vector<Renderable>& getRenderables() const { return renderables; }

private:
	std::vector<Renderable> renderables;
	float time_ = 0.0f;
};
