using System.Numerics;
using IdleL.BBModel;
using IdleL.Molang;

namespace IdleL.Assets;

// Per-bone local animation offsets at a given time.
struct BonePose
{
    public Vector3 Rotation = Vector3.Zero;   // euler DEGREES, added to the bone's base rotation
    public Vector3 Position = Vector3.Zero;   // added to the bone's origin
    public Vector3 Scale = Vector3.One;       // multiplied with the base scale
    public BonePose() { }
}

static class AnimSampler
{
    static Vector3 KfValue(KeyFrame k, MolangContext ctx) =>
        new(k.X.Eval(ctx), k.Y.Eval(ctx), k.Z.Eval(ctx));

    // Sample one time-sorted channel at `time`. The segment's mode comes from the keyframe it
    // starts FROM (the "before" one). Returns `fallback` for an empty channel.
    // NOTE: caller sets ctx.AnimTime = time first, so molang query.anim_time reads the right value.
    public static Vector3 SampleChannel(List<KeyFrame> kfs, float time, MolangContext ctx, Vector3 fallback)
    {
        if (kfs.Count == 0)        return fallback;
        if (time <= kfs[0].Time)   return KfValue(kfs[0], ctx);    // clamp before first
        if (time >= kfs[^1].Time)  return KfValue(kfs[^1], ctx);   // clamp after last

        // segment [i, i+1] with kfs[i].time <= time < kfs[i+1].time
        int i = 0;
        while (i + 1 < kfs.Count && kfs[i + 1].Time <= time) i++;

        KeyFrame before = kfs[i];
        KeyFrame after  = kfs[i + 1];
        float span = after.Time - before.Time;
        float t = span > 0f ? (time - before.Time) / span : 0f;   // 0..1 within the segment

        Vector3 b = KfValue(before, ctx);
        Vector3 a = KfValue(after, ctx);

        switch (before.Interp)
        {
            case Interp.Step:
                return b;                       // hold the before value until the next keyframe

            case Interp.Catmullrom:
            {
                // 4 control points; clamp at the ends (P0=P1 at start, P3=P2 at end)
                KeyFrame k0 = i > 0              ? kfs[i - 1] : before;
                KeyFrame k3 = i + 2 < kfs.Count ? kfs[i + 2] : after;
                Vector3 p0 = KfValue(k0, ctx), p1 = b, p2 = a, p3 = KfValue(k3, ctx);
                float t2 = t * t, t3 = t2 * t;
                return 0.5f * ((2f * p1)
                             + (-p0 + p2) * t
                             + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                             + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
            }

            case Interp.Bezier:   // TODO: real cubic bezier with per-axis handles; linear for now, maybe we want it maybe not TBD
            case Interp.Linear:
            default:
                return b + (a - b) * t;
        }
    }

    public static BonePose SampleBone(BoneAnimator ba, float time, MolangContext ctx)
    {
        var p = new BonePose();
        p.Rotation = SampleChannel(ba.Rotation, time, ctx, Vector3.Zero);
        p.Position = SampleChannel(ba.Position, time, ctx, Vector3.Zero);
        p.Scale    = SampleChannel(ba.Scale,    time, ctx, Vector3.One);   // scale defaults to 1
        return p;
    }
}
