using System.Numerics;
using IdleL.ECS;

// Validates the ECS port: create/add, multi-component view, ref-get in-place mutation,
// destroy invalidation, and index recycling with generation bump.
static class EcsTest
{
    public static void Run()
    {
        var reg = new Registry();
        var a = reg.Create();
        var b = reg.Create();
        var c = reg.Create();

        reg.Add(a, new Transform { Matrix = Matrix4x4.CreateTranslation(1, 0, 0) });
        reg.Add(b, new Transform { Matrix = Matrix4x4.CreateTranslation(2, 0, 0) });
        reg.Add(c, new Transform { Matrix = Matrix4x4.CreateTranslation(3, 0, 0) });
        reg.Add(a, new MeshRenderable());
        reg.Add(c, new MeshRenderable());

        var view = reg.View<Transform, MeshRenderable>();
        Line($"view<Transform,MeshRenderable> = {view.Count}", view.Count == 2, 2);

        // ref-get: mutate the stored component in place (proves Get returns a real reference, not a copy)
        reg.Get<Transform>(b).Matrix = Matrix4x4.CreateTranslation(9, 0, 0);
        float bx = reg.Get<Transform>(b).Matrix.Translation.X;
        Line($"ref-get mutate b.x = {bx}", bx == 9, 9);

        uint bIndex = b.Index;
        reg.Destroy(b);
        Line($"destroyed b valid? {reg.Valid(b)}", !reg.Valid(b), false);

        var d = reg.Create();   // recycles b's index with generation+1
        Line($"recycled idx {d.Index} gen {d.Generation}", d.Index == bIndex && d.Generation == 1, "idx reuse, gen 1");

        var tview = reg.View<Transform>();   // a + c remain (b removed, d has none)
        Line($"View<Transform> = {tview.Count}", tview.Count == 2, 2);

        Console.WriteLine("ecs: done");
    }

    static void Line(string label, bool ok, object expect) =>
        Console.WriteLine($"  ecs: {label,-40} (expect {expect})  {(ok ? "OK" : "FAIL")}");
}
