using System.Numerics;
using IdleL.ECS;
using IdleL.Resources;

namespace IdleL.Scenes;

// Pure game state: what to draw + the view. No Vulkan. Entities populate the ECS registry.
class Scene
{
    public Camera Camera = new OrbitCamera();   // base type so it can hold any camera (swappable)
    readonly Registry registry = new();
    public ICuller Culler;   // brute force now, swap to an octree/grid later, renderer never changes
    float time;

    public Scene() => Culler = new BruteForceCuller(registry);

    public Registry Registry => registry;
    public float Time => time;
    public void Update(float dt) => time += dt;

    public void GatherVisible(Frustum frustum, List<Entity> result) => Culler.GatherVisible(frustum, result);

    public void Add(MeshHandle mesh, Bounds bounds, Matrix4x4? transform = null)
    {
        Entity e = registry.Create();
        registry.Add(e, new Transform { Matrix = transform ?? Matrix4x4.Identity });
        registry.Add(e, new MeshRenderable { Mesh = mesh });
        registry.Add(e, bounds);
    }

    // Static world geometry (terrain / props): unique meshes drawn once each, CPU frustum-culled.
    // This is the coworker's terrain entry-point. bound = world-space sphere around the mesh.
    public struct StaticObject { public int Id; public MeshHandle Mesh; public Matrix4x4 Model; public Vector3 BoundCenter; public float BoundRadius; }
    public readonly List<StaticObject> StaticObjects = new();
    int nextStaticId;

    // returns a handle; pass it to RemoveStatic to stream this chunk back out.
    public int AddStatic(MeshHandle mesh, Matrix4x4 model, Vector3 boundCenter, float boundRadius)
    {
        int id = nextStaticId++;
        StaticObjects.Add(new StaticObject { Id = id, Mesh = mesh, Model = model, BoundCenter = boundCenter, BoundRadius = boundRadius });
        return id;
    }

    // stops drawing the chunk. NOTE: freeing its mesh GPU memory SAFELY (deferred deletion past the
    // in-flight frames) is a separate follow-up; this only removes it from the render list.
    public void RemoveStatic(int id)
    {
        int idx = StaticObjects.FindIndex(o => o.Id == id);
        if (idx < 0) return;
        StaticObjects[idx] = StaticObjects[^1];   // swap-remove (draw order doesn't matter)
        StaticObjects.RemoveAt(StaticObjects.Count - 1);
    }
}
