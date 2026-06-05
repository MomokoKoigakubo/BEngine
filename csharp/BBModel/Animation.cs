using IdleL.Molang;

namespace IdleL.BBModel;

enum LoopMode { Once, Loop, Hold }
enum Interp { Step, Linear, Catmullrom, Bezier }

class KeyFrame
{
    public float Time;
    public Interp Interp = Interp.Linear;
    public MolangExpr X = new(), Y = new(), Z = new();   // default MolangExpr evals to 0 (absent axis)
}

class BoneAnimator
{
    public string BoneUuid = "";
    public List<KeyFrame> Rotation = new();
    public List<KeyFrame> Position = new();
    public List<KeyFrame> Scale = new();
}

class Animation
{
    public string Name = "";
    public LoopMode Loop = LoopMode.Loop;
    public float Length;
    public List<BoneAnimator> Animators = new();   // (C++ called this 'animations', renamed for clarity)

    // uuid -> animator lookup, built once on first use and cached so the sampler doesn't rebuild
    // a Dictionary every frame.
    Dictionary<string, BoneAnimator>? byUuid;
    public Dictionary<string, BoneAnimator> AnimatorsByUuid
    {
        get
        {
            if (byUuid == null)
            {
                byUuid = new Dictionary<string, BoneAnimator>(Animators.Count);
                foreach (var a in Animators) byUuid[a.BoneUuid] = a;
            }
            return byUuid;
        }
    }
}
