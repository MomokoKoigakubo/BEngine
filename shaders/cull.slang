// GPU frustum + occlusion culling. one thread per instance: frustum-test its world bounding sphere,
// then Hi-Z occlusion-test it (project to screen, compare nearest depth vs the farthest drawn depth in
// that region). survivors are compacted into visibleInstances and counted into the indirect command.

struct CullData
{
	float4x4 viewProj;     // frustum planes (extracted below) + occlusion projection
	float4 modelBound;     // shared model-space sphere: xyz center, w radius
	uint instanceCount;    // instances to test
	uint outputBase;       // visibleInstances base slot for this frame-in-flight
	uint commandIndex;     // which indirect command to fill
	uint imageIndex;       // which swapchain image's Hi-Z to sample
	uint screenW;
	uint screenH;
	uint mipCount;
	uint occlusionOn;      // 0 = skip occlusion (e.g. before this image has a valid pyramid)
};
[[vk::push_constant]] CullData pc;

[[vk::binding(4)]] StructuredBuffer<float4x4> allInstances;        // input: every instance's model matrix
[[vk::binding(3)]] RWStructuredBuffer<float4x4> visibleInstances;  // output: survivors, compacted
[[vk::binding(5)]] RWStructuredBuffer<uint> drawCommands;          // 5 uints/command
[[vk::binding(6)]] Sampler2D pyramids[];                          // per-swapchain-image Hi-Z depth pyramids

bool insideFrustum(float3 c, float r)
{
	// Gribb-Hartmann from the COLUMNS of viewProj (our row-vector + Vulkan [0,1] depth)
	float4 r0 = pc.viewProj[0], r1 = pc.viewProj[1], r2 = pc.viewProj[2], r3 = pc.viewProj[3];
	float4 col0 = float4(r0.x, r1.x, r2.x, r3.x);
	float4 col1 = float4(r0.y, r1.y, r2.y, r3.y);
	float4 col2 = float4(r0.z, r1.z, r2.z, r3.z);
	float4 col3 = float4(r0.w, r1.w, r2.w, r3.w);
	float4 planes[6] = { col3 + col0, col3 - col0, col3 + col1, col3 - col1, col2, col3 - col2 };
	for (int i = 0; i < 6; i++)
	{
		float3 n = planes[i].xyz;
		float len = length(n);
		float d = (len > 0.0) ? (dot(n, c) / len + planes[i].w / len) : planes[i].w;
		if (d < -r) return false;
	}
	return true;
}

bool occluded(float3 c, float r)
{
	if (pc.occlusionOn == 0) return false;
	// project the sphere's world AABB corners -> screen AABB + nearest depth
	float2 uvMin = float2(1e9, 1e9), uvMax = float2(-1e9, -1e9);
	float nearestZ = 1e9;
	[unroll] for (int i = 0; i < 8; i++)
	{
		float3 corner = c + float3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1) * r;
		float4 clip = mul(float4(corner, 1.0), pc.viewProj);
		if (clip.w <= 0.0) return false;   // straddles the camera -> don't occlusion-cull
		float3 ndc = clip.xyz / clip.w;
		float2 uv = ndc.xy * 0.5 + 0.5;
		uvMin = min(uvMin, uv); uvMax = max(uvMax, uv);
		nearestZ = min(nearestZ, ndc.z);
	}
	// pick the mip where the footprint is ~1 texel, then sample the 4 AABB corners (farthest = occluder)
	float2 sizePx = (uvMax - uvMin) * float2(pc.screenW, pc.screenH);
	float mip = clamp(ceil(log2(max(max(sizePx.x, sizePx.y), 1.0))), 0.0, float(pc.mipCount - 1));
	Sampler2D hz = pyramids[pc.imageIndex];
	float o = max(max(hz.SampleLevel(float2(uvMin.x, uvMin.y), mip).r, hz.SampleLevel(float2(uvMax.x, uvMin.y), mip).r),
	              max(hz.SampleLevel(float2(uvMin.x, uvMax.y), mip).r, hz.SampleLevel(float2(uvMax.x, uvMax.y), mip).r));
	return nearestZ > o;   // even the closest part is behind everything drawn here -> occluded
}

[shader("compute")]
[numthreads(64, 1, 1)]
void cullMain(uint3 tid : SV_DispatchThreadID)
{
	uint i = tid.x;
	if (i >= pc.instanceCount) return;

	float4x4 model = allInstances[i];
	float3 center = mul(float4(pc.modelBound.xyz, 1.0), model).xyz;   // world sphere center (translation-only ok)
	float radius = pc.modelBound.w;

	if (!insideFrustum(center, radius)) return;
	if (occluded(center, radius)) return;

	uint slot;
	InterlockedAdd(drawCommands[pc.commandIndex * 5 + 1], 1, slot);
	visibleInstances[pc.outputBase + slot] = model;
}
