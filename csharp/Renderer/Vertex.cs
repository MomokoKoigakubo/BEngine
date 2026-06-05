using System.Numerics;
using System.Runtime.InteropServices;

namespace IdleL.Rendering;

// CPU-side vertex; uploaded as raw bytes to the GPU vertex buffer. Field order = shader input
// locations 0..4 (pos, normal, uv, texIndex, boneIndex). Sequential layout => 40-byte stride,
// matching the C++ struct. Vulkan binding/attribute descriptions get added when the renderer is ported.
[StructLayout(LayoutKind.Sequential)]
struct Vertex
{
    public Vector3 Pos;
    public Vector3 Normal;
    public Vector2 Uv;
    public float TexIndex;
    public float BoneIndex;
}
