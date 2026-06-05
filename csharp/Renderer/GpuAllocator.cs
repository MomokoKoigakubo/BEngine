using Silk.NET.Vulkan;

namespace IdleL.Rendering
{
	struct Allocation
	{
		public DeviceMemory MemOrigin;
		public ulong MemOffset;
		public ulong MemSize;
		public MemoryBlock BlockRef;
	};

	// one big VkDeviceMemory chunk, + a list of the ranges inside it that are still free.
	unsafe class MemoryBlock
	{
		public DeviceMemory Memory;
		public readonly ulong Size;
		readonly List<(ulong Offset, ulong Size)> freeRanges = new();

		public MemoryBlock(Vk vk, Device device, ulong blockSize, uint memoryTypeIndex)
		{
			Size = blockSize;
			var info = new MemoryAllocateInfo
			{
				SType = StructureType.MemoryAllocateInfo,
				AllocationSize = blockSize,
				MemoryTypeIndex = memoryTypeIndex
			};
			VkCheck.Check(vk.AllocateMemory(device, in info, null, out Memory), "block AllocateMemory");
			freeRanges.Add((0, blockSize));   // whole block free to start
		}

		// first-fit: find a free range this fits in (after aligning the start up), then carve it out.
		public bool TryAllocate(ulong size, ulong alignment, out ulong offset)
		{
			for (int i = 0; i < freeRanges.Count; i++)
			{
				ulong rOff = freeRanges[i].Offset;
				ulong rSize = freeRanges[i].Size;
				ulong start = (rOff + alignment - 1) / alignment * alignment;   // round the start up to alignment
				if (start + size > rOff + rSize) continue;                      // doesn't fit in this range

				freeRanges.RemoveAt(i);
				ulong pad = start - rOff;                       // alignment gap left before the allocation
				if (pad > 0) freeRanges.Add((rOff, pad));
				ulong after = rOff + rSize - (start + size);    // leftover after the allocation
				if (after > 0) freeRanges.Add((start + size, after));

				offset = start;
				return true;
			}
			offset = 0;
			return false;
		}

		// give a range back and merge it with any neighbours, so the block doesn't fragment into slivers.
		public void Free(ulong offset, ulong size)
		{
			freeRanges.Add((offset, size));
			freeRanges.Sort((a, b) => a.Offset.CompareTo(b.Offset));
			for (int i = 0; i < freeRanges.Count - 1; )
			{
				var cur = freeRanges[i];
				var next = freeRanges[i + 1];
				if (cur.Offset + cur.Size == next.Offset)   // adjacent, swallow next into cur
				{
					freeRanges[i] = (cur.Offset, cur.Size + next.Size);
					freeRanges.RemoveAt(i + 1);   // don't advance i, cur might now touch the next one too
				}
				else i++;
			}
		}
	}

	// owns a pool of memory blocks per memory type and suballocates out of them, instead of doing a
	// vkAllocateMemory per buffer (drivers cap the total number of allocations, ~4096 on some cards).
	unsafe class GpuAllocator
	{
		readonly Vk vk;
		readonly Device device;
		readonly Dictionary<uint, List<MemoryBlock>> pools = new();   // memoryTypeIndex -> its blocks
		const ulong DefaultBlockSize = 64ul * 1024 * 1024;            // 64 MB chunks

		public GpuAllocator(Vk vk, Device device)
		{
			this.vk = vk;
			this.device = device;
		}

		public Allocation Allocate(ulong size, ulong alignment, uint memoryTypeIndex)
		{
			if (!pools.TryGetValue(memoryTypeIndex, out var blocks))
			{
				blocks = new List<MemoryBlock>();
				pools[memoryTypeIndex] = blocks;
			}

			foreach (MemoryBlock block in blocks)
				if (block.TryAllocate(size, alignment, out ulong off))
					return new Allocation { MemOrigin = block.Memory, MemOffset = off, MemSize = size, BlockRef = block };

			// nothing had room, grow a new block. size it to fit even a single huge allocation.
			ulong blockSize = Math.Max(DefaultBlockSize, size);
			var fresh = new MemoryBlock(vk, device, blockSize, memoryTypeIndex);
			blocks.Add(fresh);
			fresh.TryAllocate(size, alignment, out ulong offset);   // can't fail on a brand new block
			return new Allocation { MemOrigin = fresh.Memory, MemOffset = offset, MemSize = size, BlockRef = fresh };
		}

		public void Free(Allocation a) => a.BlockRef.Free(a.MemOffset, a.MemSize);

		public void Dispose()
		{
			foreach (var blocks in pools.Values)
				foreach (MemoryBlock block in blocks)
					vk.FreeMemory(device, block.Memory, null);
			pools.Clear();
		}
	}
}
