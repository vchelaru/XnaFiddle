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
    // Subtract the threshold then renormalize by the remaining headroom so a pixel
    // that was already at full brightness stays at 1.0 after the cut. The max(...)
    // guard keeps the divide finite as Threshold approaches 1.
    float3 bright = saturate((c.rgb - Threshold) / max(1.0 - Threshold, 0.0001));
    return float4(bright, 1.0) * input.Color;
}

technique BasicColorDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
