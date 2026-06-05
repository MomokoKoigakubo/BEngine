using System.Numerics;

namespace IdleL.Scenes
{
	/// <summary>
	/// Input contract a camera implements so one controller can drive any camera type.
	/// The methods are abstract intents, the controller emits "look / move / zoom this much" and
	/// each camera decides what that means for itself (orbit rotates around a target; fly rotates its
	/// look direction; etc.). All amounts are relative deltas, already scaled by the controller for
	/// sensitivity and frame time, never absolute positions or angles.
	/// </summary>
	interface IControllableCamera
	{
		/// <summary>Rotate the view by relative amounts (mouse-look). Orbit: orbits its target; fly: turns its look direction.</summary>
		/// <param name="Yaw">Horizontal turn delta, in radians (controller-scaled).</param>
		/// <param name="Pitch">Vertical turn delta, in radians (controller-scaled).</param>
		void Look(float Yaw, float Pitch);

		/// <summary>Translate along the camera's LOCAL axes (WASD). Orbit: pans its target; fly: moves its position.</summary>
		/// <param name="Delta">Local-space move amount: X = right, Y = up, Z = forward.</param>
		void Move(Vector3 Delta);

		/// <summary>Zoom by a scalar amount (scroll). Orbit: changes distance to target; fly: changes fov or move speed.</summary>
		/// <param name="zoom">Relative zoom delta; the sign convention is the camera's choice.</param>
		void Zoom(float zoom);
	}
}
