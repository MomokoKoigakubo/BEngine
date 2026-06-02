#include "cubeBuilder.h"
#include "util/math/math.h"
#include <map>
#include <string>
#include <functional>

// Rotation around a pivot, in Blockbench space: scale pixels->blocks (/16),
// rotate (DEGREES, ZYX order for bedrock), pivoting about `originPx`.
//   T(origin) * Rz * Ry * Rx * T(-origin)
// Euler rotation matrix. Order is format-dependent (glm::rotate post-multiplies, so the
// order of calls = the matrix order): ZYX -> Rz*Ry*Rx (bedrock); XYZ -> Rx*Ry*Rz (free/java).
static glm::mat4 eulerRot(glm::vec3 rotationDeg, bool eulerXYZ)
{
    glm::mat4 rot(1.0f);
    if (eulerXYZ)
    {
        rot = glm::rotate(rot, glm::radians(rotationDeg.x), { 1, 0, 0 });
        rot = glm::rotate(rot, glm::radians(rotationDeg.y), { 0, 1, 0 });
        rot = glm::rotate(rot, glm::radians(rotationDeg.z), { 0, 0, 1 });
    }
    else
    {
        rot = glm::rotate(rot, glm::radians(rotationDeg.z), { 0, 0, 1 });
        rot = glm::rotate(rot, glm::radians(rotationDeg.y), { 0, 1, 0 });
        rot = glm::rotate(rot, glm::radians(rotationDeg.x), { 1, 0, 0 });
    }
    return rot;
}

// For ABSOLUTE geometry (cube from/to, groups): rotate around the pivot in place.
static glm::mat4 pivotTransform(glm::vec3 originPx, glm::vec3 rotationDeg, bool eulerXYZ)
{
    glm::vec3 origin = originPx / 16.0f;
    glm::mat4 rot = eulerRot(rotationDeg, eulerXYZ);
    return glm::translate(glm::mat4(1.0f),  origin)
         * rot
         * glm::translate(glm::mat4(1.0f), -origin);
}

static void addQuad(std::vector<Vertex>& verts, std::vector<uint32_t>& indices,
    glm::vec3 p0, glm::vec3 p1, glm::vec3 p2, glm::vec3 p3,
    glm::vec3 normal, const glm::mat4& m,
    glm::vec2 uvMin, glm::vec2 uvMax, float texIndex, int rotation)
{
    glm::mat3 nm = glm::mat3(m);                  // rotation part, for the normal
    glm::vec3 n = glm::normalize(nm * normal);

    uint32_t base = static_cast<uint32_t>(verts.size());   // index of p0

    // p0..p3 are CCW from outside; face's UV rect corners, cyclically shifted by the
    // CubeFace rotation (0/90/180/270 -> 0..3 steps).
    glm::vec2 uvs[4] = {
        { uvMin.x, uvMax.y },   // p0 at rotation 0
        { uvMax.x, uvMax.y },   // p1
        { uvMax.x, uvMin.y },   // p2
        { uvMin.x, uvMin.y }    // p3
    };
    int steps = ((rotation / 90) % 4 + 4) % 4;   // 0..3, handles negatives

    verts.push_back({ glm::vec3(m * glm::vec4(p0, 1.0f)), n, uvs[(0 + steps) % 4], texIndex });
    verts.push_back({ glm::vec3(m * glm::vec4(p1, 1.0f)), n, uvs[(1 + steps) % 4], texIndex });
    verts.push_back({ glm::vec3(m * glm::vec4(p2, 1.0f)), n, uvs[(2 + steps) % 4], texIndex });
    verts.push_back({ glm::vec3(m * glm::vec4(p3, 1.0f)), n, uvs[(3 + steps) % 4], texIndex });

    // two triangles, CCW from outside: (0,1,2) and (2,3,0)
    indices.push_back(base + 0);
    indices.push_back(base + 1);
    indices.push_back(base + 2);
    indices.push_back(base + 2);
    indices.push_back(base + 3);
    indices.push_back(base + 0);
}

void BuildCube(const element& e, std::vector<Vertex>& verts,
               std::vector<uint32_t>& indices, const resolution& res, bool eulerXYZ, const glm::mat4& parent)
{
    glm::vec3 a = e.from / 16.0f;
    glm::vec3 b = e.to / 16.0f;

    // element's own pivot rotation, composed under its ancestor groups
    glm::mat4 model = parent * pivotTransform(e.origin, e.rotation, eulerXYZ);

    // CubeFace UVs are in texture pixels -> normalize to 0..1 by the resolution
    glm::vec2 texSize{ (float)res.width, (float)res.height };
    auto uvMin = [&](const CubeFace& f) { return glm::vec2(f.u0, f.v0) / texSize; };
    auto uvMax = [&](const CubeFace& f) { return glm::vec2(f.u1, f.v1) / texSize; };
    auto texIdx = [](const CubeFace& f) { return (float)(f.texture < 0 ? 0 : f.texture); };

    // UP (+Y)
    if (e.up.present)
        addQuad(verts, indices,
            { a.x, b.y, b.z }, { b.x, b.y, b.z }, { b.x, b.y, a.z },
            { a.x, b.y, a.z }, { 0, 1, 0 }, model, uvMin(e.up), uvMax(e.up), texIdx(e.up), e.up.rotation);
    // DOWN (-Y)
    if (e.down.present)
        addQuad(verts, indices,
            { a.x, a.y, a.z }, { b.x, a.y, a.z }, { b.x, a.y, b.z },
            { a.x, a.y, b.z }, { 0, -1, 0 }, model, uvMin(e.down), uvMax(e.down), texIdx(e.down), e.down.rotation);
    // SOUTH (+Z)
    if (e.south.present)
        addQuad(verts, indices,
            { a.x, a.y, b.z }, { b.x, a.y, b.z }, { b.x, b.y, b.z },
            { a.x, b.y, b.z }, { 0, 0, 1 }, model, uvMin(e.south), uvMax(e.south), texIdx(e.south), e.south.rotation);
    // NORTH (-Z)
    if (e.north.present)
        addQuad(verts, indices,
            { b.x, a.y, a.z }, { a.x, a.y, a.z }, { a.x, b.y, a.z },
            { b.x, b.y, a.z }, { 0, 0, -1 }, model, uvMin(e.north), uvMax(e.north), texIdx(e.north), e.north.rotation);
    // EAST (+X)
    if (e.east.present)
        addQuad(verts, indices,
            { b.x, a.y, b.z }, { b.x, a.y, a.z }, { b.x, b.y, a.z },
            { b.x, b.y, b.z }, { 1, 0, 0 }, model, uvMin(e.east), uvMax(e.east), texIdx(e.east), e.east.rotation);
    // WEST (-X)
    if (e.west.present)
        addQuad(verts, indices,
            { a.x, a.y, a.z }, { a.x, a.y, b.z }, { a.x, b.y, b.z },
            { a.x, b.y, a.z }, { -1, 0, 0 }, model, uvMin(e.west), uvMax(e.west), texIdx(e.west), e.west.rotation);
}

void buildMesh(const element& e, std::vector<Vertex>& verts,
    std::vector<uint32_t>& indices, const resolution& res, bool eulerXYZ, const glm::mat4& parent)
{
    // Mesh vertices are LOCAL to the element origin (unlike cube from/to which are absolute),
    // so place them AT the origin, rotated -> T(origin) * R (no T(-origin) pivot).
    glm::vec3 origin = e.origin / 16.0f;
    glm::mat4 model = parent * glm::translate(glm::mat4(1.0f), origin) * eulerRot(e.rotation, eulerXYZ);
    glm::vec2 texSize{ (float)res.width, (float)res.height };

    for (const auto& [Key, face] : e.faces)  //face = meshface
    {
        size_t n = face.vertices.size();
        if (n < 3) continue;

        //world pos, normalized uvs, look up by vert name
        std::vector<glm::vec3> wp(n);
        std::vector<glm::vec2> uv(n);
        for (size_t i = 0; i < n; i++)
        {
            const std::string& name = face.vertices[i];
            glm::vec3 local = e.vertices.at(name) / 16.0f;
            wp[i] = glm::vec3(model * glm::vec4(local, 1.0f));
            uv[i] = face.uv.at(name) / texSize;
        }

        //mesh faces must be computed from first triangle
        glm::vec3 nrm = glm::normalize(glm::cross(wp[1] - wp[0], wp[2] - wp[0]));
        float texIndex = (float)(face.texture < 0 ? 0 : face.texture);

        uint32_t baseIdx = (uint32_t)verts.size();

        // push the face's vertices
        for (size_t i = 0; i < n; i++)
            verts.push_back({ wp[i], nrm, uv[i], texIndex });

        // triangulate as a fan: (0,1,2), (0,2,3), etc
        for (size_t i = 1; i + 1 < n; i++)
        {
            indices.push_back(baseIdx + 0);
            indices.push_back(baseIdx + (uint32_t)i);
            indices.push_back(baseIdx + (uint32_t)i + 1);
        }
    }
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
                acc = parent * pivotTransform(it->second->origin, it->second->rotation, model.eulerXYZ);

            for (const auto& child : node.children)
                walk(child, acc);
        }
        else
        {
            auto it = elemMap.find(node.uuid);
            if (it != elemMap.end())
            {
                const element& el = *it->second;
                if (el.type == ElementType::Cube)
                    BuildCube(el, verts, indices, model.res, model.eulerXYZ, parent);
                else if (el.type == ElementType::Mesh)
                    buildMesh(el, verts, indices, model.res, model.eulerXYZ, parent);
            }
        }
    };

    for (const auto& node : model.outliner)
        walk(node, glm::mat4(1.0f));
}