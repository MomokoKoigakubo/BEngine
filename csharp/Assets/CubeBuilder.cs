using System.Numerics;
using IdleL.BBModel;
using IdleL.Molang;
using IdleL.Rendering;

namespace IdleL.Assets;

struct Bone
{
    public string Uuid;
    public int ParentIndex;       // -1 = root
    public Vector3 Origin;        // group origin (pixels)
    public Vector3 Rotation;      // base rotation (deg); anim adds to this
    public Matrix4x4 BindMatrix;  // accumulated bind-pose transform
}

static class CubeBuilder
{
    // GLM is col-major/col-vector (M*v); System.Numerics is row-major/row-vector (v*M). M_sn = M_glm^T,
    // so every GLM product is REVERSED and each factor uses its Create* equivalent (which is its transpose).

    static Matrix4x4 EulerRot(Vector3 rotationDeg, bool eulerXYZ)
    {
        float rx = float.DegreesToRadians(rotationDeg.X);
        float ry = float.DegreesToRadians(rotationDeg.Y);
        float rz = float.DegreesToRadians(rotationDeg.Z);
        // glm XYZ = Rx*Ry*Rz -> reversed Rz*Ry*Rx ; glm ZYX = Rz*Ry*Rx -> reversed Rx*Ry*Rz
        return eulerXYZ
            ? Matrix4x4.CreateRotationZ(rz) * Matrix4x4.CreateRotationY(ry) * Matrix4x4.CreateRotationX(rx)
            : Matrix4x4.CreateRotationX(rx) * Matrix4x4.CreateRotationY(ry) * Matrix4x4.CreateRotationZ(rz);
    }

    // ABSOLUTE geometry (cube from/to, groups): rotate about the pivot in place.
    // glm T(o)*R*T(-o) -> reversed.
    static Matrix4x4 PivotTransform(Vector3 originPx, Vector3 rotationDeg, bool eulerXYZ)
    {
        Vector3 origin = originPx / 16f;
        return Matrix4x4.CreateTranslation(-origin)
             * EulerRot(rotationDeg, eulerXYZ)
             * Matrix4x4.CreateTranslation(origin);
    }

    static void AddQuad(List<Vertex> verts, List<uint> indices,
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
        Vector3 normal, Matrix4x4 m,
        Vector2 uvMin, Vector2 uvMax, float texIndex, int rotation, float boneIndex)
    {
        Vector3 n = Vector3.Normalize(Vector3.TransformNormal(normal, m));   // rotation part, for the normal
        uint baseIdx = (uint)verts.Count;                                    // index of p0

        // p0..p3 are CCW from outside; UV rect corners, cyclically shifted by the CubeFace rotation.
        Vector2[] uvs =
        {
            new(uvMin.X, uvMax.Y),   // p0 at rotation 0
            new(uvMax.X, uvMax.Y),   // p1
            new(uvMax.X, uvMin.Y),   // p2
            new(uvMin.X, uvMin.Y),   // p3
        };
        int steps = ((rotation / 90) % 4 + 4) % 4;   // 0..3, handles negatives

        verts.Add(new Vertex { Pos = Vector3.Transform(p0, m), Normal = n, Uv = uvs[(0 + steps) % 4], TexIndex = texIndex, BoneIndex = boneIndex });
        verts.Add(new Vertex { Pos = Vector3.Transform(p1, m), Normal = n, Uv = uvs[(1 + steps) % 4], TexIndex = texIndex, BoneIndex = boneIndex });
        verts.Add(new Vertex { Pos = Vector3.Transform(p2, m), Normal = n, Uv = uvs[(2 + steps) % 4], TexIndex = texIndex, BoneIndex = boneIndex });
        verts.Add(new Vertex { Pos = Vector3.Transform(p3, m), Normal = n, Uv = uvs[(3 + steps) % 4], TexIndex = texIndex, BoneIndex = boneIndex });

        // two triangles, CCW from outside: (0,1,2) and (2,3,0)
        indices.Add(baseIdx + 0); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
        indices.Add(baseIdx + 2); indices.Add(baseIdx + 3); indices.Add(baseIdx + 0);
    }

    // Builds one cube ELEMENT-LOCAL (un-baked), stamping boneIndex on every vertex.
    public static void BuildCube(Element e, List<Vertex> verts, List<uint> indices,
        Resolution res, bool eulerXYZ, int boneIndex)
    {
        Vector3 a = e.From / 16f;
        Vector3 b = e.To / 16f;
        Matrix4x4 model = PivotTransform(e.Origin, e.Rotation, eulerXYZ);   // element-local; bone matrix applies the hierarchy

        Vector2 texSize = new(res.Width, res.Height);
        Vector2 UvMin(CubeFace f) => new Vector2(f.U0, f.V0) / texSize;
        Vector2 UvMax(CubeFace f) => new Vector2(f.U1, f.V1) / texSize;
        float TexIdx(CubeFace f) => f.Texture < 0 ? 0 : f.Texture;

        if (e.Up.Present)    // UP (+Y)
            AddQuad(verts, indices, new(a.X, b.Y, b.Z), new(b.X, b.Y, b.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z),
                new(0, 1, 0), model, UvMin(e.Up), UvMax(e.Up), TexIdx(e.Up), e.Up.Rotation, boneIndex);
        if (e.Down.Present)  // DOWN (-Y)
            AddQuad(verts, indices, new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, a.Y, b.Z), new(a.X, a.Y, b.Z),
                new(0, -1, 0), model, UvMin(e.Down), UvMax(e.Down), TexIdx(e.Down), e.Down.Rotation, boneIndex);
        if (e.South.Present) // SOUTH (+Z)
            AddQuad(verts, indices, new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z),
                new(0, 0, 1), model, UvMin(e.South), UvMax(e.South), TexIdx(e.South), e.South.Rotation, boneIndex);
        if (e.North.Present) // NORTH (-Z)
            AddQuad(verts, indices, new(b.X, a.Y, a.Z), new(a.X, a.Y, a.Z), new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z),
                new(0, 0, -1), model, UvMin(e.North), UvMax(e.North), TexIdx(e.North), e.North.Rotation, boneIndex);
        if (e.East.Present)  // EAST (+X)
            AddQuad(verts, indices, new(b.X, a.Y, b.Z), new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(b.X, b.Y, b.Z),
                new(1, 0, 0), model, UvMin(e.East), UvMax(e.East), TexIdx(e.East), e.East.Rotation, boneIndex);
        if (e.West.Present)  // WEST (-X)
            AddQuad(verts, indices, new(a.X, a.Y, a.Z), new(a.X, a.Y, b.Z), new(a.X, b.Y, b.Z), new(a.X, b.Y, a.Z),
                new(-1, 0, 0), model, UvMin(e.West), UvMax(e.West), TexIdx(e.West), e.West.Rotation, boneIndex);
    }

    public static void BuildMesh(Element e, List<Vertex> verts, List<uint> indices,
        Resolution res, bool eulerXYZ, int boneIndex)
    {
        // Mesh vertices are LOCAL to the element origin (unlike cube from/to), so place them AT the
        // origin rotated -> glm T(origin)*R (no T(-origin) pivot) -> reversed: R*T(origin).
        Vector3 origin = e.Origin / 16f;
        Matrix4x4 model = EulerRot(e.Rotation, eulerXYZ) * Matrix4x4.CreateTranslation(origin);
        Vector2 texSize = new(res.Width, res.Height);

        foreach (var (_, face) in e.Faces)
        {
            int n = face.Vertices.Count;
            if (n < 3) continue;

            var wp = new Vector3[n];
            var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                string name = face.Vertices[i];
                Vector3 local = e.Vertices[name] / 16f;
                wp[i] = Vector3.Transform(local, model);
                uv[i] = face.Uv[name] / texSize;
            }

            Vector3 nrm = Vector3.Normalize(Vector3.Cross(wp[1] - wp[0], wp[2] - wp[0]));   // from the first triangle
            float texIndex = face.Texture < 0 ? 0 : face.Texture;
            uint baseIdx = (uint)verts.Count;

            for (int i = 0; i < n; i++)
                verts.Add(new Vertex { Pos = wp[i], Normal = nrm, Uv = uv[i], TexIndex = texIndex, BoneIndex = boneIndex });

            for (int i = 1; i + 1 < n; i++)   // triangulate as a fan
            {
                indices.Add(baseIdx + 0);
                indices.Add(baseIdx + (uint)i);
                indices.Add(baseIdx + (uint)i + 1);
            }
        }
    }

    // Walks the outliner hierarchy, composing group transforms, and builds every element into one
    // shared verts/indices list. bones[0] is a dummy identity root.
    public static void BuildModel(BBModelParts model, List<Vertex> verts, List<uint> indices, List<Bone> bones)
    {
        var groupMap = new Dictionary<string, Group>();
        var elemMap = new Dictionary<string, Element>();
        foreach (var g in model.Groups) groupMap[g.Uuid] = g;
        foreach (var e in model.Elements) elemMap[e.Uuid] = e;

        bones.Add(new Bone { ParentIndex = -1, BindMatrix = Matrix4x4.Identity });

        void Walk(OutlinerNode node, Matrix4x4 parent, int currentBone)
        {
            if (node.IsGroup)
            {
                Matrix4x4 acc = parent;
                int boneIndex = currentBone;                      // default: stay in the parent's bone
                if (groupMap.TryGetValue(node.Uuid, out var g))
                {
                    acc = PivotTransform(g.Origin, g.Rotation, model.EulerXYZ) * parent;   // glm parent*pivot reversed
                    boneIndex = bones.Count;                      // this group's new bone index
                    bones.Add(new Bone { Uuid = g.Uuid, ParentIndex = currentBone, Origin = g.Origin, Rotation = g.Rotation, BindMatrix = acc });
                }
                foreach (var child in node.Children)
                    Walk(child, acc, boneIndex);                  // children belong to THIS bone
            }
            else if (elemMap.TryGetValue(node.Uuid, out var el))
            {
                if (el.Type == ElementType.Cube)
                    BuildCube(el, verts, indices, model.Res, model.EulerXYZ, currentBone);
                else if (el.Type == ElementType.Mesh)
                    BuildMesh(el, verts, indices, model.Res, model.EulerXYZ, currentBone);
            }
        }

        foreach (var node in model.Outliner)
            Walk(node, Matrix4x4.Identity, 0);
    }

    // Bind-pose bounding sphere (model space) for frustum culling: pose each vertex by its bone's
    // bind matrix (matches how the shader skins at rest), then fit a sphere. Padded for animation.
    public static void ComputeBounds(List<Vertex> verts, List<Bone> bones, out Vector3 center, out float radius)
    {
        if (verts.Count == 0) { center = Vector3.Zero; radius = 0f; return; }
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var v in verts)
        {
            Vector3 p = Vector3.Transform(v.Pos, bones[(int)v.BoneIndex].BindMatrix);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        center = (min + max) * 0.5f;
        float r2 = 0f;
        foreach (var v in verts)
            r2 = MathF.Max(r2, Vector3.DistanceSquared(center, Vector3.Transform(v.Pos, bones[(int)v.BoneIndex].BindMatrix)));
        radius = MathF.Sqrt(r2) * 1.3f;   // padding for animation moving verts past the bind pose
    }

    // A bone's animated LOCAL transform. glm T(origin+animPos)*R*S*T(-origin) -> reversed.
    // Reduces to PivotTransform(origin, baseRot) at zero offset.
    static Matrix4x4 BoneLocalAnimated(Bone b, BonePose pose, bool eulerXYZ)
    {
        Vector3 origin  = b.Origin / 16f;
        Vector3 animPos = pose.Position / 16f;          // position keyframes are in pixels, like origin
        Vector3 rotDeg  = b.Rotation + pose.Rotation;   // Blockbench ADDS the animated euler to the base
        return Matrix4x4.CreateTranslation(-origin)
             * Matrix4x4.CreateScale(pose.Scale)
             * EulerRot(rotDeg, eulerXYZ)
             * Matrix4x4.CreateTranslation(origin + animPos);
    }

    // Reused across frames; animation sampling is single-threaded (main-loop OnUpdate).
    static readonly MolangContext SharedAnimCtx = new();

    // Allocating convenience overload (tests / one-off use).
    public static Matrix4x4[] ComputeBoneMatrices(List<Bone> bones, Animation anim, float time, bool eulerXYZ)
    {
        var world = new Matrix4x4[bones.Count];
        ComputeBoneMatrices(bones, anim, time, eulerXYZ, world);
        return world;
    }

    // Compose the animated WORLD bone matrices for `anim` at `time` into a caller-owned `world`
    // (length >= bones.Count) for the bone SSBO. bones must be in hierarchy order (parent before
    // child). Empty/zero anim -> bind pose. No per-frame heap allocations: the uuid->animator lookup
    // is cached on the clip and the MolangContext is reused.
    public static void ComputeBoneMatrices(List<Bone> bones, Animation anim, float time, bool eulerXYZ, Matrix4x4[] world)
    {
        var animators = anim.AnimatorsByUuid;   // built once, cached on the clip
        SharedAnimCtx.AnimTime = time;           // so molang query.anim_time resolves to the playhead
        for (int i = 0; i < bones.Count; i++)
        {
            Bone b = bones[i];
            BonePose pose = new();   // default = zero offset (scale 1) -> bind pose for un-animated bones
            if (b.Uuid != null && animators.TryGetValue(b.Uuid, out var ba))
                pose = AnimSampler.SampleBone(ba, time, SharedAnimCtx);

            Matrix4x4 local = BoneLocalAnimated(b, pose, eulerXYZ);
            Matrix4x4 parentMat = b.ParentIndex >= 0 ? world[b.ParentIndex] : Matrix4x4.Identity;
            world[i] = local * parentMat;   // glm parentMat*local reversed; parent registered before child
        }
    }
}
