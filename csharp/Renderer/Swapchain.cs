using Silk.NET.Vulkan;
using Silk.NET.Windowing;

namespace IdleL.Rendering;

// Owns the swapchain, its images + views, the depth image, and the chosen format/extent.
// Rebuilds itself on resize via Recreate(). Uses (not owns) a GpuDevice.
unsafe class Swapchain : IDisposable
{
    readonly GpuDevice device;
    readonly Surface surface;
    readonly IWindow window;
    readonly Vk vk;

    public SwapchainKHR Handle;
    public Image[] Images = Array.Empty<Image>();
    public ImageView[] ImageViews = Array.Empty<ImageView>();
    public Extent2D Extent;
    public Format ImageFormat;
    public Format DepthFormat = Format.D32Sfloat;
    // one depth target per swapchain image so frames in flight don't share (and race) a single depth buffer
    public Image[] DepthImages = Array.Empty<Image>();
    public ImageView[] DepthViews = Array.Empty<ImageView>();
    Allocation[] depthMemories = Array.Empty<Allocation>();

    public SampleCountFlags Samples = SampleCountFlags.Count1Bit;
    public Image[] ColorImages = Array.Empty<Image>();        // multisampled render targets (per image), only when Samples != 1
    public ImageView[] ColorViews = Array.Empty<ImageView>();
    Allocation[] colorMemories = Array.Empty<Allocation>();

    public uint ImageCount => (uint)Images.Length;

    public Swapchain(GpuDevice device, Surface surface, IWindow window, SampleCountFlags samples)
    {
        this.device = device;
        this.surface = surface;
        this.window = window;
        Samples = samples;
        vk = device.Vk;
        CreateSwapchain();
        CreateImageViews();
        CreateColorResources();
        CreateDepthResources();
    }

    static SurfaceFormatKHR ChooseFormat(SurfaceFormatKHR[] formats)
    {
        foreach (var f in formats)
            if (f.Format == Format.B8G8R8A8Srgb && f.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return f;
        return formats[0];
    }

    static PresentModeKHR ChoosePresentMode(PresentModeKHR[] modes)
    {
        foreach (var m in modes) if (m == PresentModeKHR.MailboxKhr) return m;
        return PresentModeKHR.FifoKhr;
    }

    Extent2D ChooseExtent(SurfaceCapabilitiesKHR caps)
    {
        if (caps.CurrentExtent.Width != uint.MaxValue) return caps.CurrentExtent;
        var fb = window.FramebufferSize;
        return new Extent2D
        {
            Width = Math.Clamp((uint)fb.X, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
            Height = Math.Clamp((uint)fb.Y, caps.MinImageExtent.Height, caps.MaxImageExtent.Height)
        };
    }

    void CreateSwapchain()
    {
        var support = device.QuerySwapchainSupport(device.Physical);
        var format = ChooseFormat(support.Formats);
        var present = ChoosePresentMode(support.PresentModes);
        var extent = ChooseExtent(support.Capabilities);

        uint imageCount = support.Capabilities.MinImageCount + 1;
        if (support.Capabilities.MaxImageCount > 0 && imageCount > support.Capabilities.MaxImageCount)
            imageCount = support.Capabilities.MaxImageCount;

        var info = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface.Handle,
            MinImageCount = imageCount,
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            PreTransform = support.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = present,
            Clipped = true,
            OldSwapchain = default
        };

        uint* families = stackalloc uint[] { device.GraphicsFamily, device.PresentFamily };
        if (device.GraphicsFamily != device.PresentFamily)
        {
            info.ImageSharingMode = SharingMode.Concurrent;
            info.QueueFamilyIndexCount = 2;
            info.PQueueFamilyIndices = families;
        }
        else info.ImageSharingMode = SharingMode.Exclusive;

        VkCheck.Check(device.KhrSwapchain.CreateSwapchain(device.Device, in info, null, out Handle), "vkCreateSwapchain");

        uint count = 0;
        device.KhrSwapchain.GetSwapchainImages(device.Device, Handle, ref count, null);
        Images = new Image[count];
        fixed (Image* p = Images) device.KhrSwapchain.GetSwapchainImages(device.Device, Handle, ref count, p);

        ImageFormat = format.Format;
        Extent = extent;
    }

    void CreateImageViews()
    {
        ImageViews = new ImageView[Images.Length];
        for (int i = 0; i < Images.Length; i++)
        {
            var info = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = Images[i],
                ViewType = ImageViewType.Type2D,
                Format = ImageFormat,
                Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity,
                                                  ComponentSwizzle.Identity, ComponentSwizzle.Identity),
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            VkCheck.Check(vk.CreateImageView(device.Device, in info, null, out ImageViews[i]), "swapchain view");
        }
    }

    // Multisampled color render target (resolved to the swapchain image at end-of-pass). Skipped
    // entirely when MSAA is off, then we render straight into the swapchain image.
    void CreateColorResources()
    {
        if (Samples == SampleCountFlags.Count1Bit) return;
        int n = Images.Length;
        ColorImages = new Image[n];
        ColorViews = new ImageView[n];
        colorMemories = new Allocation[n];
        for (int i = 0; i < n; i++)
        {
            device.CreateImage(Extent.Width, Extent.Height, 1, ImageFormat,
                ImageUsageFlags.ColorAttachmentBit, out ColorImages[i], out colorMemories[i], Samples);
            var view = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = ColorImages[i],
                ViewType = ImageViewType.Type2D,
                Format = ImageFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
            };
            VkCheck.Check(vk.CreateImageView(device.Device, in view, null, out ColorViews[i]), "msaa color view");
        }
    }

    void CreateDepthResources()
    {
        int n = Images.Length;
        DepthImages = new Image[n];
        DepthViews = new ImageView[n];
        depthMemories = new Allocation[n];
        for (int i = 0; i < n; i++)
        {
            device.CreateImage(Extent.Width, Extent.Height, 1, DepthFormat,
                ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit, out DepthImages[i], out depthMemories[i], Samples);
            var view = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = DepthImages[i],
                ViewType = ImageViewType.Type2D,
                Format = DepthFormat,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
            };
            VkCheck.Check(vk.CreateImageView(device.Device, in view, null, out DepthViews[i]), "depth view");
        }
    }

    public void Recreate()
    {
        var fb = window.FramebufferSize;
        while (fb.X == 0 || fb.Y == 0) { fb = window.FramebufferSize; window.DoEvents(); }

        vk.DeviceWaitIdle(device.Device);
        Cleanup();
        CreateSwapchain();
        CreateImageViews();
        CreateColorResources();
        CreateDepthResources();
    }

    void Cleanup()
    {
        for (int i = 0; i < DepthImages.Length; i++)
        {
            vk.DestroyImageView(device.Device, DepthViews[i], null);
            vk.DestroyImage(device.Device, DepthImages[i], null);
            device.Allocator.Free(depthMemories[i]);
        }
        if (Samples != SampleCountFlags.Count1Bit)
            for (int i = 0; i < ColorImages.Length; i++)
            {
                vk.DestroyImageView(device.Device, ColorViews[i], null);
                vk.DestroyImage(device.Device, ColorImages[i], null);
                device.Allocator.Free(colorMemories[i]);
            }
        foreach (var v in ImageViews) vk.DestroyImageView(device.Device, v, null);
        device.KhrSwapchain.DestroySwapchain(device.Device, Handle, null);
    }

    public void Dispose() => Cleanup();
}
