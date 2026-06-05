using System.Numerics;
using IdleL.Assets;
using IdleL.BBModel;
using IdleL.Rendering;

// Validates geometry build + bone-matrix composition. Key check: the BIND-POSE INVARIANT — a
// zero/empty animation must reproduce each bone's stored bind matrix, which only holds if every
// matrix multiply-order (GLM->System.Numerics reversal) is internally consistent.
static class CubeBuilderTest
{
    public static void Run()
    {
        string path = AssetPaths.Model("momoko.bbmodel");
        if (!File.Exists(path)) { Console.WriteLine($"cubebuilder: model not found at {path}"); return; }
        var model = BBModelLoader.Load(File.ReadAllText(path));

        var verts = new List<Vertex>();
        var indices = new List<uint>();
        var bones = new List<Bone>();
        CubeBuilder.BuildModel(model, verts, indices, bones);

        Console.WriteLine($"  cubebuilder: {verts.Count} verts, {indices.Count} indices, {bones.Count} bones");
        Line("bones == groups+1", bones.Count == model.Groups.Count + 1);
        Line("verts > 0", verts.Count > 0);
        Line("indices % 3 == 0", indices.Count % 3 == 0);

        // bind-pose invariant: zero/empty animation must reproduce each bone's stored bind matrix
        var world = CubeBuilder.ComputeBoneMatrices(bones, new Animation(), 0f, model.EulerXYZ);
        float maxErr = 0f;
        for (int i = 0; i < bones.Count; i++)
            maxErr = MathF.Max(maxErr, MatDiff(world[i], bones[i].BindMatrix));
        Console.WriteLine($"  cubebuilder: bind-pose invariant maxErr = {maxErr:0.000000}  {(maxErr < 1e-4f ? "OK" : "FAIL")}");

        // smoke: a real animation should actually move bones between t=0 and t=1, with no NaNs
        var anim = model.Animations.Find(a => a.Name == "idle_step") ?? model.Animations[0];
        var w0 = CubeBuilder.ComputeBoneMatrices(bones, anim, 0f, model.EulerXYZ);
        var w1 = CubeBuilder.ComputeBoneMatrices(bones, anim, 1f, model.EulerXYZ);
        float moved = 0f; bool finite = true;
        for (int i = 0; i < bones.Count; i++)
        {
            moved = MathF.Max(moved, MatDiff(w0[i], w1[i]));
            if (!IsFinite(w0[i]) || !IsFinite(w1[i])) finite = false;
        }
        Console.WriteLine($"  cubebuilder: anim '{anim.Name}' t0 vs t1 maxDelta = {moved:0.000} finite={finite}  {(moved > 0f && finite ? "OK" : "FAIL")}");

        Console.WriteLine("cubebuilder: done");
    }

    static void Line(string label, bool ok) =>
        Console.WriteLine($"  cubebuilder: {label,-22} {(ok ? "OK" : "FAIL")}");

    static float MatDiff(Matrix4x4 a, Matrix4x4 b)
    {
        float[] x = ToArr(a), y = ToArr(b);
        float m = 0;
        for (int i = 0; i < 16; i++) m = MathF.Max(m, MathF.Abs(x[i] - y[i]));
        return m;
    }
    static bool IsFinite(Matrix4x4 a) { foreach (float f in ToArr(a)) if (!float.IsFinite(f)) return false; return true; }
    static float[] ToArr(Matrix4x4 m) => new[]
    { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
      m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
}
