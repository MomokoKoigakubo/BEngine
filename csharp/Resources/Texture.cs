using Silk.NET.Vulkan;
using StbImageSharp;
using IdleL.Rendering;

namespace IdleL.Resources;

// Loads an image, uploads it, generates a full mip chain via blits, and creates a view + sampler.
unsafe class Texture : IDisposable
{
    readonly Vk vk;
    readonly GpuDevice device;
    Image image;
    DeviceMemory memory;
    public ImageView View;
    public Sampler Sampler;
    public int Width;
    public int Height;

    public Texture(GpuDevice device, string path)
    {
        this.device = device;
        vk = device.Vk;

        ImageResult img = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        int w = img.Width, h = img.Height;
        Width = w; Height = h;
        ulong imageSize = (ulong)(w * h * 4);
        uint mipLevels = (uint)MathF.Floor(MathF.Log2(Math.Max(w, h))) + 1;

        using var staging = new GpuBuffer(device, imageSize, BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        staging.Upload<byte>(img.Data);

        device.CreateImage((uint)w, (uint)h, mipLevels, Format.R8G8B8A8Srgb,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.SampledBit,
            out image, out memory);

        device.ImmediateSubmit(cmd =>
        {
            // 1. UNDEFINED -> TRANSFER_DST (whole chain)
            Barrier(cmd, ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
                PipelineStageFlags2.TopOfPipeBit, AccessFlags2.None,
                PipelineStageFlags2.CopyBit, AccessFlags2.TransferWriteBit, 0, mipLevels);

            // 2. copy staging -> mip 0
            var region = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D((uint)w, (uint)h, 1)
            };
            vk.CmdCopyBufferToImage(cmd, staging.Handle, image, ImageLayout.TransferDstOptimal, 1, in region);

            // 3. blit chain
            int mipW = w, mipH = h;
            for (uint i = 1; i < mipLevels; i++)
            {
                Barrier(cmd, ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal,
                    PipelineStageFlags2.CopyBit, AccessFlags2.TransferWriteBit,
                    PipelineStageFlags2.BlitBit, AccessFlags2.TransferReadBit, i - 1, 1);

                var blit = new ImageBlit
                {
                    SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, i - 1, 0, 1),
                    DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, i, 0, 1)
                };
                blit.SrcOffsets[0] = new Offset3D(0, 0, 0);
                blit.SrcOffsets[1] = new Offset3D(mipW, mipH, 1);
                blit.DstOffsets[0] = new Offset3D(0, 0, 0);
                blit.DstOffsets[1] = new Offset3D(mipW > 1 ? mipW / 2 : 1, mipH > 1 ? mipH / 2 : 1, 1);
                vk.CmdBlitImage(cmd, image, ImageLayout.TransferSrcOptimal, image, ImageLayout.TransferDstOptimal,
                    1, in blit, Filter.Linear);

                Barrier(cmd, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal,
                    PipelineStageFlags2.BlitBit, AccessFlags2.TransferReadBit,
                    PipelineStageFlags2.FragmentShaderBit, AccessFlags2.ShaderReadBit, i - 1, 1);

                if (mipW > 1) mipW /= 2;
                if (mipH > 1) mipH /= 2;
            }

            // last level: TRANSFER_DST -> SHADER_READ
            Barrier(cmd, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags2.CopyBit, AccessFlags2.TransferWriteBit,
                PipelineStageFlags2.FragmentShaderBit, AccessFlags2.ShaderReadBit, mipLevels - 1, 1);
        });

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Srgb,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, mipLevels, 0, 1)
        };
        VkCheck.Check(vk.CreateImageView(device.Device, in viewInfo, null, out View), "texture view");

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,     // crisp pixels (Blockbench look)
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            MipmapMode = SamplerMipmapMode.Linear,   // trilinear between mips
            MinLod = 0f,
            MaxLod = mipLevels,
            AnisotropyEnable = false,
            BorderColor = BorderColor.IntOpaqueBlack
        };
        VkCheck.Check(vk.CreateSampler(device.Device, in samplerInfo, null, out Sampler), "sampler");
    }

    void Barrier(CommandBuffer cmd, ImageLayout oldLayout, ImageLayout newLayout,
        PipelineStageFlags2 srcStage, AccessFlags2 srcAccess,
        PipelineStageFlags2 dstStage, AccessFlags2 dstAccess, uint baseMip, uint levelCount)
    {
        var b = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = srcStage,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStage,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, baseMip, levelCount, 0, 1)
        };
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &b
        };
        vk.CmdPipelineBarrier2(cmd, in dep);
    }

    public void Dispose()
    {
        vk.DestroySampler(device.Device, Sampler, null);
        vk.DestroyImageView(device.Device, View, null);
        vk.DestroyImage(device.Device, image, null);
        vk.FreeMemory(device.Device, memory, null);
    }
}
