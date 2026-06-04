#if OPENGL
	#define SV_POSITION POSITION
	#define PS_SHADERMODEL ps_3_0
#else
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4 BloomThreshold;
float BloomIntensity;
float BloomSaturation;

sampler TextureSampler : register(s0);

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color    : COLOR0;
	float2 TexCoord : TEXCOORD0;
};

float4 BloomPass(VertexShaderOutput input) : COLOR
{
	float4 color = tex2D(TextureSampler, input.TexCoord);
	color = saturate(color - BloomThreshold) * BloomIntensity + color;
	color = saturate(color);
	color = lerp(color, color.rgba + color.rgba * BloomSaturation, BloomSaturation);
	return color;
}

technique Bloom
{
	pass Pass1
	{
		PixelShader = compile PS_SHADERMODEL BloomPass();
	}
}
