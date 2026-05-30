
static float2 positions[3] = {
	float2(0.0, -0.5),
	float2(0.5, 0.5),
	float2(-0.5, 0.5)
};

struct VertexOutput {
	float4 sv_position : SV_position;
};

struct PushConstants
{
	float aspectScaleX;
};
[[vk::push_constant]] PushConstants pc;

[shader("vertex")]
VertexOutput vertMain(uint vid : SV_VertexID) 
{
	VertexOutput output;
	float2 pos = positions[vid];
	pos.x *= pc.aspectScaleX;
	output.sv_position = float4(pos, 0.0, 1.0);
	return output;
}

[shader("fragment")]
float4 fragMain(VertexOutput input) : SV_Target
{
	return float4(1.0, 0.0, 0.0, 1.0);
}