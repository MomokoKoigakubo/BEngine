using System.Globalization;
using System.Numerics;
using IdleL.Assets;
using IdleL.BBModel;
using IdleL.Molang;

// Validates the animation sampler: clamping, Step hold, Linear lerp, Catmull-Rom control-point
// pass-through, and empty-channel fallback.
static class AnimSamplerTest
{
    public static void Run()
    {
        var ctx = new MolangContext();

        var lin = new List<KeyFrame> { Kf(0, Interp.Linear, 0), Kf(1, Interp.Linear, 10) };
        Check("linear @0.5",            Sample(lin, 0.5f, ctx),  5);
        Check("linear clamp-before",    Sample(lin, -1f, ctx),   0);
        Check("linear clamp-after",     Sample(lin, 2f, ctx),   10);

        var step = new List<KeyFrame> { Kf(0, Interp.Step, 0), Kf(1, Interp.Linear, 10) };
        Check("step @0.5 holds before", Sample(step, 0.5f, ctx), 0);

        var cat = new List<KeyFrame>
            { Kf(0, Interp.Catmullrom, 0), Kf(1, Interp.Catmullrom, 10),
              Kf(2, Interp.Catmullrom, 20), Kf(3, Interp.Catmullrom, 30) };
        Check("catmull @1.0 = ctrl pt", Sample(cat, 1.0f, ctx), 10);

        float fb = AnimSampler.SampleChannel(new List<KeyFrame>(), 0.5f, ctx, new Vector3(7, 0, 0)).X;
        Check("empty -> fallback",      fb, 7);

        Console.WriteLine("sampler: done");
    }

    static float Sample(List<KeyFrame> kfs, float t, MolangContext ctx) =>
        AnimSampler.SampleChannel(kfs, t, ctx, Vector3.Zero).X;

    static KeyFrame Kf(float time, Interp interp, float x) => new()
    {
        Time = time,
        Interp = interp,
        X = MolangExpr.Compile(x.ToString(CultureInfo.InvariantCulture)),
    };

    static void Check(string label, float got, float expect)
    {
        bool ok = MathF.Abs(got - expect) < 1e-4f;
        Console.WriteLine($"  sampler: {label,-26} = {got,6:0.###} (expect {expect})  {(ok ? "OK" : "FAIL")}");
    }
}
