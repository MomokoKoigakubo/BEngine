using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using IdleL.ECS;
using IdleL.Resources;
using IdleL.Scenes;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace IdleL.Rendering;

[StructLayout(LayoutKind.Sequential)]
struct PushConstants
{
    public Matrix4x4 ViewProj;
    public float Time;          // seconds; shader derives the flipbook from this
    public uint BoneBase;       // base index into the bone buffer = this frame-in-flight's region
    public uint InstanceBase;   // base index into the instance buffer (SV_InstanceID is 0-based, doesn't carry firstInstance)
}

[StructLayout(LayoutKind.Sequential)]
struct CullData   // compute push constant; mirrors CullData in cull.slang (112 bytes)
{
    public Matrix4x4 ViewProj;   // frustum planes (extracted GPU-side) + occlusion projection
    public Vector4 ModelBound;   // shared model-space sphere: xyz center, w radius
    public uint InstanceCount;
    public uint OutputBase;      // visibleInstances base slot for this frame-in-flight
    public uint CommandIndex;
    public uint ImageIndex;      // which swapchain image's Hi-Z to sample
    public uint ScreenW;
    public uint ScreenH;
    public uint MipCount;
    public uint OcclusionOn;     // 0 until this image has a valid pyramid (avoids garbage-depth culls)
}

[StructLayout(LayoutKind.Sequential)]
struct StaticPush   // static-mesh pipeline push constant; mirrors static.slang (128 bytes)
{
    public Matrix4x4 Model;     // per static mesh (identity for world-space-baked chunks)
    public Matrix4x4 ViewProj;
}

[StructLayout(LayoutKind.Sequential)]
struct FlipbookParam
{
    public float FrameCount;
    public float FlipRate;
}

unsafe class Renderer : IDisposable
{
    const uint MaxFramesInFlight = 2;
    const uint MaxBones = 4096;   // bones = GROUPS; 64B/bone allocated once, perframe upload sized to actual count
    const uint MaxInstances = 16384; // bump this when we have trees and more instances
    readonly IWindow window;
    readonly Vk vk;
    bool framebufferResized;

    readonly GpuInstance instance;
    readonly Surface surface;
    public GpuDevice Device { get; }
    readonly Swapchain swapchain;
    HiZ hiz = null!;
    SampleCountFlags msaaSamples;

    PipelineLayout pipelineLayout;
    DescriptorSetLayout descriptorSetLayout;
    DescriptorPool descriptorPool;
    DescriptorSet descriptorSet;
    Pipeline graphicsPipeline;
    PipelineLayout computePipelineLayout;
    Pipeline cullPipeline;
    PipelineLayout staticPipelineLayout;
    Pipeline staticPipeline;

    uint currentFrame;
    uint maxTextures;
    uint nextTextureSlot;
    CommandPool commandPool;
    CommandBuffer[] commandBuffers = Array.Empty<CommandBuffer>();
    Semaphore[] imageAvailableSemaphores = Array.Empty<Semaphore>();
    Semaphore[] renderFinishedSemaphores = Array.Empty<Semaphore>();
    Fence[] inFlightFences = Array.Empty<Fence>();

    GpuBuffer flipbookBuffer = null!;
    FlipbookParam[] flipbookParams = Array.Empty<FlipbookParam>();

    GpuBuffer boneBuffer = null!;
    GpuBuffer allInstanceBuffer = null!;      // every instance's model matrix, uploaded once; the cull compute reads this
    GpuBuffer visibleInstanceBuffer = null!;  // cull compute writes survivors here; the vertex shader reads them
    GpuBuffer drawCommandBuffer = null!;      // one DrawIndexedIndirectCommand per frame-in-flight; instanceCount filled by compute
    uint sceneInstanceCount;                  // number of instances currently in allInstanceBuffer
    bool[] pyramidBuilt = Array.Empty<bool>();   // per swapchain image: has its Hi-Z been built at least once
    MeshHandle instancedMesh;                 // the single mesh all instances share (the grid)
    Vector3 modelBoundCenter;                 // shared model-space bounding sphere (for GPU culling)
    float modelBoundRadius;
    Matrix4x4[] pendingBoneMatrices = Array.Empty<Matrix4x4>();

    public Renderer(IWindow window, int requestedMsaa = 4)
    {
        this.window = window;
        instance = new GpuInstance(window);
        surface = new Surface(instance, window);
        Device = new GpuDevice(instance, surface);
        vk = Device.Vk;
        msaaSamples = ClampSamples(requestedMsaa);
        swapchain = new Swapchain(Device, surface, window, msaaSamples);
        hiz = new HiZ(Device, swapchain.Extent, swapchain.DepthViews);

        CreateCommandPool();

        maxTextures = Math.Min(4096u, Device.MaxBindlessTextures);

        flipbookParams = new FlipbookParam[maxTextures];
        flipbookBuffer = new GpuBuffer(Device, (ulong)(sizeof(FlipbookParam) * maxTextures),
            BufferUsageFlags.StorageBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        flipbookBuffer.Upload<FlipbookParam>(flipbookParams);

        // bone SSBO
        boneBuffer = new GpuBuffer(Device, (ulong)sizeof(Matrix4x4) * MaxBones * MaxFramesInFlight,
            BufferUsageFlags.StorageBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        var identity = new Matrix4x4[MaxBones * MaxFramesInFlight];
        Array.Fill(identity, Matrix4x4.Identity);
        boneBuffer.Upload<Matrix4x4>(identity);

        // GPU-driven culling: all matrices (compute input), survivors (compute output -> vertex, per-frame
        // region), and the indirect command whose instanceCount the compute fills in. all device-local.
        allInstanceBuffer = new GpuBuffer(Device, (ulong)sizeof(Matrix4x4) * MaxInstances,
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit);
        visibleInstanceBuffer = new GpuBuffer(Device, (ulong)sizeof(Matrix4x4) * MaxInstances * MaxFramesInFlight,
            BufferUsageFlags.StorageBufferBit, MemoryPropertyFlags.DeviceLocalBit);
        drawCommandBuffer = new GpuBuffer(Device, (ulong)sizeof(DrawIndexedIndirectCommand) * MaxFramesInFlight,
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.IndirectBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.DeviceLocalBit);

        CreateDescriptorSetLayout();
        CreatePipelineLayout();
        CreateGraphicsPipeline();
        CreateComputePipeline();
        CreateStaticPipeline();
        CreateCommandBuffer();
        CreateSyncObjects();
        CreateDescriptorSet();
        pyramidBuilt = new bool[swapchain.ImageCount];
        UpdatePyramidDescriptors();
    }

    public void SetFrameBufferResized() => framebufferResized = true;

    public void WaitIdle() => vk.DeviceWaitIdle(Device.Device);

    public uint RegisterBindlessTexture(ImageView view, Sampler sampler)
    {
        uint slot = nextTextureSlot++;
        var imageInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = view,
            Sampler = sampler
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 0,
            DstArrayElement = slot,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfo
        };
        vk.UpdateDescriptorSets(Device.Device, 1, in write, 0, null);
        return slot;
    }

    public void SetFlipbook(uint slot, float frameCount, float flipRate)
    {
        flipbookParams[slot] = new FlipbookParam { FrameCount = frameCount, FlipRate = flipRate };
        flipbookBuffer.Upload<FlipbookParam>(flipbookParams);
    }

    public void SetBoneMatrices(Matrix4x4[] mats) => pendingBoneMatrices = mats;

    // Upload every instance's model matrix once (the scene is static), plus the shared mesh + its
    // model-space bounding sphere. The cull compute reads these every frame.
    public void SetInstances(MeshHandle mesh, ReadOnlySpan<Matrix4x4> models, Vector3 boundCenter, float boundRadius)
    {
        instancedMesh = mesh;
        modelBoundCenter = boundCenter;
        modelBoundRadius = boundRadius;
        sceneInstanceCount = (uint)models.Length;
        Device.UploadToBuffer<Matrix4x4>(allInstanceBuffer, models);
    }

    // Map the requested sample count to a flag and clamp to what the GPU supports (1 = MSAA off).
    SampleCountFlags ClampSamples(int requested)
    {
        SampleCountFlags want = requested >= 8 ? SampleCountFlags.Count8Bit
                              : requested >= 4 ? SampleCountFlags.Count4Bit
                              : requested >= 2 ? SampleCountFlags.Count2Bit
                              : SampleCountFlags.Count1Bit;
        var max = Device.GetMaxUsableSampleCount();
        return (SampleCountFlags)Math.Min((uint)want, (uint)max);   // single-bit powers of 2 -> min picks the lower count
    }

    static ImageMemoryBarrier2 ColorAttachmentBarrier(Image img) => new()
    {
        SType = StructureType.ImageMemoryBarrier2,
        // src at COLOR_ATTACHMENT_OUTPUT (not TOP_OF_PIPE) so the transition waits on the acquire
        // semaphore (also waited at that stage). otherwise the layout write races the acquire.
        SrcStageMask = PipelineStageFlags2.ColorAttachmentOutputBit, SrcAccessMask = AccessFlags2.None,
        DstStageMask = PipelineStageFlags2.ColorAttachmentOutputBit, DstAccessMask = AccessFlags2.ColorAttachmentWriteBit,
        OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.ColorAttachmentOptimal,
        Image = img,
        SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
    };

    void BufferBarrier(CommandBuffer cmd, Silk.NET.Vulkan.Buffer buffer,
        PipelineStageFlags2 srcStage, AccessFlags2 srcAccess, PipelineStageFlags2 dstStage, AccessFlags2 dstAccess)
    {
        var b = new BufferMemoryBarrier2
        {
            SType = StructureType.BufferMemoryBarrier2,
            SrcStageMask = srcStage, SrcAccessMask = srcAccess,
            DstStageMask = dstStage, DstAccessMask = dstAccess,
            Buffer = buffer, Offset = 0, Size = Vk.WholeSize
        };
        var dep = new DependencyInfo { SType = StructureType.DependencyInfo, BufferMemoryBarrierCount = 1, PBufferMemoryBarriers = &b };
        vk.CmdPipelineBarrier2(cmd, in dep);
    }

    void CreateCommandPool()
    {
        var info = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = Device.GraphicsFamily
        };
        VkCheck.Check(vk.CreateCommandPool(Device.Device, in info, null, out commandPool), "command pool");
    }

    void CreateDescriptorSetLayout()
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[7];
        bindings[0] = new DescriptorSetLayoutBinding   // bindless combined image samplers
        {
            Binding = 0, DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = maxTextures, StageFlags = ShaderStageFlags.FragmentBit
        };
        bindings[1] = new DescriptorSetLayoutBinding   // flipbook params (frag)
        {
            Binding = 1, DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1, StageFlags = ShaderStageFlags.FragmentBit
        };
        bindings[2] = new DescriptorSetLayoutBinding   // bone matrices (vertex)
        {
            Binding = 2, DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1, StageFlags = ShaderStageFlags.VertexBit
        };
        bindings[3] = new DescriptorSetLayoutBinding   // visible instances: compute writes, vertex reads
        {
            Binding = 3, DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1, StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.ComputeBit
        };
        bindings[4] = new DescriptorSetLayoutBinding   // all instance matrices (compute input)
        {
            Binding = 4, DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit
        };
        bindings[5] = new DescriptorSetLayoutBinding   // indirect draw commands (compute fills instanceCount)
        {
            Binding = 5, DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit
        };
        bindings[6] = new DescriptorSetLayoutBinding   // Hi-Z pyramids (one per swapchain image), occlusion cull
        {
            Binding = 6, DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 8, StageFlags = ShaderStageFlags.ComputeBit
        };

        var flags = stackalloc DescriptorBindingFlags[7];
        flags[0] = DescriptorBindingFlags.PartiallyBoundBit | DescriptorBindingFlags.UpdateAfterBindBit;
        flags[1] = 0; flags[2] = 0; flags[3] = 0; flags[4] = 0; flags[5] = 0;
        flags[6] = DescriptorBindingFlags.PartiallyBoundBit | DescriptorBindingFlags.UpdateAfterBindBit;

        var bindingFlags = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 7,
            PBindingFlags = flags
        };
        var info = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 7,
            PBindings = bindings,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            PNext = &bindingFlags
        };
        VkCheck.Check(vk.CreateDescriptorSetLayout(Device.Device, in info, null, out descriptorSetLayout), "set layout");
    }

    void CreatePipelineLayout()
    {
        var range = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = (uint)sizeof(PushConstants)
        };
        fixed (DescriptorSetLayout* pLayout = &descriptorSetLayout)
        {
            var info = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = pLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &range
            };
            VkCheck.Check(vk.CreatePipelineLayout(Device.Device, in info, null, out pipelineLayout), "pipeline layout");
        }
    }

    void CreateComputePipeline()
    {
        byte[] code = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "shaders", "cull.spv"));
        ShaderModule shader = CreateShaderModule(code);
        byte* name = (byte*)SilkMarshal.StringToPtr("main");   // slangc names a single entry point "main"

        var range = new PushConstantRange { StageFlags = ShaderStageFlags.ComputeBit, Offset = 0, Size = (uint)sizeof(CullData) };
        fixed (DescriptorSetLayout* pLayout = &descriptorSetLayout)
        {
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1, PSetLayouts = pLayout,
                PushConstantRangeCount = 1, PPushConstantRanges = &range
            };
            VkCheck.Check(vk.CreatePipelineLayout(Device.Device, in layoutInfo, null, out computePipelineLayout), "compute pipeline layout");
        }
        var info = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.ComputeBit, Module = shader, PName = name
            },
            Layout = computePipelineLayout
        };
        VkCheck.Check(vk.CreateComputePipelines(Device.Device, default, 1, in info, null, out cullPipeline), "compute pipeline");
        vk.DestroyShaderModule(Device.Device, shader, null);
        SilkMarshal.Free((nint)name);
    }

    ShaderModule CreateShaderModule(byte[] code)
    {
        fixed (byte* p = code)
        {
            var info = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)p
            };
            VkCheck.Check(vk.CreateShaderModule(Device.Device, in info, null, out var module), "shader module");
            return module;
        }
    }

    void CreateGraphicsPipeline()
    {
        byte[] code = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "shaders", "triangle.spv"));
        ShaderModule shader = CreateShaderModule(code);

        byte* vertName = (byte*)SilkMarshal.StringToPtr("vertMain");
        byte* fragName = (byte*)SilkMarshal.StringToPtr("fragMain");

        var stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit, Module = shader, PName = vertName
        };
        stages[1] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit, Module = shader, PName = fragName
        };

        var binding = new VertexInputBindingDescription
        {
            Binding = 0, Stride = (uint)sizeof(Vertex), InputRate = VertexInputRate.Vertex
        };
        var attrs = stackalloc VertexInputAttributeDescription[5];
        attrs[0] = new VertexInputAttributeDescription { Binding = 0, Location = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
        attrs[1] = new VertexInputAttributeDescription { Binding = 0, Location = 1, Format = Format.R32G32B32Sfloat, Offset = 12 };
        attrs[2] = new VertexInputAttributeDescription { Binding = 0, Location = 2, Format = Format.R32G32Sfloat,    Offset = 24 };
        attrs[3] = new VertexInputAttributeDescription { Binding = 0, Location = 3, Format = Format.R32Sfloat,       Offset = 32 };
        attrs[4] = new VertexInputAttributeDescription { Binding = 0, Location = 4, Format = Format.R32Sfloat,       Offset = 36 };

        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1, PVertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = 5, PVertexAttributeDescriptions = attrs
        };
        var inputAssembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList, PrimitiveRestartEnable = false
        };

        var dynStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamicState = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2, PDynamicStates = dynStates
        };
        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1
        };
        var rasterizer = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = false, RasterizerDiscardEnable = false,
            PolygonMode = PolygonMode.Fill, LineWidth = 1.0f,
            CullMode = CullModeFlags.BackBit, FrontFace = FrontFace.CounterClockwise,
            DepthBiasEnable = false
        };
        var multisampling = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false, RasterizationSamples = msaaSamples
        };
        var colorBlendAttachment = new PipelineColorBlendAttachmentState
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                             ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = false
        };
        var colorBlending = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            LogicOpEnable = false, AttachmentCount = 1, PAttachments = &colorBlendAttachment
        };
        var depthStencil = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true, DepthWriteEnable = true, DepthCompareOp = CompareOp.Less,
            DepthBoundsTestEnable = false, StencilTestEnable = false
        };

        Format colorFormat = swapchain.ImageFormat;
        var renderingInfo = new PipelineRenderingCreateInfo
        {
            SType = StructureType.PipelineRenderingCreateInfo,
            ColorAttachmentCount = 1,
            PColorAttachmentFormats = &colorFormat,
            DepthAttachmentFormat = swapchain.DepthFormat
        };

        var pipelineInfo = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            PNext = &renderingInfo,
            StageCount = 2, PStages = stages,
            PVertexInputState = &vertexInput,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterizer,
            PMultisampleState = &multisampling,
            PDepthStencilState = &depthStencil,
            PDynamicState = &dynamicState,
            PColorBlendState = &colorBlending,
            Layout = pipelineLayout,
            RenderPass = default,
            Subpass = 0
        };
        VkCheck.Check(vk.CreateGraphicsPipelines(Device.Device, default, 1, in pipelineInfo, null, out graphicsPipeline), "pipeline");

        vk.DestroyShaderModule(Device.Device, shader, null);
        SilkMarshal.Free((nint)vertName);
        SilkMarshal.Free((nint)fragName);
    }

    void CreateStaticPipeline()
    {
        byte[] code = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "shaders", "static.spv"));
        ShaderModule shader = CreateShaderModule(code);
        byte* vertName = (byte*)SilkMarshal.StringToPtr("vertMain");
        byte* fragName = (byte*)SilkMarshal.StringToPtr("fragMain");

        var range = new PushConstantRange { StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, Offset = 0, Size = (uint)sizeof(StaticPush) };
        fixed (DescriptorSetLayout* pLayout = &descriptorSetLayout)
        {
            var li = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = pLayout, PushConstantRangeCount = 1, PPushConstantRanges = &range };
            VkCheck.Check(vk.CreatePipelineLayout(Device.Device, in li, null, out staticPipelineLayout), "static pipeline layout");
        }

        var stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = shader, PName = vertName };
        stages[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = shader, PName = fragName };

        // same 40-byte Vertex stride; static.slang reads pos/normal/uv (loc 0-2); texIndex/boneIndex unused
        var binding = new VertexInputBindingDescription { Binding = 0, Stride = (uint)sizeof(Vertex), InputRate = VertexInputRate.Vertex };
        var attrs = stackalloc VertexInputAttributeDescription[3];
        attrs[0] = new VertexInputAttributeDescription { Binding = 0, Location = 0, Format = Format.R32G32B32Sfloat, Offset = 0 };
        attrs[1] = new VertexInputAttributeDescription { Binding = 0, Location = 1, Format = Format.R32G32B32Sfloat, Offset = 12 };
        attrs[2] = new VertexInputAttributeDescription { Binding = 0, Location = 2, Format = Format.R32G32Sfloat, Offset = 24 };
        var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo, VertexBindingDescriptionCount = 1, PVertexBindingDescriptions = &binding, VertexAttributeDescriptionCount = 3, PVertexAttributeDescriptions = attrs };
        var inputAssembly = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = PrimitiveTopology.TriangleList };

        var dynStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamicState = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynStates };
        var viewportState = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };
        var rasterizer = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, LineWidth = 1.0f, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise };
        var multisampling = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = msaaSamples };
        var blendAttach = new PipelineColorBlendAttachmentState { ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit, BlendEnable = false };
        var colorBlending = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttach };
        var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = true, DepthWriteEnable = true, DepthCompareOp = CompareOp.Less };

        Format colorFormat = swapchain.ImageFormat;
        var renderingInfo = new PipelineRenderingCreateInfo { SType = StructureType.PipelineRenderingCreateInfo, ColorAttachmentCount = 1, PColorAttachmentFormats = &colorFormat, DepthAttachmentFormat = swapchain.DepthFormat };
        var pipelineInfo = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo, PNext = &renderingInfo,
            StageCount = 2, PStages = stages, PVertexInputState = &vertexInput, PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState, PRasterizationState = &rasterizer, PMultisampleState = &multisampling,
            PDepthStencilState = &depthStencil, PDynamicState = &dynamicState, PColorBlendState = &colorBlending,
            Layout = staticPipelineLayout
        };
        VkCheck.Check(vk.CreateGraphicsPipelines(Device.Device, default, 1, in pipelineInfo, null, out staticPipeline), "static pipeline");
        vk.DestroyShaderModule(Device.Device, shader, null);
        SilkMarshal.Free((nint)vertName);
        SilkMarshal.Free((nint)fragName);
    }

    void CreateCommandBuffer()
    {
        commandBuffers = new CommandBuffer[MaxFramesInFlight];
        var info = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = MaxFramesInFlight
        };
        fixed (CommandBuffer* p = commandBuffers)
            VkCheck.Check(vk.AllocateCommandBuffers(Device.Device, in info, p), "command buffers");
    }

    void CreateSyncObjects()
    {
        var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };

        imageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
        inFlightFences = new Fence[MaxFramesInFlight];
        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            VkCheck.Check(vk.CreateSemaphore(Device.Device, in semInfo, null, out imageAvailableSemaphores[i]), "imgAvail sem");
            VkCheck.Check(vk.CreateFence(Device.Device, in fenceInfo, null, out inFlightFences[i]), "fence");
        }

        renderFinishedSemaphores = new Semaphore[swapchain.ImageCount];
        for (int i = 0; i < swapchain.ImageCount; i++)
            VkCheck.Check(vk.CreateSemaphore(Device.Device, in semInfo, null, out renderFinishedSemaphores[i]), "renderDone sem");
    }

    void CreateDescriptorSet()
    {
        var poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = maxTextures + 8 };   // bindless textures + Hi-Z pyramids
        poolSizes[1] = new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = 5 };   // flipbook, bone, visible, all-instances, draw-commands
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 2, PPoolSizes = poolSizes,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
            MaxSets = 1
        };
        VkCheck.Check(vk.CreateDescriptorPool(Device.Device, in poolInfo, null, out descriptorPool), "descriptor pool");

        fixed (DescriptorSetLayout* pLayout = &descriptorSetLayout)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = pLayout
            };
            VkCheck.Check(vk.AllocateDescriptorSets(Device.Device, in allocInfo, out descriptorSet), "descriptor set");
        }

        // binding 1 -> flipbook buffer
        var flipInfo = new DescriptorBufferInfo { Buffer = flipbookBuffer.Handle, Offset = 0, Range = Vk.WholeSize };
        var flipWrite = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet, DstSet = descriptorSet, DstBinding = 1, DstArrayElement = 0,
            DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, PBufferInfo = &flipInfo
        };
        vk.UpdateDescriptorSets(Device.Device, 1, in flipWrite, 0, null);

        // binding 2 -> bone buffer
        var boneInfo = new DescriptorBufferInfo { Buffer = boneBuffer.Handle, Offset = 0, Range = Vk.WholeSize };
        var boneWrite = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet, 
            DstSet = descriptorSet, 
            DstBinding = 2, 
            DstArrayElement = 0,
            DescriptorType = DescriptorType.StorageBuffer, 
            DescriptorCount = 1, 
            PBufferInfo = &boneInfo
        };
        vk.UpdateDescriptorSets(Device.Device, 1, in boneWrite, 0, null);
        // binding 3 -> visible instances (compute output, vertex input); 4 -> all matrices (compute input);
        // 5 -> indirect draw commands (compute fills instanceCount)
        var visInfo = new DescriptorBufferInfo { Buffer = visibleInstanceBuffer.Handle, Offset = 0, Range = Vk.WholeSize };
        WriteStorageBuffer(3, &visInfo);
        var allInfo = new DescriptorBufferInfo { Buffer = allInstanceBuffer.Handle, Offset = 0, Range = Vk.WholeSize };
        WriteStorageBuffer(4, &allInfo);
        var cmdInfo = new DescriptorBufferInfo { Buffer = drawCommandBuffer.Handle, Offset = 0, Range = Vk.WholeSize };
        WriteStorageBuffer(5, &cmdInfo);
    }

    void WriteStorageBuffer(uint binding, DescriptorBufferInfo* info)
    {
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet, DstSet = descriptorSet, DstBinding = binding,
            DstArrayElement = 0, DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, PBufferInfo = info
        };
        vk.UpdateDescriptorSets(Device.Device, 1, in write, 0, null);
    }

    // binding 6 -> the per-swapchain-image Hi-Z pyramids; the cull samples pyramids[imageIndex]
    void UpdatePyramidDescriptors()
    {
        uint n = swapchain.ImageCount;
        var infos = stackalloc DescriptorImageInfo[(int)n];
        for (uint i = 0; i < n; i++)
            infos[i] = new DescriptorImageInfo { ImageView = hiz.SampleView(i), Sampler = hiz.Sampler, ImageLayout = ImageLayout.General };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet, DstSet = descriptorSet, DstBinding = 6, DstArrayElement = 0,
            DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = n, PImageInfo = infos
        };
        vk.UpdateDescriptorSets(Device.Device, 1, in write, 0, null);
    }

    void RecordCommandBuffer(CommandBuffer cmd, uint imageIndex, Scene scene, ResourceManager resources)
    {
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        VkCheck.Check(vk.BeginCommandBuffer(cmd, in begin), "begin cmd");

        float aspect = swapchain.Extent.Width / (float)swapchain.Extent.Height;
        Matrix4x4 view = scene.Camera.ViewMatrix();
        Matrix4x4 proj = scene.Camera.ProjectionMatrix(aspect);

        // upload this frame's bone matrices into THIS frame-in-flight's region (fence already waited)
        uint boneBase = currentFrame * MaxBones;
        if (pendingBoneMatrices.Length > 0)
        {
            int nb = Math.Min(pendingBoneMatrices.Length, (int)MaxBones);
            boneBuffer.Upload<Matrix4x4>(pendingBoneMatrices.AsSpan(0, nb), (ulong)boneBase * (ulong)sizeof(Matrix4x4));
        }

        // ---- GPU-driven culling. compute frustum-tests every instance and writes the survivors +
        //      the indirect instanceCount. must run BEFORE the render pass (no compute inside dynamic rendering).
        Mesh mesh = resources.GetMesh(instancedMesh);
        uint instanceBase = currentFrame * MaxInstances;
        ulong cmdOffset = (ulong)currentFrame * (ulong)sizeof(DrawIndexedIndirectCommand);

        // reset this frame's indirect command: indexCount set, instanceCount zeroed (compute bumps it)
        var resetCmd = new DrawIndexedIndirectCommand { IndexCount = mesh.IndexCount, InstanceCount = 0, FirstIndex = 0, VertexOffset = 0, FirstInstance = 0 };
        vk.CmdUpdateBuffer(cmd, drawCommandBuffer.Handle, cmdOffset, (ulong)sizeof(DrawIndexedIndirectCommand), &resetCmd);
        BufferBarrier(cmd, drawCommandBuffer.Handle, PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit,
            PipelineStageFlags2.ComputeShaderBit, AccessFlags2.ShaderStorageReadBit | AccessFlags2.ShaderStorageWriteBit);

        var cull = new CullData
        {
            ViewProj = view * proj,
            ModelBound = new Vector4(modelBoundCenter, modelBoundRadius),
            InstanceCount = sceneInstanceCount, OutputBase = instanceBase, CommandIndex = currentFrame,
            ImageIndex = imageIndex, ScreenW = swapchain.Extent.Width, ScreenH = swapchain.Extent.Height,
            MipCount = hiz.MipLevels, OcclusionOn = pyramidBuilt[imageIndex] ? 1u : 0u
        };
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, cullPipeline);
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, computePipelineLayout, 0, 1, in descriptorSet, 0, null);
        vk.CmdPushConstants(cmd, computePipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(CullData), &cull);
        vk.CmdDispatch(cmd, (sceneInstanceCount + 63) / 64, 1, 1);

        // compute writes -> the vertex shader reads the survivors, the indirect draw reads the command
        BufferBarrier(cmd, visibleInstanceBuffer.Handle, PipelineStageFlags2.ComputeShaderBit, AccessFlags2.ShaderStorageWriteBit,
            PipelineStageFlags2.VertexShaderBit, AccessFlags2.ShaderStorageReadBit);
        BufferBarrier(cmd, drawCommandBuffer.Handle, PipelineStageFlags2.ComputeShaderBit, AccessFlags2.ShaderStorageWriteBit,
            PipelineStageFlags2.DrawIndirectBit, AccessFlags2.IndirectCommandReadBit);

        // UNDEFINED -> COLOR/DEPTH attachment optimal. With MSAA, the multisampled image is the render
        // target and the swapchain image is the resolve target, both need COLOR_ATTACHMENT_OPTIMAL.
        bool msaa = msaaSamples != SampleCountFlags.Count1Bit;
        var barriers = stackalloc ImageMemoryBarrier2[3];
        uint nBarriers = 0;
        if (msaa) barriers[nBarriers++] = ColorAttachmentBarrier(swapchain.ColorImages[imageIndex]);
        barriers[nBarriers++] = ColorAttachmentBarrier(swapchain.Images[imageIndex]);
        barriers[nBarriers++] = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.TopOfPipeBit, SrcAccessMask = AccessFlags2.None,
            DstStageMask = PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
            DstAccessMask = AccessFlags2.DepthStencilAttachmentWriteBit,
            OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.DepthAttachmentOptimal,
            Image = swapchain.DepthImages[imageIndex],
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = nBarriers, PImageMemoryBarriers = barriers
        };
        vk.CmdPipelineBarrier2(cmd, in dep);

        var colorAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = msaa ? swapchain.ColorViews[imageIndex] : swapchain.ImageViews[imageIndex],
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = msaa ? AttachmentStoreOp.DontCare : AttachmentStoreOp.Store,   // MSAA image is discarded after resolve
            ClearValue = new ClearValue { Color = new ClearColorValue(0, 0, 0, 1) }
        };
        if (msaa)   // resolve the multisampled image down into the single-sample swapchain image
        {
            colorAttachment.ResolveMode = ResolveModeFlags.AverageBit;
            colorAttachment.ResolveImageView = swapchain.ImageViews[imageIndex];
            colorAttachment.ResolveImageLayout = ImageLayout.ColorAttachmentOptimal;
        }
        var depthAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = swapchain.DepthViews[imageIndex],
            ImageLayout = ImageLayout.DepthAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.DontCare,
            ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
        };
        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D(new Offset2D(0, 0), swapchain.Extent),
            LayerCount = 1, ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachment,
            PDepthAttachment = &depthAttachment
        };
        vk.CmdBeginRendering(cmd, in rendering);
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, graphicsPipeline);
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, in descriptorSet, 0, null);

        var viewport = new Viewport
        {
            X = 0, Y = 0, Width = swapchain.Extent.Width, Height = swapchain.Extent.Height, MinDepth = 0, MaxDepth = 1
        };
        vk.CmdSetViewport(cmd, 0, 1, in viewport);
        var scissor = new Rect2D(new Offset2D(0, 0), swapchain.Extent);
        vk.CmdSetScissor(cmd, 0, 1, in scissor);

        // graphics: one indirect draw of the survivors the compute compacted. InstanceBase tells the
        // vertex shader which frame-in-flight region of visibleInstances to read.
        var pc = new PushConstants { ViewProj = view * proj, Time = scene.Time, BoneBase = boneBase, InstanceBase = instanceBase };
        vk.CmdPushConstants(cmd, pipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0, (uint)sizeof(PushConstants), &pc);
        mesh.DrawIndirect(cmd, drawCommandBuffer.Handle, cmdOffset);

        // static meshes (terrain / props): CPU frustum-cull and draw the visible ones into the SAME depth,
        // so they occlude + are occluded correctly (and feed the Hi-Z). reuses the Frustum/culler seam.
        if (scene.StaticObjects.Count > 0)
        {
            vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, staticPipeline);
            Frustum frustum = scene.Camera.GetFrustum(aspect);
            foreach (var so in scene.StaticObjects)
            {
                Vector3 wc = Vector3.Transform(so.BoundCenter, so.Model);
                if (!frustum.Intersects(wc, so.BoundRadius)) continue;
                var spc = new StaticPush { Model = so.Model, ViewProj = view * proj };
                vk.CmdPushConstants(cmd, staticPipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    0, (uint)sizeof(StaticPush), &spc);
                resources.GetMesh(so.Mesh).Draw(cmd, 1, 0);
            }
        }

        vk.CmdEndRendering(cmd);

        // build this image's Hi-Z from the depth we just rendered (used to occlusion-cull NEXT time this
        // image comes around). transition the depth attachment -> shader-read for the build first.
        var depthToRead = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.LateFragmentTestsBit, SrcAccessMask = AccessFlags2.DepthStencilAttachmentWriteBit,
            DstStageMask = PipelineStageFlags2.ComputeShaderBit, DstAccessMask = AccessFlags2.ShaderSampledReadBit,
            OldLayout = ImageLayout.DepthAttachmentOptimal, NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            Image = swapchain.DepthImages[imageIndex],
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        var depthDep = new DependencyInfo { SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 1, PImageMemoryBarriers = &depthToRead };
        vk.CmdPipelineBarrier2(cmd, in depthDep);
        hiz.Build(cmd, imageIndex);
        pyramidBuilt[imageIndex] = true;

        // COLOR attachment -> PRESENT
        var toPresent = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.ColorAttachmentOutputBit, SrcAccessMask = AccessFlags2.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags2.BottomOfPipeBit, DstAccessMask = AccessFlags2.None,
            OldLayout = ImageLayout.ColorAttachmentOptimal, NewLayout = ImageLayout.PresentSrcKhr,
            Image = swapchain.Images[imageIndex],
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        var presentDep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 1, PImageMemoryBarriers = &toPresent
        };
        vk.CmdPipelineBarrier2(cmd, in presentDep);

        VkCheck.Check(vk.EndCommandBuffer(cmd), "end cmd");
    }

    public void DrawFrame(Scene scene, ResourceManager resources)
    {
        var fb = window.FramebufferSize;
        if (fb.X == 0 || fb.Y == 0) return;

        vk.WaitForFences(Device.Device, 1, in inFlightFences[currentFrame], true, ulong.MaxValue);

        if (framebufferResized)   // recreate at the top so this frame renders at the current size
        {
            framebufferResized = false;
            swapchain.Recreate();
            hiz.Recreate(swapchain.Extent, swapchain.DepthViews);
            pyramidBuilt = new bool[swapchain.ImageCount];
            UpdatePyramidDescriptors();
        }

        uint imageIndex = 0;
        Result acquired = Device.KhrSwapchain.AcquireNextImage(Device.Device, swapchain.Handle, ulong.MaxValue,
            imageAvailableSemaphores[currentFrame], default, ref imageIndex);
        if (acquired == Result.ErrorOutOfDateKhr) { swapchain.Recreate(); hiz.Recreate(swapchain.Extent, swapchain.DepthViews); pyramidBuilt = new bool[swapchain.ImageCount]; UpdatePyramidDescriptors(); return; }

        vk.ResetFences(Device.Device, 1, in inFlightFences[currentFrame]);
        vk.ResetCommandBuffer(commandBuffers[currentFrame], 0);
        RecordCommandBuffer(commandBuffers[currentFrame], imageIndex, scene, resources);

        var waitInfo = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = imageAvailableSemaphores[currentFrame],
            StageMask = PipelineStageFlags2.ColorAttachmentOutputBit
        };
        var signalInfo = new SemaphoreSubmitInfo
        {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = renderFinishedSemaphores[imageIndex],
            StageMask = PipelineStageFlags2.AllCommandsBit   // signal after the to-present transition, so present waits for it
        };
        var cmdInfo = new CommandBufferSubmitInfo
        {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = commandBuffers[currentFrame]
        };
        var submit = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            WaitSemaphoreInfoCount = 1, PWaitSemaphoreInfos = &waitInfo,
            CommandBufferInfoCount = 1, PCommandBufferInfos = &cmdInfo,
            SignalSemaphoreInfoCount = 1, PSignalSemaphoreInfos = &signalInfo
        };
        VkCheck.Check(vk.QueueSubmit2(Device.GraphicsQueue, 1, in submit, inFlightFences[currentFrame]), "submit2");

        var swapHandle = swapchain.Handle;
        var renderDone = renderFinishedSemaphores[imageIndex];
        var present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1, PWaitSemaphores = &renderDone,
            SwapchainCount = 1, PSwapchains = &swapHandle,
            PImageIndices = &imageIndex
        };
        Result presented = Device.KhrSwapchain.QueuePresent(Device.PresentQueue, in present);
        if (presented is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr) framebufferResized = true;

        currentFrame = (currentFrame + 1) % MaxFramesInFlight;
    }

    public void Dispose()
    {
        vk.DeviceWaitIdle(Device.Device);

        foreach (var s in renderFinishedSemaphores) vk.DestroySemaphore(Device.Device, s, null);
        foreach (var s in imageAvailableSemaphores) vk.DestroySemaphore(Device.Device, s, null);
        foreach (var f in inFlightFences) vk.DestroyFence(Device.Device, f, null);

        vk.DestroyCommandPool(Device.Device, commandPool, null);
        vk.DestroyPipeline(Device.Device, graphicsPipeline, null);
        vk.DestroyPipeline(Device.Device, cullPipeline, null);
        vk.DestroyPipeline(Device.Device, staticPipeline, null);
        vk.DestroyPipelineLayout(Device.Device, pipelineLayout, null);
        vk.DestroyPipelineLayout(Device.Device, computePipelineLayout, null);
        vk.DestroyPipelineLayout(Device.Device, staticPipelineLayout, null);
        vk.DestroyDescriptorPool(Device.Device, descriptorPool, null);
        vk.DestroyDescriptorSetLayout(Device.Device, descriptorSetLayout, null);

        hiz.Dispose();
        boneBuffer.Dispose();
        flipbookBuffer.Dispose();
        allInstanceBuffer.Dispose();
        visibleInstanceBuffer.Dispose();
        drawCommandBuffer.Dispose();

        swapchain.Dispose();
        Device.Dispose();
        surface.Dispose();
        instance.Dispose();
    }
}
