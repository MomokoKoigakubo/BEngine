#include "cubeBuilder.h"
#include "util/math/math.h"
#include <map>
#include <string>
#include <functional>

// Rotation around a pivot, in Blockbench space: scale pixels->blocks (/16),
// rotate (DEGREES, ZYX order for bedrock), pivoting about `originPx`.
//   T(origin) * Rz * Ry * Rx * T(-origin)
static glm::mat4 pivotTransform(glm::vec3 originPx, glm::vec3 rotationDeg)
{
    glm::vec3 origin = originPx / 16.0f;

    glm::mat4 rot(1.0f);
    rot = glm::rotate(rot, glm::radians(rotationDeg.z), { 0, 0, 1 });
    rot = glm::rotate(rot, glm::radians(rotationDeg.y), { 0, 1, 0 });
    rot = glm::rotate(rot, glm::radians(rotationDeg.x), { 1, 0, 0 });

    return glm::translate(glm::mat4(1.0f),  origin)
         * rot
         * glm::translate(glm::mat4(1.0f), -origin);
}

static void addQuad(std::vector<Vertex>& verts, std::vector<uint32_t>& indices,
    glm::vec3 p0, glm::vec3 p1, glm::vec3 p2, glm::vec3 p3,
    glm::vec3 normal, const glm::mat4& m,
    glm::vec2 uvMin, glm::vec2 uvMax)
{
    glm::mat3 nm = glm::mat3(m);                  // rotation part, for the normal
    glm::vec3 n = glm::normalize(nm * normal);

    uint32_t base = static_cast<uint32_t>(verts.size());   // index of p0

    // p0..p3 are CCW from outside; map them to the face's UV rect corners
    verts.push_back({ glm::vec3(m * glm::vec4(p0, 1.0f)), n, { uvMin.x, uvMax.y } });
    verts.push_back({ glm::vec3(m * glm::vec4(p1, 1.0f)), n, { uvMax.x, uvMax.y } });
    verts.push_back({ glm::vec3(m * glm::vec4(p2, 1.0f)), n, { uvMax.x, uvMin.y } });
    verts.push_back({ glm::vec3(m * glm::vec4(p3, 1.0f)), n, { uvMin.x, uvMin.y } });

    // two triangles, CCW from outside: (0,1,2) and (2,3,0)
    indices.push_back(base + 0);
    indices.push_back(base + 1);
    indices.push_back(base + 2);
    indices.push_back(base + 2);
    indices.push_back(base + 3);
    indices.push_back(base + 0);
}

void BuildCube(const element& e, std::vector<Vertex>& verts,
               std::vector<uint32_t>& indices, const resolution& res, const glm::mat4& parent)
{
    glm::vec3 a = e.from / 16.0f;
    glm::vec3 b = e.to / 16.0f;

    // element's own pivot rotation, composed under its ancestor groups
    glm::mat4 model = parent * pivotTransform(e.origin, e.rotation);

    // CubeFace UVs are in texture pixels -> normalize to 0..1 by the resolution
    glm::vec2 texSize{ (float)res.width, (float)res.height };
    auto uvMin = [&](const CubeFace& f) { return glm::vec2(f.u0, f.v0) / texSize; };
    auto uvMax = [&](const CubeFace& f) { return glm::vec2(f.u1, f.v1) / texSize; };

    // UP (+Y)
    addQuad(verts, indices,
        { a.x, b.y, b.z }, { b.x, b.y, b.z }, { b.x, b.y, a.z },
        { a.x, b.y, a.z }, { 0, 1, 0 }, model, uvMin(e.up), uvMax(e.up));
    // DOWN (-Y)
    addQuad(verts, indices,
        { a.x, a.y, a.z }, { b.x, a.y, a.z }, { b.x, a.y, b.z },
        { a.x, a.y, b.z }, { 0, -1, 0 }, model, uvMin(e.down), uvMax(e.down));
    // SOUTH (+Z)
    addQuad(verts, indices,
        { a.x, a.y, b.z }, { b.x, a.y, b.z }, { b.x, b.y, b.z },
        { a.x, b.y, b.z }, { 0, 0, 1 }, model, uvMin(e.south), uvMax(e.south));
    // NORTH (-Z)
    addQuad(verts, indices,
        { b.x, a.y, a.z }, { a.x, a.y, a.z }, { a.x, b.y, a.z },
        { b.x, b.y, a.z }, { 0, 0, -1 }, model, uvMin(e.north), uvMax(e.north));
    // EAST (+X)
    addQuad(verts, indices,
        { b.x, a.y, b.z }, { b.x, a.y, a.z }, { b.x, b.y, a.z },
        { b.x, b.y, b.z }, { 1, 0, 0 }, model, uvMin(e.east), uvMax(e.east));
    // WEST (-X)
    addQuad(verts, indices,
        { a.x, a.y, a.z }, { a.x, a.y, b.z }, { a.x, b.y, b.z },
        { a.x, b.y, a.z }, { -1, 0, 0 }, model, uvMin(e.west), uvMax(e.west));
}

void buildModel(const BBModelParts& model, std::vector<Vertex>& verts,
                std::vector<uint32_t>& indices)
{
    // uuid -> transform data / element (the outliner only stores uuids)
    std::map<std::string, const group*>   groupMap;
    std::map<std::string, const element*> elemMap;
    for (const auto& g : model.groups)   groupMap[g.uuid] = &g;
    for (const auto& e : model.elements) elemMap[e.uuid] = &e;

    // Walk the hierarchy, accumulating each group's transform into `parent`.
    std::function<void(const OutlinerNode&, const glm::mat4&)> walk =
        [&](const OutlinerNode& node, const glm::mat4& parent)
    {
        if (node.isGroup)
        {
            glm::mat4 acc = parent;
            auto it = groupMap.find(node.uuid);
            if (it != groupMap.end())
                acc = parent * pivotTransform(it->second->origin, it->second->rotation);

            for (const auto& child : node.children)
                walk(child, acc);
        }
        else
        {
            auto it = elemMap.find(node.uuid);
            if (it != elemMap.end() && it->second->type == ElementType::Cube)
                BuildCube(*it->second, verts, indices, model.res, parent);
        }
    };

    for (const auto& node : model.outliner)
        walk(node, glm::mat4(1.0f));
}