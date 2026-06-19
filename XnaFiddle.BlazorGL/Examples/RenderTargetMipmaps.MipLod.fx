#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Explicit-LOD sampling probe. The host draws the SAME mipmapped render target six
// times with Lod = 0..5; this shader samples that exact mip level so the human can
// see the chain go sharp -> blurry.
//
// CRITICAL: the legacy intrinsic tex2Dlod does NOT compile on this target. The only
// way to pick a mip level explicitly here is the modern texture-object method
// Texture.SampleLevel(sampler, uv, lod). Declaring SpriteTexture + SpriteSampler
// FIRST binds them to texture/sampler slot 0 -- the slot SpriteBatch sets the drawn
// texture into -- so SampleLevel reads whatever sprite is currently being drawn.
Texture2D SpriteTexture;
SamplerState SpriteSampler;

// Which mip level to sample. Set per-draw from C# (0,1,2,...) before each thumbnail.
float Lod;

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
	float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
	float4 c = SpriteTexture.SampleLevel(SpriteSampler, input.TextureCoordinates, Lod);
	return c * input.Color;
}

technique BasicColorDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
