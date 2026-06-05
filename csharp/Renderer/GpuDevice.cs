using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace IdleL.Rendering;

// What a physical device + surface support for a swapchain.
struct SwapChainSupport
{
    public SurfaceCapabilitiesKHR Capabilities;
    public SurfaceFormatKHR[] Formats;
    public PresentModeKHR[] PresentModes;
}

// Owns the physical + logical device, graphics/present queues, queue family indices, and the
// KHR_swapchain extension. Hand-rolls memory allocation (no VMA).
unsafe class GpuDevice : IDisposable
{
    public Vk Vk { get; }
    readonly GpuInstance instance;
    readonly Surface surface;

    public PhysicalDevice Physical;
    public Device Device;
    public Queue GraphicsQueue;
    public Queue PresentQueue;
    public uint GraphicsFamily = uint.MaxValue;
    public uint PresentFamily = uint.MaxValue;
    public uint MaxBindlessTextures;
    public KhrSwapchain KhrSwapchain = null!;

    static readonly string[] DeviceExtensions = { KhrSwapchain.ExtensionName };

    public GpuDevice(GpuInstance instance, Surface surface)
    {
        this.instance = instance;
        this.surface = surface;
        Vk = instance.Vk;
        PickPhysicalDevice();
        CreateLogicalDevice();
        if (!Vk.TryGetDeviceExtension(instance.Instance, Device, out KhrSwapchain))
            throw new Exception("VK_KHR_swapchain not available");
    }

    // ---- queue families ----
    (uint? graphics, uint? present) FindQueueFamilies(PhysicalDevice dev)
    {
        uint count = 0;
        Vk.GetPhysicalDeviceQueueFamilyProperties(dev, ref count, null);
        var props = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* p = props) Vk.GetPhysicalDeviceQueueFamilyProperties(dev, ref count, p);

        uint? graphics = null, present = null;
        for (uint i = 0; i < count; i++)
        {
            if ((props[i].QueueFlags & QueueFlags.GraphicsBit) != 0) graphics = i;
            instance.KhrSurface.GetPhysicalDeviceSurfaceSupport(dev, i, surface.Handle, out Bool32 presentSupport);
            if (presentSupport) present = i;
            if (graphics.HasValue && present.HasValue) break;
        }
        return (graphics, present);
    }

    public SwapChainSupport QuerySwapchainSupport(PhysicalDevice dev)
    {
        var s = new SwapChainSupport();
        instance.KhrSurface.GetPhysicalDeviceSurfaceCapabilities(dev, surface.Handle, out s.Capabilities);

        uint fmtCount = 0;
        instance.KhrSurface.GetPhysicalDeviceSurfaceFormats(dev, surface.Handle, ref fmtCount, null);
        s.Formats = new SurfaceFormatKHR[fmtCount];
        fixed (SurfaceFormatKHR* p = s.Formats)
            instance.KhrSurface.GetPhysicalDeviceSurfaceFormats(dev, surface.Handle, ref fmtCount, p);

        uint pmCount = 0;
        instance.KhrSurface.GetPhysicalDeviceSurfacePresentModes(dev, surface.Handle, ref pmCount, null);
        s.PresentModes = new PresentModeKHR[pmCount];
        fixed (PresentModeKHR* p = s.PresentModes)
            instance.KhrSurface.GetPhysicalDeviceSurfacePresentModes(dev, surface.Handle, ref pmCount, p);
        return s;
    }

    bool CheckDeviceExtensions(PhysicalDevice dev)
    {
        uint count = 0;
        Vk.EnumerateDeviceExtensionProperties(dev, (byte*)null, ref count, null);
        var avail = new ExtensionProperties[count];
        fixed (ExtensionProperties* p = avail)
            Vk.EnumerateDeviceExtensionProperties(dev, (byte*)null, ref count, p);

        var names = new HashSet<string>();
        for (int i = 0; i < avail.Length; i++)
            fixed (byte* n = avail[i].ExtensionName)
            {
                var s = Silk.NET.Core.Native.SilkMarshal.PtrToString((nint)n);
                if (s != null) names.Add(s);
            }
        foreach (var e in DeviceExtensions) if (!names.Contains(e)) return false;
        return true;
    }

    bool IsSuitable(PhysicalDevice dev)
    {
        Vk.GetPhysicalDeviceProperties(dev, out var props);
        bool typeOk = props.DeviceType is PhysicalDeviceType.DiscreteGpu or PhysicalDeviceType.IntegratedGpu;
        bool extOk = CheckDeviceExtensions(dev);
        bool swapOk = false;
        if (extOk) { var sup = QuerySwapchainSupport(dev); swapOk = sup.Formats.Length > 0 && sup.PresentModes.Length > 0; }
        var (g, p) = FindQueueFamilies(dev);
        return typeOk && extOk && swapOk && g.HasValue && p.HasValue;
    }

    void PickPhysicalDevice()
    {
        uint count = 0;
        Vk.EnumeratePhysicalDevices(instance.Instance, ref count, null);
        if (count == 0) throw new Exception("no Vulkan-capable GPU found");
        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* p = devices) Vk.EnumeratePhysicalDevices(instance.Instance, ref count, p);

        foreach (var d in devices)
            if (IsSuitable(d)) { Physical = d; break; }
        if (Physical.Handle == 0) throw new Exception("no suitable GPU found");

        var (g, pr) = FindQueueFamilies(Physical);
        GraphicsFamily = g!.Value;
        PresentFamily = pr!.Value;
    }

    void CreateLogicalDevice()
    {
        var uniqueFamilies = new HashSet<uint> { GraphicsFamily, PresentFamily };
        var queueInfos = new DeviceQueueCreateInfo[uniqueFamilies.Count];
        float priority = 1.0f;
        int qi = 0;
        foreach (uint fam in uniqueFamilies)
            queueInfos[qi++] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = fam,
                QueueCount = 1,
                PQueuePriorities = &priority
            };

        // bindless texture limit
        var vk12props = new PhysicalDeviceVulkan12Properties { SType = StructureType.PhysicalDeviceVulkan12Properties };
        var props2 = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &vk12props };
        Vk.GetPhysicalDeviceProperties2(Physical, &props2);
        MaxBindlessTextures = vk12props.MaxDescriptorSetUpdateAfterBindSampledImages;

        var features12 = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            RuntimeDescriptorArray = true,
            ShaderSampledImageArrayNonUniformIndexing = true,
            DescriptorBindingPartiallyBound = true,
            DescriptorBindingSampledImageUpdateAfterBind = true
        };
        var features11 = new PhysicalDeviceVulkan11Features
        {
            SType = StructureType.PhysicalDeviceVulkan11Features,
            ShaderDrawParameters = true,
            PNext = &features12
        };
        var features13 = new PhysicalDeviceVulkan13Features
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
            DynamicRendering = true,
            Synchronization2 = true,
            PNext = &features11
        };

        var extPtrs = Silk.NET.Core.Native.SilkMarshal.StringArrayToPtr(DeviceExtensions);
        fixed (DeviceQueueCreateInfo* pQueues = queueInfos)
        {
            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                PNext = &features13,
                QueueCreateInfoCount = (uint)queueInfos.Length,
                PQueueCreateInfos = pQueues,
                EnabledExtensionCount = (uint)DeviceExtensions.Length,
                PpEnabledExtensionNames = (byte**)extPtrs
            };
            VkCheck.Check(Vk.CreateDevice(Physical, in createInfo, null, out Device), "vkCreateDevice");
        }
        Silk.NET.Core.Native.SilkMarshal.Free(extPtrs);

        Vk.GetDeviceQueue(Device, GraphicsFamily, 0, out GraphicsQueue);
        Vk.GetDeviceQueue(Device, PresentFamily, 0, out PresentQueue);
    }

    // ---- hand-rolled memory ----
    public uint FindMemoryType(uint typeFilter, MemoryPropertyFlags props)
    {
        Vk.GetPhysicalDeviceMemoryProperties(Physical, out var memProps);
        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & props) == props)
                return i;
        throw new Exception("no suitable memory type");
    }

    public void CreateImage(uint w, uint h, uint mipLevels, Format format, ImageUsageFlags usage,
        out Image image, out DeviceMemory memory)
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(w, h, 1),
            MipLevels = mipLevels,
            ArrayLayers = 1,
            Format = format,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive
        };
        VkCheck.Check(Vk.CreateImage(Device, in info, null, out image), "vkCreateImage");

        Vk.GetImageMemoryRequirements(Device, image, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = FindMemoryType(req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        VkCheck.Check(Vk.AllocateMemory(Device, in alloc, null, out memory), "vkAllocateMemory(image)");
        Vk.BindImageMemory(Device, image, memory, 0);
    }

    public void UploadToBuffer<T>(GpuBuffer dst, ReadOnlySpan<T> data) where T : unmanaged
    {
        ulong size = (ulong)(data.Length * sizeof(T));
        using var staging = new GpuBuffer(this, size, BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Upload(data);
        ImmediateSubmit(cmd =>
        {
            var copy = new BufferCopy { Size = size };
            Vk.CmdCopyBuffer(cmd, staging.Handle, dst.Handle, 1, in copy);
        });
    }

    public void ImmediateSubmit(Action<CommandBuffer> record)
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = GraphicsFamily
        };
        VkCheck.Check(Vk.CreateCommandPool(Device, in poolInfo, null, out var pool), "immediate pool");

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = pool,
            CommandBufferCount = 1
        };
        Vk.AllocateCommandBuffers(Device, in allocInfo, out CommandBuffer cmd);

        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        Vk.BeginCommandBuffer(cmd, in begin);
        record(cmd);
        Vk.EndCommandBuffer(cmd);

        var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
        Vk.QueueSubmit(GraphicsQueue, 1, in submit, default);
        Vk.QueueWaitIdle(GraphicsQueue);
        Vk.DestroyCommandPool(Device, pool, null);
    }

    public void Dispose() => Vk.DestroyDevice(Device, null);
}
