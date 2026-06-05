namespace IdleL.Resources;

// Opaque indices into ResourceManager's arrays. Default = invalid.
struct MeshHandle
{
    public uint Id = uint.MaxValue;
    public MeshHandle() { }
}

struct TextureHandle
{
    public uint Id = uint.MaxValue;
    public TextureHandle() { }
}
