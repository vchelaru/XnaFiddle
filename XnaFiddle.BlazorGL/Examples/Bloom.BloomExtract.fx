#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;

sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

// Bright-pass cutoff in [0,1]. Only the part of each channel ABOVE Threshold
// survives; everything dimmer is pushed to black so the later blur passes glow
// only around the bright shapes, not the whole frame. Set per-Run from C#
// (bloomExtract.Parameters["Threshold"]). Lower = more of the scene blooms.
float Threshold;

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

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

technique BasicColorDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
