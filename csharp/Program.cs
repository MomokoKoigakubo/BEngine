// IdleL (C#) — porting C++ -> C#.
// Default: run the console logic tests (headless, fast). `dotnet run -- app` launches the
// Silk.NET window + Vulkan renderer (momoko renders + animates).
if (args.Length > 0 && args[0] == "app")
{
    new IdleL.App.Application().Run();
    return;
}

MolangTest.Run();
System.Console.WriteLine();
BBModelTest.Run();
System.Console.WriteLine();
EcsTest.Run();
System.Console.WriteLine();
AnimSamplerTest.Run();
System.Console.WriteLine();
CubeBuilderTest.Run();
