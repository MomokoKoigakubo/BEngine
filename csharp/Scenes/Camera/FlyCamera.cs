using System.Numerics;
using System.Security.AccessControl;

namespace IdleL.Scenes;

class FlyCamera : Camera, IControllableCamera
{
	public Vector3 Position = new(0f, 2f, 5f);
	public float Yaw = 0f;
	public float Pitch = 0f;

	//unit dir the cam looks (yaw=0, pitch=0 > down -z)
	public Vector3 Forward() => Vector3.Normalize(new Vector3(
		MathF.Sin(Yaw) * MathF.Cos(Pitch),
		MathF.Sin(Pitch),
	   -MathF.Cos(Yaw) * MathF.Cos(Pitch)));

	public override Matrix4x4 ViewMatrix()
		=> Matrix4x4.CreateLookAt(Position, Position + Forward(), Vector3.UnitY);

	public void Rotate(float dYaw, float dPitch)
	{
		Yaw += dYaw;
		float limit = float.DegreesToRadians(89f);   // can't pitch fully vertical (lookAt degenerates vs up)
		Pitch = Math.Clamp(Pitch + dPitch, -limit, limit);
	}

	void IControllableCamera.Look(float Yaw, float Pitch)
	{
		Rotate(Yaw, Pitch);
	}

	void IControllableCamera.Move(Vector3 Delta)
	{
		Vector3 forward = Forward();
		Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
		Vector3 up = Vector3.Cross(forward, right);
		Position += right * Delta.X + up * Delta.Y + forward * Delta.Z;   // Delta is camera-local (X=right, Y=up, Z=forward)
	}
	void IControllableCamera.Zoom(float zoom)
	{
		float min = float.DegreesToRadians(10f);    // telephoto
		float max = float.DegreesToRadians(100f);   // wide
		Fov = Math.Clamp(Fov - zoom * float.DegreesToRadians(2f), min, max);   // scroll in = narrower fov = zoom in
	}
}
