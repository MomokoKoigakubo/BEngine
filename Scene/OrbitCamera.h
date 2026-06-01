#pragma once
#include "Camera.h"

class OrbitCamera : public Camera
{
public:
	glm::vec3 target{ 0.0f };
	float distance = 3.0f;
	float yaw = 0.0f; //radians
	float pitch = 0.0f;
	float fov = glm::radians(70.0f);
	float nearPlane = 0.1f;
	float farPlane = 100.0f;

	glm::mat4 viewMatrix() const override;
	glm::mat4 projectionMatrix(float aspect) const override;
};