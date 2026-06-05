using Silk.NET.Vulkan;
using System.Runtime.InteropServices;

namespace IdleL.Rendering;

// Hierarchical-Z depth pyramid for GPU occlusion culling. One pyramid per swapchain image, so the
// cull at frame N reads the pyramid this image got the LAST time it was used (fence/acquire-protected,
// race-free) = previous-frame depth. mip 0 = the depth (sample 0 of the MSAA depth); each higher mip =
// max of the 2x2 below (the farthest surface in that region) so a coarse texel is conservative.
// R32_SFLOAT (depth formats can't be storage images), kept in GENERAL (valid for storage + sampling).
unsafe class HiZ : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct HiZPush { public uint DstMip; public uint DstW; public uint DstH; public uint Pad; }

    const uint MaxMips = 16;   // descriptor array is fixed-size + partially bound; covers up to 65536px

    readonly GpuDevice device;
    readonly Vk vk;

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint MipLevels { get; private set; }

    Image[] images = Array.Empty<Image>();
    Allocation[] memories = Array.Empty<Allocation>();
    ImageView[] sampleViews = Array.Empty<ImageView>();    // [image]: whole chain, for the cull to textureLod
    ImageView[][] mipViews = Array.Empty<ImageView[]>();    // [image][mip]: single-mip storage views, for the build
    public Sampler Sampler;

    DescriptorSetLayout setLayout;
    PipelineLayout pipelineLayout;
    Pipeline copyPipeline, reducePipeline;
    DescriptorPool pool;
    DescriptorSet[] sets = Array.Empty<DescriptorSet>();

    public HiZ(GpuDevice device, Extent2D extent, ImageView[] depthViews)
    {
        this.device = device;
        vk = device.Vk;
        CreateSampler();
        CreatePipelines();
        Recreate(extent, depthViews);
    }

    public ImageView SampleView(uint imageIndex) => sampleViews[imageIndex];

    void CreateSampler()
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest, MinFilter = Filter.Nearest, MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge, AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge, MinLod = 0f, MaxLod = 32f
        };
        VkCheck.Check(vk.CreateSampler(device.Device, in info, null, out Sampler), "hiz sampler");
    }

    void CreatePipelines()
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[2];
        bindings[0] = new DescriptorSetLayoutBinding { Binding = 0, DescriptorType = DescriptorType.SampledImage, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit };
        bindings[1] = new DescriptorSetLayoutBinding { Binding = 1, DescriptorType = DescriptorType.StorageImage, DescriptorCount = MaxMips, StageFlags = ShaderStageFlags.ComputeBit };
        var bflags = stackalloc DescriptorBindingFlags[2] { 0, DescriptorBindingFlags.PartiallyBoundBit };
        var flagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo { SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo, BindingCount = 2, PBindingFlags = bflags };
        var layoutInfo = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 2, PBindings = bindings, PNext = &flagsInfo };
        VkCheck.Check(vk.CreateDescriptorSetLayout(device.Device, in layoutInfo, null, out setLayout), "hiz set layout");

        var range = new PushConstantRange { StageFlags = ShaderStageFlags.ComputeBit, Offset = 0, Size = (uint)sizeof(HiZPush) };
        fixed (DescriptorSetLayout* pl = &setLayout)
        {
            var plInfo = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = pl, PushConstantRangeCount = 1, PPushConstantRanges = &range };
            VkCheck.Check(vk.CreatePipelineLayout(device.Device, in plInfo, null, out pipelineLayout), "hiz pipeline layout");
        }

        byte[] code = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "shaders", "hiz.spv"));
        ShaderModule module;
        fixed (byte* p = code)
        {
            var smInfo = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = (uint*)p };
            VkCheck.Check(vk.CreateShaderModule(device.Device, in smInfo, null, out module), "hiz module");
        }
        copyPipeline = CreatePipe(module, "copyMain");
        reducePipeline = CreatePipe(module, "reduceMain");
        vk.DestroyShaderModule(device.Device, module, null);

        // pool sized for a few swapchain images
        var sizes = stackalloc DescriptorPoolSize[2];
        sizes[0] = new DescriptorPoolSize { Type = DescriptorType.SampledImage, DescriptorCount = 8 };
        sizes[1] = new DescriptorPoolSize { Type = DescriptorType.StorageImage, DescriptorCount = 8 * MaxMips };
        var poolInfo = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, PoolSizeCount = 2, PPoolSizes = sizes, MaxSets = 8 };
        VkCheck.Check(vk.CreateDescriptorPool(device.Device, in poolInfo, null, out pool), "hiz pool");
    }

    Pipeline CreatePipe(ShaderModule module, string entry)
    {
        byte* name = (byte*)Silk.NET.Core.Native.SilkMarshal.StringToPtr(entry);
        var info = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit, Module = module, PName = name },
            Layout = pipelineLayout
        };
        VkCheck.Check(vk.CreateComputePipelines(device.Device, default, 1, in info, null, out Pipeline pipe), "hiz pipe " + entry);
        Silk.NET.Core.Native.SilkMarshal.Free((nint)name);
        return pipe;
    }

    public void Recreate(Extent2D extent, ImageView[] depthViews)
    {
        DestroyImages();
        uint imageCount = (uint)depthViews.Length;
        Width = Math.Max(1, extent.Width);
        Height = Math.Max(1, extent.Height);
        MipLevels = (uint)Math.Floor(Math.Log2(Math.Max(Width, Height))) + 1;

        images = new Image[imageCount];
        memories = new Allocation[imageCount];
        sampleViews = new ImageView[imageCount];
        mipViews = new ImageView[imageCount][];

        for (int i = 0; i < imageCount; i++)
        {
            device.CreateImage(Width, Height, MipLevels, Format.R32Sfloat,
                ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit, out images[i], out memories[i]);
            sampleViews[i] = CreateView(images[i], 0, MipLevels);
            mipViews[i] = new ImageView[MipLevels];
            for (uint m = 0; m < MipLevels; m++) mipViews[i][m] = CreateView(images[i], m, 1);
            device.ImmediateSubmit(cmd => ToGeneral(cmd, images[i]));
        }

        vk.ResetDescriptorPool(device.Device, pool, 0);
        sets = new DescriptorSet[imageCount];
        for (int i = 0; i < imageCount; i++)
        {
            fixed (DescriptorSetLayout* pl = &setLayout)
            {
                var ai = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = pl };
                VkCheck.Check(vk.AllocateDescriptorSets(device.Device, in ai, out sets[i]), "hiz set");
            }
            // binding 0 -> the depth image (sampled, read in ShaderReadOnly layout)
            var depthInfo = new DescriptorImageInfo { ImageView = depthViews[i], ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
            var w0 = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = sets[i], DstBinding = 0, DstArrayElement = 0, DescriptorType = DescriptorType.SampledImage, DescriptorCount = 1, PImageInfo = &depthInfo };
            vk.UpdateDescriptorSets(device.Device, 1, in w0, 0, null);
            // binding 1 -> the MipLevels storage views
            var mipInfos = stackalloc DescriptorImageInfo[(int)MipLevels];
            for (uint m = 0; m < MipLevels; m++) mipInfos[m] = new DescriptorImageInfo { ImageView = mipViews[i][m], ImageLayout = ImageLayout.General };
            var w1 = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = sets[i], DstBinding = 1, DstArrayElement = 0, DescriptorType = DescriptorType.StorageImage, DescriptorCount = MipLevels, PImageInfo = mipInfos };
            vk.UpdateDescriptorSets(device.Device, 1, in w1, 0, null);
        }
    }

    // record the pyramid build for this image (depth must already be in ShaderReadOnly layout)
    public void Build(CommandBuffer cmd, uint imageIndex)
    {
        var set = sets[imageIndex];
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, pipelineLayout, 0, 1, in set, 0, null);

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, copyPipeline);
        var pc0 = new HiZPush { DstMip = 0, DstW = Width, DstH = Height };
        vk.CmdPushConstants(cmd, pipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(HiZPush), &pc0);
        vk.CmdDispatch(cmd, (Width + 7) / 8, (Height + 7) / 8, 1);
        StorageBarrier(cmd, images[imageIndex]);

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, reducePipeline);
        for (uint mip = 1; mip < MipLevels; mip++)
        {
            uint w = Math.Max(1u, Width >> (int)mip);
            uint h = Math.Max(1u, Height >> (int)mip);
            var pc = new HiZPush { DstMip = mip, DstW = w, DstH = h };
            vk.CmdPushConstants(cmd, pipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(HiZPush), &pc);
            vk.CmdDispatch(cmd, (w + 7) / 8, (h + 7) / 8, 1);
            StorageBarrier(cmd, images[imageIndex]);
        }
    }

    void StorageBarrier(CommandBuffer cmd, Image img)
    {
        var b = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.ComputeShaderBit, SrcAccessMask = AccessFlags2.ShaderStorageWriteBit,
            DstStageMask = PipelineStageFlags2.ComputeShaderBit, DstAccessMask = AccessFlags2.ShaderStorageReadBit,
            OldLayout = ImageLayout.General, NewLayout = ImageLayout.General,
            Image = img, SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, MipLevels, 0, 1)
        };
        var dep = new DependencyInfo { SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 1, PImageMemoryBarriers = &b };
        vk.CmdPipelineBarrier2(cmd, in dep);
    }

    ImageView CreateView(Image img, uint baseMip, uint mipCount)
    {
        var info = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo, Image = img, ViewType = ImageViewType.Type2D, Format = Format.R32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, baseMip, mipCount, 0, 1)
        };
        VkCheck.Check(vk.CreateImageView(device.Device, in info, null, out ImageView view), "hiz view");
        return view;
    }

    void ToGeneral(CommandBuffer cmd, Image img)
    {
        var b = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.TopOfPipeBit, SrcAccessMask = AccessFlags2.None,
            DstStageMask = PipelineStageFlags2.ComputeShaderBit, DstAccessMask = AccessFlags2.ShaderStorageWriteBit,
            OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.General,
            Image = img, SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, MipLevels, 0, 1)
        };
        var dep = new DependencyInfo { SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 1, PImageMemoryBarriers = &b };
        vk.CmdPipelineBarrier2(cmd, in dep);
    }

    void DestroyImages()
    {
        for (int i = 0; i < images.Length; i++)
        {
            if (sampleViews.Length > i) vk.DestroyImageView(device.Device, sampleViews[i], null);
            if (mipViews.Length > i && mipViews[i] != null)
                foreach (var v in mipViews[i]) vk.DestroyImageView(device.Device, v, null);
            vk.DestroyImage(device.Device, images[i], null);
            device.Allocator.Free(memories[i]);
        }
    }

    public void Dispose()
    {
        DestroyImages();
        vk.DestroyDescriptorPool(device.Device, pool, null);
        vk.DestroyPipeline(device.Device, copyPipeline, null);
        vk.DestroyPipeline(device.Device, reducePipeline, null);
        vk.DestroyPipelineLayout(device.Device, pipelineLayout, null);
        vk.DestroyDescriptorSetLayout(device.Device, setLayout, null);
        vk.DestroySampler(device.Device, Sampler, null);
    }
}
