// Hi-Z depth pyramid build. copyMain: MSAA depth sample-0 -> mip 0. reduceMain: max of the 2x2 in the
// parent mip -> this mip (so a coarse texel holds the FARTHEST surface in its region = conservative).

struct PC
{
	uint dstMip;
	uint dstW;   // size of the mip being written
	uint dstH;
	uint _pad;
};
[[vk::push_constant]] PC pc;

[[vk::binding(0)]] Texture2DMS<float> depth;                   // the multisampled depth (we take sample 0)
[[vk::binding(1)]] [format("r32f")] RWTexture2D<float> pyramid[];   // one storage view per mip level

[shader("compute")]
[numthreads(8, 8, 1)]
void copyMain(uint3 id : SV_DispatchThreadID)
{
	if (id.x >= pc.dstW || id.y >= pc.dstH) return;
	pyramid[0][id.xy] = depth.Load(int2(id.xy), 0);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void reduceMain(uint3 id : SV_DispatchThreadID)
{
	if (id.x >= pc.dstW || id.y >= pc.dstH) return;
	uint src = pc.dstMip - 1;
	uint2 p = id.xy * 2;
	float d0 = pyramid[src][p + uint2(0, 0)];
	float d1 = pyramid[src][p + uint2(1, 0)];
	float d2 = pyramid[src][p + uint2(0, 1)];
	float d3 = pyramid[src][p + uint2(1, 1)];
	pyramid[pc.dstMip][id.xy] = max(max(d0, d1), max(d2, d3));   // farthest of the 4
}
