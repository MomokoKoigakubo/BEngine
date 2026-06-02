#include "OrbitCamera.h"

glm::mat4 OrbitCamera::viewMatrix() const 
{
	glm::vec3 eye;
	eye.x = target.x + distance * cos(pitch) * cos(yaw);
	eye.y = target.y + distance * sin(pitch);
	eye.z = target.z + distance * cos(pitch) * sin(yaw);
	return glm::lookAt(eye, target, glm::vec3(0, 1, 0));
}

glm::mat4 OrbitCamera::projectionMatrix(float aspect) const
{
	glm::mat4 proj = glm::perspective(fov, aspect, nearPlane, farPlane);
	proj[1][1] *= -1; //yflip
	return proj;
}

void OrbitCamera::orbit(float dYaw, float dPitch)
{
	yaw   += dYaw;
	pitch += dPitch;

	const float limit = glm::radians(89.0f);   // don't flip over the poles
	if (pitch >  limit) pitch =  limit;
	if (pitch < -limit) pitch = -limit;
}
