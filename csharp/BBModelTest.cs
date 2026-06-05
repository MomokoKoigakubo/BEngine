using IdleL.BBModel;
using IdleL.Molang;

// Validates the bbmodel port — same momoko parse check as the C++ version (counts must match).
static class BBModelTest
{
    public static void Run()
    {
        string path = AssetPaths.Model("momoko.bbmodel");
        if (!File.Exists(path)) { Console.WriteLine($"bbmodel: model not found at {path}"); return; }

        var parts = BBModelLoader.Load(File.ReadAllText(path));
        Console.WriteLine($"bbmodel: {parts.Animations.Count} animations  ({parts.Elements.Count} elements, {parts.Groups.Count} groups)");

        var ctx = new MolangContext();
        foreach (var a in parts.Animations)
        {
            int total = 0;
            foreach (var ba in a.Animators)
                total += ba.Rotation.Count + ba.Position.Count + ba.Scale.Count;
            Console.WriteLine($"  {a.Name,-16} loop={a.Loop} len={a.Length:0.00} bones={a.Animators.Count} kf={total}");
        }

        // spot-check molang eval through a keyframe (animation3 first rot kf x should be -17.5)
        foreach (var a in parts.Animations)
            foreach (var ba in a.Animators)
                if (ba.Rotation.Count > 0)
                {
                    var k = ba.Rotation[0];
                    Console.WriteLine($"  spot-check '{a.Name}' first rot kf @t={k.Time:0.00} -> x={k.X.Eval(ctx)} y={k.Y.Eval(ctx)} z={k.Z.Eval(ctx)}");
                    return;
                }
    }
}
