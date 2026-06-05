using System.Numerics;
using Silk.NET.Input;

namespace IdleL.Scenes;

// The universal controller: reads input each frame and drives whatever IControllableCamera it points
// at, via Look / Move / Zoom. Look & move are POLLED (so switching cameras is just swapping Camera);
// scroll is delta-based, so it's accumulated through the mouse's Scroll event and consumed per frame.
class CameraController : ICameraController
{
    public IControllableCamera Camera;   // swap this to change which camera is driven

    readonly IKeyboard keyboard;
    readonly IMouse mouse;
    Vector2 lastMouse;
    float scrollAccum;

    public float LookSensitivity = 0.005f;
    public float MoveSpeed = 6f;    // world units / second
    public float ZoomSpeed = 2f;

    public CameraController(IControllableCamera camera, IKeyboard keyboard, IMouse mouse)
    {
        Camera = camera;
        this.keyboard = keyboard;
        this.mouse = mouse;
        lastMouse = mouse.Position;
        mouse.Scroll += (_, wheel) => scrollAccum += wheel.Y;
    }

    public void Update(float dt)
    {
        Vector2 pos = mouse.Position;
        Vector2 d = pos - lastMouse;
        lastMouse = pos;
        if (mouse.IsButtonPressed(MouseButton.Left))
            Camera.Look(d.X * LookSensitivity, -d.Y * LookSensitivity);

        // move: WASD + E/Q up/down, in the camera's LOCAL frame
        Vector3 move = Vector3.Zero;
        if (keyboard.IsKeyPressed(Key.W)) move.Z += 1;
        if (keyboard.IsKeyPressed(Key.S)) move.Z -= 1;
        if (keyboard.IsKeyPressed(Key.D)) move.X += 1;
        if (keyboard.IsKeyPressed(Key.A)) move.X -= 1;
        if (keyboard.IsKeyPressed(Key.E)) move.Y += 1;
        if (keyboard.IsKeyPressed(Key.Q)) move.Y -= 1;
        if (move != Vector3.Zero)
            Camera.Move(move * (MoveSpeed * dt));

        if (scrollAccum != 0f)
        {
            Camera.Zoom(scrollAccum * ZoomSpeed);
            scrollAccum = 0f;
        }
    }
}
