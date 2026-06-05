struct VertexInput
{
	[[vk::location(0)]] float3 pos;
	[[vk::location(1)]] float3 normal;
	[[vk::location(2)]] float2 uv;
	[[vk::location(3)]] float texIndex;
	[[vk::location(4)]] float boneIndex;
};

struct VertexOutput
{
	float4 sv_position : SV_Position;
	float3 normal : NORMAL;          // world-space
	float2 uv : TEXCOORD0;
	nointerpolation float texIndex : TEXCOORD1;
};

struct PushConstants
{
	float4x4 viewProj;  // per-instance model now comes from instanceModels[], so only view*proj is pushed
	float time;         // seconds > derived pertext
	uint boneBase;      // base index into the bone buffer for this frame-in-flight's region
	uint instanceBase;  // base index into the instance buffer. SV_InstanceID is 0-based (HLSL), so we add this ourselves
};
[[vk::push_constant]] PushConstants pc;

// binding 0 = bindless array of combined image samplers
[[vk::binding(0)]] Sampler2D textures[];

struct FlipbookParam
{
	float frameCount;
	float flipRate;
};
[[vk::binding(1)]] StructuredBuffer<FlipbookParam> flipbooks;

// binding 2 = one float4x4 per bone (skinning). Bind-pose matrices render identical.
[[vk::binding(2)]] StructuredBuffer<float4x4> boneMatrices;

// binding 3 = one float4x4 model transform per instance, indexed by SV_InstanceID.
[[vk::binding(3)]] StructuredBuffer<float4x4> instanceModels;

[shader("vertex")]
VertexOutput vertMain(VertexInput input, uint instanceId : SV_InstanceID)
{
	VertexOutput output;
	uint bi = pc.boneBase + uint(input.boneIndex);              // index into this frame's bone region
	// Row-vector convention (matches System.Numerics, row-major layout): v * bone * instance * viewProj,
	// read left-to-right. No transpose anywhere — the matrix here IS the matrix in C#.
	uint ii = pc.instanceBase + instanceId;                          // absolute slot (SV_InstanceID is 0-based per draw)
	float4 skinned = mul(float4(input.pos, 1.0), boneMatrices[bi]);   // skin: bone matrix applies the group hierarchy
	float4 world   = mul(skinned, instanceModels[ii]);               // place this instance in the world
	output.sv_position = mul(world, pc.viewProj);
	float3 n = mul(input.normal, (float3x3)boneMatrices[bi]);        // rotate the normal by the bone...
	output.normal = mul(n, (float3x3)instanceModels[ii]);            // ...then by the instance transform
	output.uv = input.uv;
	output.texIndex = input.texIndex;
	return output;
}

[shader("fragment")]
float4 fragMain(VertexOutput input) : SV_Target
{
	int idx = int(input.texIndex);

	FlipbookParam fb = flipbooks[idx];
	// flipbook: remap V into the current frame's band (flipCount=1 -> unchanged)
	float2 uv = input.uv;
	if (fb.frameCount > 1.0)
	{
		float frame = floor(fmod(pc.time * fb.flipRate, fb.frameCount));
		uv.y = (frame + uv.y) / fb.frameCount;
	}

	float4 albedo = textures[NonUniformResourceIndex(idx)].Sample(uv);
	if (albedo.a < 0.1) discard;   // alpha cutout

	float3 N = normalize(input.normal);

	// lighting
	// soft directional "sun", half_Lambert wrap so the shadow side stays gentle.
	float3 sunDir   = normalize(float3(0.4, 1.0, 0.3));
	float3 sunColor = float3(1.0, 0.96, 0.86);
	float  wrap     = dot(N, sunDir) * 0.5 + 0.5;     // 0..1
	float3 sun      = wrap * sunColor * 0.7;          // <1 so lit faces don't clamp to white

	// hemisphere ambient: warm ground fill below, cool sky fill above. Low floor so shadows read.
	float  hemi     = N.y * 0.5 + 0.5;
	float3 ambient  = lerp(float3(0.20, 0.17, 0.16),  // ground
	                       float3(0.30, 0.34, 0.40),  // sky
	                       hemi);

	float3 lit = albedo.rgb * (ambient + sun);        // ~0.2 (shadow) .. ~0.9 (lit)
	return float4(lit, 1.0);
}
