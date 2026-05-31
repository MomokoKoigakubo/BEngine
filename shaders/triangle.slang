struct VertexInput
{
	[[vk::location(0)]] float2 pos;
	[[vk::location(1)]] float3 colour;
};

struct VertexOutput
{
	float4 sv_position : SV_Position;
	float3 colour : COLOR0;
};

struct PushConstants
{
	float aspectScaleX;
};
[[vk::push_constant]] PushConstants pc;

[shader("vertex")]
VertexOutput vertMain(VertexInput input)
{
	VertexOutput output;
	float2 pos = input.pos;
	pos.x *= pc.aspectScaleX;
	output.sv_position = float4(pos, 0.0, 1.0);
	output.colour = input.colour;
	return output;
}

[shader("fragment")]
float4 fragMain(VertexOutput input) : SV_Target
{
	return float4(input.colour, 1.0);
}