using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using IdleL.Assets;
using IdleL.BBModel;
using IdleL.Rendering;
using IdleL.Resources;
using IdleL.Scenes;

namespace IdleL.App;

// Owns the window + the renderer/resources/scene. Loads momoko, loops a clip, and drives the
// per-frame skeletal animation. Mirrors the C++ Application, mapped onto Silk.NET's callbacks.
class Application
{
    IWindow window = null!;
    IInputContext input = null!;
    Renderer renderer = null!;
    ResourceManager resources = null!;
    Scene scene = null!;

    readonly List<Bone> bones = new();
    Matrix4x4[] boneMatrices = Array.Empty<Matrix4x4>();   // reused each frame; renderer holds this same ref
    Animation animClip = new();
    bool modelEulerXYZ;
    float animTime;
    Vector2 lastMouse;

    public void Run()
    {
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(1280, 720),
            Title = "BEngine",
            VSync = false   // uncapped, so the FPS readout shows raw throughput (set true for smooth 60)
        };
        window = Window.Create(options);
        window.Load += OnLoad;
        window.Update += OnUpdate;
        window.Render += OnRender;
        window.FramebufferResize += _ => renderer.SetFrameBufferResized();
        window.Run();

        renderer?.WaitIdle();
        resources?.Dispose();
        renderer?.Dispose();
        window.Dispose();
    }

    void OnLoad()
    {
        SetMinimumSize(650, 360);   // Silk.NET's high-level API has no min-size; go through GLFW
        renderer = new Renderer(window);
        resources = new ResourceManager(renderer);
        scene = new Scene();

        var model = BBModelLoader.Load(File.ReadAllText(AssetPaths.Model("momoko.bbmodel")));
        var verts = new List<Vertex>();
        var indices = new List<uint>();
        CubeBuilder.BuildModel(model, verts, indices, bones);

        modelEulerXYZ = model.EulerXYZ;
        foreach (var a in model.Animations) if (a.Name == "talking_lineal") animClip = a;

        // initial bind-pose upload; OnUpdate refills this same array in place from here on.
        boneMatrices = new Matrix4x4[bones.Count];
        for (int i = 0; i < bones.Count; i++) boneMatrices[i] = bones[i].BindMatrix;
        renderer.SetBoneMatrices(boneMatrices);   // renderer keeps the reference; no further calls needed

        MeshHandle mesh = resources.CreateMesh(verts.ToArray(), indices.ToArray());
        TextureHandle tex = resources.LoadTexture(AssetPaths.Model("momoko.png"));
        scene.Add(mesh);

        if (model.Textures.Count > 0)
        {
            var tm = model.Textures[0];
            int imgW = resources.TextureWidth(tex);
            int imgH = resources.TextureHeight(tex);
            if (imgW > 0 && tm.UvHeight > 0)
            {
                float frameCount = (imgH * tm.UvWidth) / (float)(imgW * tm.UvHeight);
                resources.RegisterFlipbook(tex, frameCount, 10.0f);
            }
        }

        scene.Camera.Target = new Vector3(0.0f, 0.6f, -0.75f);
        scene.Camera.Distance = 8.0f;

        input = window.CreateInput();
        foreach (var mouse in input.Mice)
            mouse.MouseMove += OnMouseMove;
    }

    // Clamp how small the window can be dragged. The high-level IWindow doesn't expose this, so we
    // reach the GLFW window handle and call glfwSetWindowSizeLimits directly (no-op on other backends).
    unsafe void SetMinimumSize(int minW, int minH)
    {
        if (window.Native?.Glfw is nint handle)
        {
            var glfw = Silk.NET.GLFW.GlfwProvider.GLFW.Value;
            glfw.SetWindowSizeLimits((Silk.NET.GLFW.WindowHandle*)handle, minW, minH,
                Silk.NET.GLFW.Glfw.DontCare, Silk.NET.GLFW.Glfw.DontCare);
        }
    }

    void OnMouseMove(IMouse mouse, Vector2 pos)
    {
        const float sens = 0.005f;
        if (mouse.IsButtonPressed(MouseButton.Left))
        {
            Vector2 d = pos - lastMouse;
            scene.Camera.Orbit(d.X * sens, -d.Y * sens);
        }
        lastMouse = pos;
    }

    void OnUpdate(double dt)
    {
        scene.Update((float)dt);
        if (animClip.Length > 0.0f)
        {
            animTime = (animTime + (float)dt) % animClip.Length;   // advance + loop
            // refill the shared array in place (no alloc); the renderer already holds this reference
            CubeBuilder.ComputeBoneMatrices(bones, animClip, animTime, modelEulerXYZ, boneMatrices);
        }
    }

    double fpsTimer;
    int fpsFrames;

    void OnRender(double dt)
    {
        renderer.DrawFrame(scene, resources);

        // FPS readout: average over ~1s, shown in the title bar + console (mirrors the C++ loop)
        fpsFrames++;
        fpsTimer += dt;
        if (fpsTimer >= 1.0)
        {
            double fps = fpsFrames / fpsTimer;
            window.Title = $"BEngine - {fps:F0} FPS";
            Console.WriteLine($"{fps:F0} FPS");
            fpsFrames = 0;
            fpsTimer = 0;
        }
    }
}
