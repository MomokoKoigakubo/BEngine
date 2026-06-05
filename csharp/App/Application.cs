using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using IdleL.Assets;
using IdleL.BBModel;
using IdleL.ECS;
using IdleL.Rendering;
using IdleL.Resources;
using IdleL.Scenes;

namespace IdleL.App;

class Application
{
    const int Msaa = 4;   // anti-aliasing setting: 1 = off, 2/4/8 = MSAA samples (clamped to GPU support)

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
    OrbitCamera orbitCam = null!;
    FlyCamera flyCam = null!;
    CameraController controller = null!;
    bool usingFly;

    public void Run()
    {
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(1280, 720),
            Title = "BEngine",
            VSync = false  
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
        SetMinimumSize(650, 360);   // Silk.NETs API has no min-size; go through GLFW
        renderer = new Renderer(window, Msaa);
        resources = new ResourceManager(renderer);
        scene = new Scene();

        var model = BBModelLoader.Load(File.ReadAllText(AssetPaths.Model("momoko.bbmodel")));
        var verts = new List<Vertex>();
        var indices = new List<uint>();
        CubeBuilder.BuildModel(model, verts, indices, bones);

        modelEulerXYZ = model.EulerXYZ;
        foreach (var a in model.Animations) if (a.Name == "talking_lineal") animClip = a;

        boneMatrices = new Matrix4x4[bones.Count];
        for (int i = 0; i < bones.Count; i++) boneMatrices[i] = bones[i].BindMatrix;
        renderer.SetBoneMatrices(boneMatrices);   // renderer keeps the reference; no further calls needed

        CubeBuilder.ComputeBounds(verts, bones, out Vector3 center, out float radius);
        MeshHandle mesh = resources.CreateMesh(verts.ToArray(), indices.ToArray());
        TextureHandle tex = resources.LoadTexture(AssetPaths.Model("momoko.png"));

        // instancing stress test: a 100x100 grid = 10,000 momokos, GPU-culled into one indirect draw.
        const int grid = 100;
        var models = new Matrix4x4[grid * grid];
        int mi = 0;
        for (int x = 0; x < grid; x++)
            for (int z = 0; z < grid; z++)
                models[mi++] = Matrix4x4.CreateTranslation((x - grid / 2) * 2.5f, 0f, (z - grid / 2) * 2.5f);
        renderer.SetInstances(mesh, models, center, radius);

        // ground plane via the static mesh path (terrain stand-in / coworker's entry-point demo)
        const float gs = 150f;
        var groundVerts = new Vertex[]   // uv = world XZ so the shader's grid lines up with the world
        {
            new() { Pos = new(-gs, 0, -gs), Normal = new(0, 1, 0), Uv = new(-gs, -gs) },
            new() { Pos = new(gs, 0, -gs), Normal = new(0, 1, 0), Uv = new(gs, -gs) },
            new() { Pos = new(gs, 0, gs), Normal = new(0, 1, 0), Uv = new(gs, gs) },
            new() { Pos = new(-gs, 0, gs), Normal = new(0, 1, 0), Uv = new(-gs, gs) },
        };
        var groundIndices = new uint[] { 0, 1, 2, 0, 2, 3 };
        MeshHandle ground = resources.CreateMesh(groundVerts, groundIndices);
        scene.AddStatic(ground, Matrix4x4.Identity, Vector3.Zero, gs * 1.5f);

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

        // cameras: orbit (frames the grid) + fly; one universal controller drives whichever is active. F toggles.
        orbitCam = new OrbitCamera { Target = new Vector3(0f, 0.6f, -0.75f), Distance = 16f };
        flyCam = new FlyCamera { Position = new Vector3(0f, 4f, 18f) };
        scene.Camera = orbitCam;

        input = window.CreateInput();
        var mouse = input.Mice[0];
        var keyboard = input.Keyboards[0];
        controller = new CameraController(orbitCam, keyboard, mouse);
        keyboard.KeyDown += (_, key, _) => { if (key == Key.F) ToggleCamera(); };   // F = swap orbit/fly
    }
    unsafe void SetMinimumSize(int minW, int minH)
    {
        if (window.Native?.Glfw is nint handle)
        {
            var glfw = Silk.NET.GLFW.GlfwProvider.GLFW.Value;
            glfw.SetWindowSizeLimits((Silk.NET.GLFW.WindowHandle*)handle, minW, minH,
                Silk.NET.GLFW.Glfw.DontCare, Silk.NET.GLFW.Glfw.DontCare);
        }
    }
    void ToggleCamera()
    {
        usingFly = !usingFly;
        if (usingFly) { scene.Camera = flyCam; controller.Camera = flyCam; }
        else { scene.Camera = orbitCam; controller.Camera = orbitCam; }
    }
    void OnUpdate(double dt)
    {
        scene.Update((float)dt);
        controller.Update((float)dt);
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
