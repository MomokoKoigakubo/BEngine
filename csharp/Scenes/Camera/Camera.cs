using Silk.NET.Vulkan;
using System.Numerics;

namespace IdleL.Scenes;

// Base for all cameras. A new camera type only has to override ViewMatrix(); the projection
// (Vulkan perspective, Y-flipped) is shared here. Override ProjectionMatrix only for e.g. ortho.
abstract class Camera
{
    public float Fov = float.DegreesToRadians(70f);
    public float NearPlane = 0.1f;
    public float FarPlane = 1000f;

    public abstract Matrix4x4 ViewMatrix();

    public virtual Matrix4x4 ProjectionMatrix(float aspect)
    {
        var p = Matrix4x4.CreatePerspectiveFieldOfView(Fov, aspect, NearPlane, FarPlane);
        p.M22 *= -1f;   // Vulkan clip-space Y points down
        return p;
    }

    // Every camera gets frustum culling for free: the frustum is purely a function of view*proj.
    public Frustum GetFrustum(float aspect)
        => Frustum.FromViewProjection(ViewMatrix() * ProjectionMatrix(aspect));
}
