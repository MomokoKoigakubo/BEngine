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
	float3 normal : NORMAL;          // world-space (model transform baked in cubeBuilder)
	float2 uv : TEXCOORD0;
	nointerpolation float texIndex : TEXCOORD1;
};

struct PushConstants
{
	float4x4 mvp;
	float time; //seconds > derived pertext
	uint boneBase; //base index into the bone buffer for this frame-in-flight's region
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

[shader("vertex")]
VertexOutput vertMain(VertexInput input)
{
	VertexOutput output;
	uint bi = pc.boneBase + uint(input.boneIndex);   // index into this frame's bone region
	float4 world = mul(boneMatrices[bi], float4(input.pos, 1.0));   // skin: bone matrix applies the group hierarchy
	output.sv_position = mul(pc.mvp, world);
	output.normal = mul((float3x3)boneMatrices[bi], input.normal); // rotate the normal by the bone too
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
