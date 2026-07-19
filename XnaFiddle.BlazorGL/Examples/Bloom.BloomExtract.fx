#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#elif defined(SM6) || defined(VULKAN)
	// MonoGame's native Content Builder (DX12/Vulkan) compiles via DXC, which requires Shader
	// Model 6 and rejects the classic sampler2D/tex2D combo below -- see the Texture2D/SamplerState
	// branch and the SM6 sampling call site further down.
	#define VS_SHADERMODEL vs_6_0
	#define PS_SHADERMODEL ps_6_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#if defined(SM6) || defined(VULKAN)
Texture2D<float4> SpriteTexture : register(t0);
SamplerState SpriteTextureSampler : register(s0);
#else
Texture2D SpriteTexture;

sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};
#endif

// Bright-pass cutoff in [0,1]. A pixel blooms only when its brightness (max channel)
// exceeds Threshold; the whole color is then scaled uniformly by how far above it is,
// so hue is preserved and dimmer pixels fade to black -- the later blur passes glow
// only around the bright shapes, not the whole frame. Set per-Run from C#
// (bloomExtract.Parameters["Threshold"]). Lower = more of the scene blooms.
float Threshold;

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

// ShadowDusk's OpenGL translator (and the real MonoGame Content Builder's DXC pass) rewrite/validate
// the pixel shader entry point by matching a literal, unconditioned `: COLOR`/`tex2D(...)` shape --
// splitting the signature or a call site across an #if breaks that (verified against the real
// ShadowDuskCLI tool and a real DX12 Content Builder build while fixing issue #52's SM6 gap: SM6
// also requires `SV_Target0` instead of `COLOR`). So the two shader models get fully separate,
// self-contained entry points instead of one shared function with an internal #if.
#if defined(SM6) || defined(VULKAN)
float4 MainPS(VertexShaderOutput input) : SV_Target0
{
    float4 c = SpriteTexture.Sample(SpriteTextureSampler, input.TextureCoordinates);
    // Gate on overall brightness (the max channel) and scale the ORIGINAL color by the
    // same factor on every channel. Uniform scaling preserves hue, so an orange square
    // blooms orange and cyan blooms cyan. (The old per-channel threshold crushed each
    // color's weaker channel, collapsing every neon hue toward a pure R/G/B primary.)
    float brightness = max(c.r, max(c.g, c.b));
    float k = saturate((brightness - Threshold) / max(1.0 - Threshold, 0.0001));
    return float4(c.rgb * k, 1.0) * input.Color;
}
#else
float4 MainPS(VertexShaderOutput input) : COLOR
{
    float4 c = tex2D(SpriteTextureSampler, input.TextureCoordinates);
    // Gate on overall brightness (the max channel) and scale the ORIGINAL color by the
    // same factor on every channel. Uniform scaling preserves hue, so an orange square
    // blooms orange and cyan blooms cyan. (The old per-channel threshold crushed each
    // color's weaker channel, collapsing every neon hue toward a pure R/G/B primary.)
    float brightness = max(c.r, max(c.g, c.b));
    float k = saturate((brightness - Threshold) / max(1.0 - Threshold, 0.0001));
    return float4(c.rgb * k, 1.0) * input.Color;
}
#endif

technique BasicColorDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
