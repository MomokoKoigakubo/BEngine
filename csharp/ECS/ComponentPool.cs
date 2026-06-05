using System.Runtime.InteropServices;

namespace IdleL.ECS;

interface IComponentPool
{
    void Remove(Entity e);
}

// Sparse set: dense[] packs live entity indices (cache-friendly iteration), sparse[] maps
// entity index -> packed position, components[] runs parallel to dense[].
class ComponentPool<T> : IComponentPool
{
    readonly List<uint> sparse = new();      // entity index -> packed pos (or InvalidIndex)
    readonly List<uint> dense = new();       // packed entity indices
    readonly List<T> components = new();     // parallel to dense

    public IReadOnlyList<uint> Entities => dense;

    public bool Has(Entity e)
    {
        if (e.Index >= sparse.Count) return false;
        uint pos = sparse[(int)e.Index];
        return pos < dense.Count && dense[(int)pos] == e.Index;   // dense re-check guards stale sparse slots
    }

    public void Add(Entity e, T component)
    {
        if (Has(e)) { components[(int)sparse[(int)e.Index]] = component; return; }   // dup guard: overwrite
        uint pos = (uint)dense.Count;
        while (sparse.Count <= e.Index) sparse.Add(Entity.InvalidIndex);             // grow sparse
        dense.Add(e.Index);
        components.Add(component);
        sparse[(int)e.Index] = pos;
    }

    // ref return preserves the C++ `T&` so systems mutate the stored component in place
    // (not a copy). Invalidated by a subsequent Add/Remove resize — same caveat as a C++ vector ref.
    public ref T Get(Entity e) => ref CollectionsMarshal.AsSpan(components)[(int)sparse[(int)e.Index]];

    public void Remove(Entity e)
    {
        if (!Has(e)) return;
        int p = (int)sparse[(int)e.Index];   // victim position
        int last = dense.Count - 1;          // swap last element into the hole, then pop
        dense[p] = dense[last];
        components[p] = components[last];
        sparse[(int)dense[p]] = (uint)p;
        dense.RemoveAt(last);
        components.RemoveAt(last);
        sparse[(int)e.Index] = Entity.InvalidIndex;
    }
}
