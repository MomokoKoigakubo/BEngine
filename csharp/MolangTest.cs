using IdleL.Molang;

// Validates the molang port — the same 24 cases we ran on the C++ version.
static class MolangTest
{
    public static void Run()
    {
        var ctx = new MolangContext { AnimTime = 2.5f };
        ctx.Variables["speed"] = 3.0f;

        var cases = new (string src, float expect)[]
        {
            ("35",                    35),
            ("2 + 3 * 4",             14),
            ("-3 + 1",                -2),
            ("!0",                     1),
            ("2 < 3",                  1),
            ("5 >= 9",                 0),
            ("3 == 3",                 1),
            ("1 && 0",                 0),
            ("1 || 0",                 1),
            ("1 ? 5 : 9",              5),
            ("0 ? 5 : 9",              9),
            ("2 < 3 ? 10 : 20",       10),
            ("math.sin(90)",           1),
            ("math.cos(0)",            1),
            ("math.max(2, 7)",         7),
            ("math.min(2, 7)",         2),
            ("math.clamp(15, 0, 10)", 10),
            ("math.sqrt(16)",          4),
            ("math.lerp(0, 10, 0.5)",  5),
            ("math.abs(-8)",           8),
            ("query.anim_time",      2.5f),
            ("variable.speed * 2",     6),
            ("1; 2; 3",                3),
            ("(2 + 3) * 4",           20),
        };

        int fails = 0;
        foreach (var (src, expect) in cases)
        {
            float got = MolangExpr.Compile(src).Eval(ctx);
            bool ok = MathF.Abs(got - expect) < 1e-4f;
            Console.WriteLine($"{src,-24} = {got,8:0.###}  (expect {expect})  {(ok ? "OK" : "FAIL")}");
            if (!ok) fails++;
        }
        Console.WriteLine(fails == 0 ? "\nmolang: ALL PASS" : $"\nmolang: {fails} FAILED");
    }
}
