#if OPENGL
	#define SV_POSITION POSITION
	#define PS_SHADERMODEL ps_3_0
#elif defined(SM6) || defined(VULKAN)
	// MonoGame's native Content Builder (DX12/Vulkan) compiles via DXC, which requires Shader
	// Model 6 and rejects the classic `sampler TextureSampler : register(s0);` binding below --
	// see the Texture2D/SamplerState branch and the SM6 sampling call site further down.
	#define PS_SHADERMODEL ps_6_0
#else
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4 BloomThreshold;
float BloomIntensity;
float BloomSaturation;

#if defined(SM6) || defined(VULKAN)
Texture2D<float4> SpriteTexture : register(t0);
SamplerState TextureSampler : register(s0);
#else
sampler TextureSampler : register(s0);
#endif

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color    : COLOR0;
	float2 TexCoord : TEXCOORD0;
};

// ShadowDusk's OpenGL translator (and the real MonoGame Content Builder's DXC pass) rewrite/validate
// the pixel shader entry point by matching a literal, unconditioned `: COLOR`/`tex2D(...)` shape --
// splitting the signature or a call site across an #if breaks that (verified against the real
// ShadowDuskCLI tool and a real DX12 Content Builder build while fixing issue #52's SM6 gap: SM6
// also requires `SV_Target0` instead of `COLOR`). So the two shader models get fully separate,
// self-contained entry points instead of one shared function with an internal #if.
#if defined(SM6) || defined(VULKAN)
float4 BloomPass(VertexShaderOutput input) : SV_Target0
{
	float4 color = SpriteTexture.Sample(TextureSampler, input.TexCoord);
	color = saturate(color - BloomThreshold) * BloomIntensity + color;
	color = saturate(color);
	color = lerp(color, color.rgba + color.rgba * BloomSaturation, BloomSaturation);
	return color;
}
#else
float4 BloomPass(VertexShaderOutput input) : COLOR
{
	float4 color = tex2D(TextureSampler, input.TexCoord);
	color = saturate(color - BloomThreshold) * BloomIntensity + color;
	color = saturate(color);
	color = lerp(color, color.rgba + color.rgba * BloomSaturation, BloomSaturation);
	return color;
}
#endif

technique Bloom
{
	pass Pass1
	{
		PixelShader = compile PS_SHADERMODEL BloomPass();
	}
}
