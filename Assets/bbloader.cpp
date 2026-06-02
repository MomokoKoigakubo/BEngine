#include "bbloader.h"
#include <glm/glm.hpp>
#include <variant>


BBModelParts BBModelLoader::load(const JsonValue& root)
{
	BBModelParts parts;
	const JsonObject& obj = std::get<JsonObject>(root.data);

	const JsonObject& res = std::get<JsonObject>(obj.at("resolution").data);
	parts.res.width  = (int)std::get<double>(res.at("width").data);
	parts.res.height = (int)std::get<double>(res.at("height").data);

	// euler rotation order is format-dependent: bedrock = ZYX, others (free/java) = XYZ
	if (obj.count("meta"))
	{
		const JsonObject& meta = std::get<JsonObject>(obj.at("meta").data);
		if (meta.count("model_format"))
			parts.eulerXYZ = (std::get<std::string>(meta.at("model_format").data) != "bedrock");
	}

	if (obj.count("textures"))
	{
		const JsonArray& texArr = std::get<JsonArray>(obj.at("textures").data);
		for (const auto& t : texArr)
		{
			const JsonObject& tobj = std::get<JsonObject>(t.data);
			TextureMeta tm;
			if (tobj.count("name"))             tm.name = std::get<std::string>(tobj.at("name").data);
			if (tobj.count("uv_width"))         tm.uvWidth = (int)std::get<double>(tobj.at("uv_width").data);
			if (tobj.count("uv_height"))        tm.uvHeight = (int)std::get<double>(tobj.at("uv_height").data);
			if (tobj.count("frame_time"))       tm.frameTime = (float)std::get<double>(tobj.at("frame_time").data);
			if (tobj.count("frame_order_type")) tm.flipType = std::get<std::string>(tobj.at("frame_order_type").data);
			parts.textures.push_back(tm);
		}
	}

	const JsonArray& elements = std::get<JsonArray>(obj.at("elements").data);
	for (const auto& elem : elements)
	{
		parts.elements.push_back(loadElement(std::get<JsonObject>(elem.data)));
	}

	// groups[] holds group TRANSFORM data (uuid/origin/rotation); the outliner
	// holds only the hierarchy. Joined by uuid when composing parent transforms.
	if (obj.count("groups"))
	{
		const JsonArray& groupArr = std::get<JsonArray>(obj.at("groups").data);
		for (const auto& g : groupArr)
		{
			const JsonObject& gobj = std::get<JsonObject>(g.data);
			group grp;
			grp.uuid = std::get<std::string>(gobj.at("uuid").data);
			if (gobj.count("origin"))
				grp.origin = toVec3(std::get<JsonArray>(gobj.at("origin").data));
			if (gobj.count("rotation"))
				grp.rotation = toVec3(std::get<JsonArray>(gobj.at("rotation").data));
			parts.groups.push_back(grp);
		}
	}

	const JsonArray& outliner = std::get<JsonArray>(obj.at("outliner").data);
	for (const auto& node : outliner)
	{
		if (std::holds_alternative<std::string>(node.data))
		{
			OutlinerNode leaf;
			leaf.uuid = std::get<std::string>(node.data);
			leaf.isGroup = false;
			parts.outliner.push_back(leaf);
		}
		else	// group object -> recurse
		{
			parts.outliner.push_back(loadOutlinerNode(std::get<JsonObject>(node.data)));
		}
	}

	return parts;
}

glm::vec3 BBModelLoader::toVec3(const JsonArray& a)
{
	glm::vec3 result;
	for (int i = 0; i < a.size(); i++) 
	{
		result[i] = std::get<double>(a[i].data);
	}

	return result;
}

glm::vec2 BBModelLoader::toVec2(const JsonArray& a)
{
	glm::vec2 result;
	for (int i = 0; i < a.size(); i++)
	{
		result[i] = std::get<double>(a[i].data);
	}
	return result;
}

element BBModelLoader::loadElement(const JsonObject& obj)
{
	element el;
	
	el.uuid = std::get<std::string>(obj.at("uuid").data);
	std::string type = std::get<std::string>(obj.at("type").data);

	el.origin = toVec3(std::get<JsonArray>(obj.at("origin").data));
	if (obj.count("rotation"))
		el.rotation = toVec3(std::get<JsonArray>(obj.at("rotation").data));

	if (type == "cube")
	{
		el.type = ElementType::Cube;
		el.from = toVec3(std::get<JsonArray>(obj.at("from").data));
		el.to = toVec3(std::get<JsonArray>(obj.at("to").data));

		const JsonObject& faces = std::get<JsonObject>(obj.at("faces").data);
		if(faces.count("north")) el.north = loadCubeFace(std::get<JsonObject>(faces.at("north").data));
		if(faces.count("south")) el.south = loadCubeFace(std::get<JsonObject>(faces.at("south").data));
		if(faces.count("east"))  el.east  =  loadCubeFace(std::get<JsonObject>(faces.at("east").data));
		if(faces.count("west"))  el.west  =  loadCubeFace(std::get<JsonObject>(faces.at("west").data));
		if(faces.count("up"))    el.up    =    loadCubeFace(std::get<JsonObject>(faces.at("up").data));
		if(faces.count("down"))  el.down  =  loadCubeFace(std::get<JsonObject>(faces.at("down").data));
	}
	else if(type == "mesh")
	{
		el.type = ElementType::Mesh;
		const JsonObject& verts = std::get<JsonObject>(obj.at("vertices").data);
		for (const auto& pair : verts)
		{
			el.vertices[pair.first] = toVec3(std::get<JsonArray>(pair.second.data));
		}

		const JsonObject& meshFaces = std::get<JsonObject>(obj.at("faces").data);
		for (const auto& pair : meshFaces)
		{
			el.faces[pair.first] = loadMeshFace(std::get<JsonObject>(pair.second.data));
		}
	}
	else if (type == "locator" || type == "null_object")
	{
		el.type = (type == "locator") ? ElementType::Locator : ElementType::NullObject;
		el.position = toVec3(std::get<JsonArray>(obj.at("position").data));
	}
	else {
		el.type = ElementType::Unknown;
	}
	return el;
}

CubeFace BBModelLoader::loadCubeFace(const JsonObject& faceObj)
{
	CubeFace face;
	const JsonArray& uv = std::get<JsonArray>(faceObj.at("uv").data);
	face.u0 = std::get<double>(uv[0].data);
	face.v0 = std::get<double>(uv[1].data);
	face.u1 = std::get<double>(uv[2].data);
	face.v1 = std::get<double>(uv[3].data);

	if (faceObj.count("texture") &&
		std::holds_alternative<double>(faceObj.at("texture").data)) {
		face.texture = (int)std::get<double>(faceObj.at("texture").data);
	}
	if (faceObj.count("rotation") &&
		std::holds_alternative<double>(faceObj.at("rotation").data)) {
		face.rotation = (int)std::get<double>(faceObj.at("rotation").data);
	}
	face.present = true;
	return face;
}


MeshFace BBModelLoader::loadMeshFace(const JsonObject& faceObj)
{
	MeshFace face;
	const JsonArray& verts = std::get<JsonArray>(faceObj.at("vertices").data);

	for (int i = 0; i < verts.size(); i++) {
		face.vertices.push_back(std::get<std::string>(verts[i].data));
	}

	const JsonObject& uv = std::get<JsonObject>(faceObj.at("uv").data);
	for (const auto& pair : uv)
	{
		face.uv[pair.first] = toVec2(std::get<JsonArray>(pair.second.data));
	}
	if (faceObj.count("texture") &&
		std::holds_alternative<double>(faceObj.at("texture").data)) {
		face.texture = (int)std::get<double>(faceObj.at("texture").data);
	}
	return face;
}

OutlinerNode BBModelLoader::loadOutlinerNode(const JsonObject& node)
{
	OutlinerNode result;
	result.uuid = std::get<std::string>(node.at("uuid").data);
	result.isGroup = true;	// we only reach this function for group objects

	const JsonArray& children = std::get<JsonArray>(node.at("children").data);
	for (const auto& child : children)
	{
		if (std::holds_alternative<std::string>(child.data))
		{
			OutlinerNode leaf;
			leaf.uuid = std::get<std::string>(child.data);
			leaf.isGroup = false;
			result.children.push_back(leaf);
		}
		else	// it's an object -> a sub-group, recurse
		{
			result.children.push_back(loadOutlinerNode(std::get<JsonObject>(child.data)));
		}
	}

	return result;
}