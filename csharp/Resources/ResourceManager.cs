using IdleL.Rendering;

namespace IdleL.Resources;

// Owns GPU meshes + textures, handing out opaque handles. Textures register into the renderer's
// bindless array on load.
class ResourceManager : IDisposable
{
    readonly Renderer renderer;
    readonly List<Mesh> meshes = new();
    readonly List<Texture> textures = new();

    public ResourceManager(Renderer renderer) => this.renderer = renderer;

    public MeshHandle CreateMesh(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices)
    {
        uint id = (uint)meshes.Count;
        meshes.Add(new Mesh(renderer.Device, vertices, indices));
        return new MeshHandle { Id = id };
    }

    public TextureHandle LoadTexture(string path)
    {
        var tex = new Texture(renderer.Device, path);
        uint slot = renderer.RegisterBindlessTexture(tex.View, tex.Sampler);
        textures.Add(tex);
        return new TextureHandle { Id = slot };
    }

    public int TextureWidth(TextureHandle h) => textures[(int)h.Id].Width;
    public int TextureHeight(TextureHandle h) => textures[(int)h.Id].Height;
    public void RegisterFlipbook(TextureHandle h, float frameCount, float flipRate) =>
        renderer.SetFlipbook(h.Id, frameCount, flipRate);

    public Mesh GetMesh(MeshHandle h) => meshes[(int)h.Id];

    public void Dispose()
    {
        foreach (var m in meshes) m.Dispose();
        foreach (var t in textures) t.Dispose();
    }
}
