using System.Numerics;
using IdleL.Resources;

namespace IdleL.ECS;

// ECS components are plain DATA (no logic). An entity "is" whichever of these it has; systems read them.
// Add a new kind of thing = add a component + a system, touch nothing else.

// World transform. mat4 for now (matches the old Renderable, render swap is 1:1).
// LATER: becomes TRS (Vector3 pos; Quaternion rot; Vector3 scale) once animation/instancing
// need to interpolate + un-bake transforms.
struct Transform
{
    public Matrix4x4 Matrix = Matrix4x4.Identity;
    public Transform() { }   // field initializer only runs via explicit ctor; `new Transform()` => identity
}

// Marks an entity drawable: which mesh to render (ResourceManager owns the mesh data).
struct MeshRenderable
{
    public MeshHandle Mesh;
}
