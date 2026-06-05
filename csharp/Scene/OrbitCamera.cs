using System.Numerics;

namespace IdleL.Scenes;

// Orbits a target at a distance, driven by yaw/pitch. Matrices are System.Numerics (row-vector):
// they get uploaded raw and the shader reads them column-major (= their transpose = the GLM matrix).
class OrbitCamera
{
    public Vector3 Target = Vector3.Zero;
    public float Distance = 3.0f;
    public float Yaw = 0f;      // radians
    public float Pitch = 0f;
    public float Fov = float.DegreesToRadians(70f);
    public float NearPlane = 0.1f;
    public float FarPlane = 100f;

    public Matrix4x4 ViewMatrix()
    {
        Vector3 eye;
        eye.X = Target.X + Distance * MathF.Cos(Pitch) * MathF.Cos(Yaw);
        eye.Y = Target.Y + Distance * MathF.Sin(Pitch);
        eye.Z = Target.Z + Distance * MathF.Cos(Pitch) * MathF.Sin(Yaw);
        return Matrix4x4.CreateLookAt(eye, Target, new Vector3(0, 1, 0));   // RH, dual of glm::lookAt
    }

    public Matrix4x4 ProjectionMatrix(float aspect)
    {
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(Fov, aspect, NearPlane, FarPlane);  // RH, [0,1] depth
        proj.M22 *= -1f;   // Vulkan clip-space Y points down (mirrors the C++ proj[1][1] *= -1)
        return proj;
    }

    public void Orbit(float dYaw, float dPitch)
    {
        Yaw += dYaw;
        Pitch += dPitch;
        float limit = float.DegreesToRadians(89f);   // don't flip over the poles
        Pitch = Math.Clamp(Pitch, -limit, limit);
    }
}
