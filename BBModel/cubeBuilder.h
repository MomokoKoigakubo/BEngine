#pragma once
#include "VulkanContext/Vertex.h"
#include "BBModel/bbmodel.h"
#include "util/math/math.h"
#include <vector>

// Builds one cube element into verts/indices. `parent` is the accumulated
// transform from the element's ancestor groups (identity if ungrouped).
void BuildCube(const element& e, std::vector<Vertex>& verts,
               std::vector<uint32_t>& indices, const resolution& res,
               const glm::mat4& parent = glm::mat4(1.0f));

// Walks the whole model's outliner hierarchy, composing group transforms, and
// builds every cube into one shared verts/indices list.
void buildModel(const BBModelParts& model, std::vector<Vertex>& verts,
                std::vector<uint32_t>& indices);