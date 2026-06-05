namespace IdleL.Molang;

// Compiled molang expression: compile a string once to an AST, eval cheaply per frame.
// Literals hit the constant fast-path (root null, value in constant_) so they cost ~nothing.
class MolangExpr
{
    private Node? root;
    private float constant;

    public static MolangExpr Compile(string src)
    {
        List<Token> tokens = Tokenizer.Tokenize(src);
        var parser = new Parser(tokens);
        Node node = parser.ParseProgram();
        if (!parser.AtEnd) throw new Exception("molang: unexpected trailing tokens");

        var expr = new MolangExpr();
        if (node.kind == Node.Kind.Number)
            expr.constant = (float)node.number;     // constant fast-path
        else
            expr.root = node;
        return expr;
    }

    public float Eval(MolangContext ctx)
    {
        if (root != null) return EvalNode(root, ctx);
        return constant;
    }

    static float EvalNode(Node n, MolangContext ctx)
    {
        switch (n.kind)
        {
            case Node.Kind.Number:
                return (float)n.number;

            case Node.Kind.Lookup:
                return EvalLookup(n.name, ctx);

            case Node.Kind.Unary:
            {
                float v = EvalNode(n.left!, ctx);
                if (n.op == Tok.Minus) return -v;
                if (n.op == Tok.Not)   return v != 0.0f ? 0.0f : 1.0f;
                throw new Exception("molang: unknown unary op");
            }

            case Node.Kind.Binary:
            {
                float l = EvalNode(n.left!, ctx);
                float r = EvalNode(n.right!, ctx);
                switch (n.op)
                {
                    case Tok.Plus:      return l + r;
                    case Tok.Minus:     return l - r;
                    case Tok.Star:      return l * r;
                    case Tok.Slash:     return r != 0.0f ? l / r : 0.0f;   // molang: /0 = 0
                    case Tok.Less:      return l <  r ? 1.0f : 0.0f;
                    case Tok.LessEq:    return l <= r ? 1.0f : 0.0f;
                    case Tok.Greater:   return l >  r ? 1.0f : 0.0f;
                    case Tok.GreaterEq: return l >= r ? 1.0f : 0.0f;
                    case Tok.EqEq:      return l == r ? 1.0f : 0.0f;
                    case Tok.NotEq:     return l != r ? 1.0f : 0.0f;
                    case Tok.And:       return (l != 0.0f && r != 0.0f) ? 1.0f : 0.0f;
                    case Tok.Or:        return (l != 0.0f || r != 0.0f) ? 1.0f : 0.0f;
                    case Tok.Coalesce:  return float.IsFinite(l) ? l : r;
                    default: throw new Exception("molang: unknown binary op");
                }
            }

            case Node.Kind.Ternary:
                return EvalNode(n.cond!, ctx) != 0.0f ? EvalNode(n.left!, ctx) : EvalNode(n.right!, ctx);

            case Node.Kind.Call:
            {
                var a = new List<float>(n.args.Count);
                foreach (var arg in n.args) a.Add(EvalNode(arg, ctx));
                return EvalCall(n.name, a);
            }
        }
        throw new Exception("molang: unknown node kind");
    }

    static float EvalLookup(string name, MolangContext ctx)
    {
        bool IsPrefix(string p) => name.StartsWith(p, StringComparison.Ordinal);

        if (name == "query.anim_time"  || name == "q.anim_time")  return ctx.AnimTime;
        if (name == "query.life_time"  || name == "q.life_time")  return ctx.LifeTime;
        if (name == "query.delta_time" || name == "q.delta_time") return ctx.DeltaTime;
        if (name == "math.pi") return 3.14159265358979323846f;

        foreach (var p in new[] { "variable.", "v.", "temp.", "t." })
            if (IsPrefix(p))
            {
                string key = name.Substring(p.Length);
                return ctx.Variables.TryGetValue(key, out float val) ? val : 0.0f;
            }
        return 0.0f;   // unknown lookup -> 0 (molang is lenient)
    }

    static float EvalCall(string name, List<float> a)
    {
        const float PI = 3.14159265358979323846f;
        float D2R(float d) => d * PI / 180.0f;   // molang trig is in DEGREES
        float R2D(float r) => r * 180.0f / PI;
        float Arg(int i) => i < a.Count ? a[i] : 0.0f;

        switch (name)
        {
            case "math.sin":   return MathF.Sin(D2R(Arg(0)));
            case "math.cos":   return MathF.Cos(D2R(Arg(0)));
            case "math.tan":   return MathF.Tan(D2R(Arg(0)));
            case "math.asin":  return R2D(MathF.Asin(Arg(0)));
            case "math.acos":  return R2D(MathF.Acos(Arg(0)));
            case "math.atan":  return R2D(MathF.Atan(Arg(0)));
            case "math.atan2": return R2D(MathF.Atan2(Arg(0), Arg(1)));
            case "math.abs":   return MathF.Abs(Arg(0));
            case "math.ceil":  return MathF.Ceiling(Arg(0));
            case "math.floor": return MathF.Floor(Arg(0));
            case "math.round": return MathF.Round(Arg(0));
            case "math.trunc": return MathF.Truncate(Arg(0));
            case "math.exp":   return MathF.Exp(Arg(0));
            case "math.ln":    return MathF.Log(Arg(0));
            case "math.sqrt":  return MathF.Sqrt(Arg(0));
            case "math.pow":   return MathF.Pow(Arg(0), Arg(1));
            case "math.mod":   return Arg(1) != 0.0f ? Arg(0) % Arg(1) : 0.0f;
            case "math.max":   return MathF.Max(Arg(0), Arg(1));
            case "math.min":   return MathF.Min(Arg(0), Arg(1));
            case "math.clamp": return MathF.Max(Arg(1), MathF.Min(Arg(0), Arg(2)));
            case "math.lerp":  return Arg(0) + (Arg(1) - Arg(0)) * Arg(2);
            case "math.hermite_blend": { float t = Arg(0); return t * t * (3.0f - 2.0f * t); }
            case "math.lerprotate":
            {
                float diff = (Arg(1) - Arg(0)) % 360.0f;
                if (diff >  180.0f) diff -= 360.0f;
                if (diff < -180.0f) diff += 360.0f;
                return Arg(0) + diff * Arg(2);
            }
        }
        throw new Exception($"molang: unknown function '{name}'");
    }
}
