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
    public Matrix4x4 Mvp;
    public float Time;       // seconds; shader derives the flipbook from this
    public uint BoneBase;    // base index into the bone buffer = this frame-in-flight's region
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
    const uint MaxBones = 4096;   // bones = GROUPS; 64B/bone allocated once, per-frame upload sized to actual count

    readonly IWindow window;
    readonly Vk vk;
    bool framebufferResized;

    readonly GpuInstance instance;
    readonly Surface surface;
    public GpuDevice Device { get; }
    readonly Swapchain swapchain;

    PipelineLayout pipelineLayout;
    DescriptorSetLayout descriptorSetLayout;
    DescriptorPool descriptorPool;
    DescriptorSet descriptorSet;
    Pipeline graphicsPipeline;

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
    Matrix4x4[] pendingBoneMatrices = Array.Empty<Matrix4x4>();
    readonly List<Entity> visibleEntities = new();   // reused each frame (no per-frame List alloc)

    public Renderer(IWindow window)
    {
        this.window = window;
        instance = new GpuInstance(window);
        surface = new Surface(instance, window);
        Device = new GpuDevice(instance, surface);
        swapchain = new Swapchain(Device, surface, window);
        vk = Device.Vk;

        CreateCommandPool();

        maxTextures = Math.Min(4096u, Device.MaxBindlessTextures);

        flipbookParams = new FlipbookParam[maxTextures];
        flipbookBuffer = new GpuBuffer(Device, (ulong)(sizeof(FlipbookParam) * maxTextures),
            BufferUsageFlags.StorageBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        flipbookBuffer.Upload<FlipbookParam>(flipbookParams);

        // bone SSBO: one MAX_BONES region per frame-in-flight (double-buffered), identity-initialised
        boneBuffer = new GpuBuffer(Device, (ulong)sizeof(Matrix4x4) * MaxBones * MaxFramesInFlight,
            BufferUsageFlags.StorageBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        var identity = new Matrix4x4[MaxBones * MaxFramesInFlight];
        Array.Fill(identity, Matrix4x4.Identity);
        boneBuffer.Upload<Matrix4x4>(identity);

        CreateDescriptorSetLayout();
        CreatePipelineLayout();
        CreateGraphicsPipeline();
        CreateCommandBuffer();
        CreateSyncObjects();
        CreateDescriptorSet();
    }

    // ---- public API used by ResourceManager / Application ----

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

    // ---- init ----

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
        var bindings = stackalloc DescriptorSetLayoutBinding[3];
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

        var flags = stackalloc DescriptorBindingFlags[3];
        flags[0] = DescriptorBindingFlags.PartiallyBoundBit | DescriptorBindingFlags.UpdateAfterBindBit;
        flags[1] = 0;
        flags[2] = 0;

        var bindingFlags = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 3,
            PBindingFlags = flags
        };
        var info = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
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
            SampleShadingEnable = false, RasterizationSamples = SampleCountFlags.Count1Bit
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
        poolSizes[0] = new DescriptorPoolSize { Type = DescriptorType.CombinedImageSampler, DescriptorCount = maxTextures };
        poolSizes[1] = new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = 2 };

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
            SType = StructureType.WriteDescriptorSet, DstSet = descriptorSet, DstBinding = 2, DstArrayElement = 0,
            DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, PBufferInfo = &boneInfo
        };
        vk.UpdateDescriptorSets(Device.Device, 1, in boneWrite, 0, null);
    }

    // ---- per-frame ----

    void RecordCommandBuffer(CommandBuffer cmd, uint imageIndex, Scene scene, ResourceManager resources)
    {
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        VkCheck.Check(vk.BeginCommandBuffer(cmd, in begin), "begin cmd");

        // UNDEFINED -> COLOR/DEPTH attachment optimal
        var barriers = stackalloc ImageMemoryBarrier2[2];
        barriers[0] = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.TopOfPipeBit, SrcAccessMask = AccessFlags2.None,
            DstStageMask = PipelineStageFlags2.ColorAttachmentOutputBit, DstAccessMask = AccessFlags2.ColorAttachmentWriteBit,
            OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.ColorAttachmentOptimal,
            Image = swapchain.Images[imageIndex],
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        barriers[1] = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.TopOfPipeBit, SrcAccessMask = AccessFlags2.None,
            DstStageMask = PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
            DstAccessMask = AccessFlags2.DepthStencilAttachmentWriteBit,
            OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.DepthAttachmentOptimal,
            Image = swapchain.DepthImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 2, PImageMemoryBarriers = barriers
        };
        vk.CmdPipelineBarrier2(cmd, in dep);

        var colorAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = swapchain.ImageViews[imageIndex],
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue { Color = new ClearColorValue(0, 0, 0, 1) }
        };
        var depthAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = swapchain.DepthView,
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

        float aspect = swapchain.Extent.Width / (float)swapchain.Extent.Height;
        Matrix4x4 view = scene.Camera.ViewMatrix();
        Matrix4x4 proj = scene.Camera.ProjectionMatrix(aspect);

        // upload this frame's bone matrices into THIS frame-in-flight's region (fence already waited)
        uint boneBase = currentFrame * MaxBones;
        if (pendingBoneMatrices.Length > 0)
        {
            int n = Math.Min(pendingBoneMatrices.Length, (int)MaxBones);
            boneBuffer.Upload<Matrix4x4>(pendingBoneMatrices.AsSpan(0, n), (ulong)boneBase * (ulong)sizeof(Matrix4x4));
        }

        var registry = scene.Registry;
        registry.View<Transform, MeshRenderable>(visibleEntities);
        foreach (Entity e in visibleEntities)
        {
            Transform t = registry.Get<Transform>(e);
            MeshRenderable mr = registry.Get<MeshRenderable>(e);
            var pc = new PushConstants
            {
                Mvp = t.Matrix * view * proj,   // System.Numerics row-vector order; raw upload -> shader reads transpose = glm mvp
                Time = scene.Time,
                BoneBase = boneBase
            };
            vk.CmdPushConstants(cmd, pipelineLayout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0, (uint)sizeof(PushConstants), &pc);
            resources.GetMesh(mr.Mesh).Draw(cmd);
        }

        vk.CmdEndRendering(cmd);

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
        }

        uint imageIndex = 0;
        Result acquired = Device.KhrSwapchain.AcquireNextImage(Device.Device, swapchain.Handle, ulong.MaxValue,
            imageAvailableSemaphores[currentFrame], default, ref imageIndex);
        if (acquired == Result.ErrorOutOfDateKhr) { swapchain.Recreate(); return; }

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
            StageMask = PipelineStageFlags2.ColorAttachmentOutputBit
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
        vk.DestroyPipelineLayout(Device.Device, pipelineLayout, null);
        vk.DestroyDescriptorPool(Device.Device, descriptorPool, null);
        vk.DestroyDescriptorSetLayout(Device.Device, descriptorSetLayout, null);

        boneBuffer.Dispose();
        flipbookBuffer.Dispose();

        swapchain.Dispose();
        Device.Dispose();
        surface.Dispose();
        instance.Dispose();
    }
}
