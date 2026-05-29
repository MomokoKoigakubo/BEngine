#pragma once

#include <glm/glm.hpp>
#include <vector>
#include <string>
#include <map>

struct resolution
{
	int width, height;
};

struct OutlinerNode
{
	std::string uuid;
	bool isGroup;
	std::vector<OutlinerNode> children;
};

enum class ElementType{Cube, Mesh, Locator, NullObject, Unknown};

struct CubeFace 
{ 
	float u0, v0, u1, v1;
	int texture = -1;
	int rotation = 0;
};

struct MeshFace 
{
	std::vector<std::string> vertices;
	std::map<std::string, glm::vec2> uv;
	int texture = -1;
};

struct element 
{
	std::string uuid;
	ElementType type = ElementType::Cube;
	glm::vec3 origin{ 0 }, rotation{ 0 }, position{0};

	//cube
	glm::vec3 from{ 0 }, to{ 0 };
	CubeFace north, south, east, west, up, down;

	//mesh
	std::map<std::string, glm::vec3> vertices;
	std::map<std::string, MeshFace> faces;
};

struct group
{
	std::string uuid;
	glm::vec3 origin, rotation;
	std::vector<std::string> children;
};

struct BBModelParts 
{
	resolution res;
	std::vector<element> elements;
	std::vector<group> groups;
	std::vector<OutlinerNode> outliner;
};