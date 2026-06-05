using Silk.NET.Vulkan;
using Silk.NET.Windowing;

namespace IdleL.Rendering;

// Owns the VkSurfaceKHR (bridge between Vulkan and the window). Created from the instance via the
// window's IVkSurface (replaces SDL_Vulkan_CreateSurface), destroyed before the instance.
unsafe class Surface : IDisposable
{
    readonly GpuInstance instance;
    public SurfaceKHR Handle;

    public Surface(GpuInstance instance, IWindow window)
    {
        this.instance = instance;
        Handle = window.VkSurface!.Create<AllocationCallbacks>(instance.Instance.ToHandle(), null).ToSurface();
    }

    public void Dispose() => instance.KhrSurface.DestroySurface(instance.Instance, Handle, null);
}
