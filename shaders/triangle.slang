struct VertexInput
{
	[[vk::location(0)]] float3 pos;
	[[vk::location(1)]] float3 normal;
	[[vk::location(2)]] float2 uv;
};

struct VertexOutput
{
	float4 sv_position : SV_Position;
	float2 uv : TEXCOORD0;
};

struct PushConstants
{
	float4x4 mvp;
};
[[vk::push_constant]] PushConstants pc;

// binding 0 = combined image sampler (matches the descriptor set layout)
[[vk::binding(0)]] Sampler2D tex;

[shader("vertex")]
VertexOutput vertMain(VertexInput input)
{
	VertexOutput output;
	output.sv_position = mul(pc.mvp, float4(input.pos, 1.0));
	output.uv = input.uv;
	return output;
}

[shader("fragment")]
float4 fragMain(VertexOutput input) : SV_Target
{
float4 color = tex.Sample(input.uv);
if(color.a < 0.1) discard;
return color;
}
