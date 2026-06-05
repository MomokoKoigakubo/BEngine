using Silk.NET.Vulkan;

namespace IdleL.Rendering;

static class VkCheck
{
    public static void Check(Result r, string what)
    {
        if (r != Result.Success) throw new Exception($"{what} failed: VkResult={r}");
    }
}
