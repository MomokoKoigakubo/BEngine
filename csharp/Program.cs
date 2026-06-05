// IdleL entry point. boots the window + vulkan renderer (momoko).
if (args.Length > 0 && args[0] == "app")
{
    new IdleL.App.Application().Run();
    return;
}
