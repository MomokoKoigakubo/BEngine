using System.Numerics;

namespace IdleL.Scenes;

class OrbitCamera : Camera, IControllableCamera
{
    public Vector3 Target = Vector3.Zero;
    public float Distance = 5.0f;
    public float Yaw = 0f;      // radians
    public float Pitch = 0f;

    public override Matrix4x4 ViewMatrix() 
    {
        Vector3 eye;
        eye.X = Target.X + Distance * MathF.Cos(Pitch) * MathF.Cos(Yaw);
        eye.Y = Target.Y + Distance * MathF.Sin(Pitch);
        eye.Z = Target.Z + Distance * MathF.Cos(Pitch) * MathF.Sin(Yaw);
        return Matrix4x4.CreateLookAt(eye, Target, new Vector3(0, 1, 0));   // RH, dual of glm::lookAt
    }

    // mouse-look = orbit around the target. (param names lowercase so they don't shadow the Yaw/Pitch fields)
    public void Look(float yaw, float pitch)
    {
        Yaw += yaw;
        float limit = float.DegreesToRadians(89f);   // don't flip over the poles
        Pitch = Math.Clamp(Pitch + pitch, -limit, limit);
    }

    // WASD = pan the target across the view plane; forward (Z) dollies in/out.
    public void Move(Vector3 delta)
    {
        Vector3 dir = new Vector3(                       // unit direction from target to eye
            MathF.Cos(Pitch) * MathF.Cos(Yaw),
            MathF.Sin(Pitch),
            MathF.Cos(Pitch) * MathF.Sin(Yaw));
        Vector3 forward = -dir;                          // the way the camera looks
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);
        Target += right * delta.X + up * delta.Y;
        Distance = MathF.Max(0.1f, Distance - delta.Z);
    }

    // scroll = change distance to the target (positive = zoom in).
    public void Zoom(float amount) => Distance = MathF.Max(0.1f, Distance - amount);

    // legacy: Application still calls Orbit until the controller lands; just forwards to Look.
    public void Orbit(float dYaw, float dPitch) => Look(dYaw, dPitch);
}
