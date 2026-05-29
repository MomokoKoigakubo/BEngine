#pragma once
#include "bbmodel.h"
#include "../JsonParser/jparser.h"

class BBModelLoader
{
public:
	BBModelParts load(const JsonValue& root);

private:
	glm::vec3 toVec3(const JsonArray& a);
	glm::vec2 toVec2(const JsonArray& a);

	element loadElement(const JsonObject& obj);
	CubeFace loadCubeFace(const JsonObject& faceObj);
	MeshFace loadMeshFace(const JsonObject& faceObj);
	OutlinerNode loadOutlinerNode(const JsonObject& node);
};