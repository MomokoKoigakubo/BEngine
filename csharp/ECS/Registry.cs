namespace IdleL.ECS;

// Entity indices are handed out, recycled through a free list on destroy, and version-stamped
// with a generation so stale handles fail Valid(). Component storage lives in per-type pools.
class Registry
{
    readonly List<uint> generations = new();                       // generations[i] = current gen of slot i
    readonly List<uint> freeList = new();                          // destroyed indices, recyclable
    readonly Dictionary<Type, IComponentPool> pools = new();       // replaces C++ type_index map

    public void Add<T>(Entity e, T c) => PoolFor<T>().Add(e, c);
    public bool Has<T>(Entity e) => PoolFor<T>().Has(e);
    public ref T Get<T>(Entity e) => ref PoolFor<T>().Get(e);
    public void Remove<T>(Entity e) => PoolFor<T>().Remove(e);

    public Entity Create()
    {
        if (freeList.Count > 0)
        {
            uint last = freeList[^1];
            freeList.RemoveAt(freeList.Count - 1);
            return new Entity(last, generations[(int)last]);
        }
        uint index = (uint)generations.Count;
        generations.Add(0);
        return new Entity(index, generations[(int)index]);
    }

    public bool Valid(Entity e) =>
        e.Index < generations.Count && generations[(int)e.Index] == e.Generation;

    public void Destroy(Entity e)
    {
        if (!Valid(e)) return;
        foreach (var pool in pools.Values) pool.Remove(e);
        generations[(int)e.Index]++;          // bump gen so old handles go stale
        freeList.Add(e.Index);
    }

    // Iterate one pool's dense entities and rebuild full handles. (C# has no variadic generics,
    // so the C++ view<First, Rest...> becomes fixed-arity overloads.)
    public List<Entity> View<T>()
    {
        var result = new List<Entity>();
        foreach (uint idx in PoolFor<T>().Entities)
            result.Add(new Entity(idx, generations[(int)idx]));
        return result;
    }

    public List<Entity> View<T1, T2>()
    {
        var result = new List<Entity>();
        View<T1, T2>(result);
        return result;
    }

    // Fills a caller-owned list (cleared first) so the hot path allocates nothing. Indexes the dense
    // list rather than foreach-ing the IReadOnlyList, which would box a struct enumerator.
    public void View<T1, T2>(List<Entity> result)
    {
        result.Clear();
        var entities = PoolFor<T1>().Entities;
        for (int i = 0; i < entities.Count; i++)
        {
            uint idx = entities[i];
            var e = new Entity(idx, generations[(int)idx]);
            if (Has<T2>(e)) result.Add(e);
        }
    }

    ComponentPool<T> PoolFor<T>()
    {
        if (pools.TryGetValue(typeof(T), out var p)) return (ComponentPool<T>)p;
        var pool = new ComponentPool<T>();
        pools[typeof(T)] = pool;
        return pool;
    }
}
