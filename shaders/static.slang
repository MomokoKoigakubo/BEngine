// Static world-space mesh (terrain / props). No skinning, no instancing: each mesh is placed by its own
// model matrix and lit with the same sun + hemisphere ambient as the animated meshes.
//
// TEXTURING (coworker): uv flows through already and there's a faint procedural grid as the default
// albedo. To texture for real, replace the albedo block in fragMain. If you need to PICK a texture per
// vertex, add texIndex back:
//   1. VInput  += [[vk::location(3)]] float texIndex;   VOutput += nointerpolation float texIndex : TEXCOORD1;
//   2. vertMain: o.texIndex = input.texIndex;
//   3. CreateStaticPipeline: add the location-3 vertex attribute (R32Sfloat, offset 32)
//   4. fragMain: float3 albedo = textures[NonUniformResourceIndex(int(input.texIndex))].Sample(input.uv).rgb;

struct VInput
{
	[[vk::location(0)]] float3 pos;
	[[vk::location(1)]] float3 normal;
	[[vk::location(2)]] float2 uv;
};
struct VOutput
{
	float4 sv_position : SV_Position;
	float3 normal : NORMAL;   // world-space
	float2 uv : TEXCOORD0;
};
struct PushConstants
{
	float4x4 model;     // per static mesh (identity for world-space-baked chunks)
	float4x4 viewProj;
};
[[vk::push_constant]] PushConstants pc;

// same bindless texture array as the animated path (the descriptor set is already bound, layouts compatible)
[[vk::binding(0)]] Sampler2D textures[];

[shader("vertex")]
VOutput vertMain(VInput input)
{
	VOutput o;
	float4 world = mul(float4(input.pos, 1.0), pc.model);
	o.sv_position = mul(world, pc.viewProj);
	o.normal = mul(input.normal, (float3x3)pc.model);
	o.uv = input.uv;
	return o;
}

[shader("fragment")]
float4 fragMain(VOutput input) : SV_Target
{
	float3 N = normalize(input.normal);
	float3 sunDir = normalize(float3(0.4, 1.0, 0.3));
	float wrap = dot(N, sunDir) * 0.5 + 0.5;
	float3 sun = wrap * float3(1.0, 0.96, 0.86) * 0.7;
	float hemi = N.y * 0.5 + 0.5;
	float3 ambient = lerp(float3(0.20, 0.17, 0.16), float3(0.30, 0.34, 0.40), hemi);

	// ============ TERRAIN ALBEDO HOOK (coworker replaces this) ============
	// default: a faint grid (uv is world-space here) so the ground reads as a surface.
	float2 c = abs(frac(input.uv * 0.1) - 0.5);
	float grid = smoothstep(0.46, 0.5, max(c.x, c.y));
	float3 albedo = lerp(float3(0.40, 0.42, 0.38), float3(0.30, 0.32, 0.30), grid);
	// =====================================================================

	return float4(albedo * (ambient + sun), 1.0);
}
