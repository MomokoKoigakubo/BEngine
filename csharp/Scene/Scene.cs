using System.Numerics;
using IdleL.ECS;
using IdleL.Resources;

namespace IdleL.Scenes;

// Pure game state: what to draw + the view. No Vulkan. Entities populate the ECS registry.
class Scene
{
    public OrbitCamera Camera = new();
    readonly Registry registry = new();
    float time;

    public Registry Registry => registry;
    public float Time => time;
    public void Update(float dt) => time += dt;

    public void Add(MeshHandle mesh, Matrix4x4? transform = null)
    {
        Entity e = registry.Create();
        registry.Add(e, new Transform { Matrix = transform ?? Matrix4x4.Identity });
        registry.Add(e, new MeshRenderable { Mesh = mesh });
    }
}
