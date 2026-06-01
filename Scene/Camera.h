#pragma once
#include "util/math/math.h"   // brings in glm with the right defines

class Camera {
public:
    virtual ~Camera() = default;
    virtual glm::mat4 viewMatrix() const = 0;
    virtual glm::mat4 projectionMatrix(float aspect) const = 0;
};