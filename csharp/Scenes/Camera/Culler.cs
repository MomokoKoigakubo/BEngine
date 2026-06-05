using System.Numerics;
using IdleL.ECS;

namespace IdleL.Scenes;

// The culling seam: decides which entities are visible for a frustum. The renderer only ever calls
// this, so the strategy is swappable. brute force scan today, octree/grid later, with
// no renderer change. It works on pure scene data (Transform + Bounds), never GPU resources.
interface ICuller
{
    void GatherVisible(Frustum frustum, List<Entity> result);
}

// Tests every entity against the frustum: O(n). Fine to ~100k; replace with a spatial structure beyond that. maybe octree
class BruteForceCuller : ICuller
{
    readonly Registry registry;
    readonly List<Entity> candidates = new();   // reused

    public BruteForceCuller(Registry registry) => this.registry = registry;

    public void GatherVisible(Frustum frustum, List<Entity> result)
    {
        result.Clear();
        registry.View<Transform, Bounds>(candidates);
        foreach (Entity e in candidates)
        {
            Bounds b = registry.Get<Bounds>(e);
            Matrix4x4 model = registry.Get<Transform>(e).Matrix;
            Vector3 worldCenter = Vector3.Transform(b.Center, model);   // instances are translation-only -> radius unchanged
            if (frustum.Intersects(worldCenter, b.Radius))
                result.Add(e);
        }
    }
}
