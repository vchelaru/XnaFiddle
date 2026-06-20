#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// TWO textures sampled in ONE pass. SpriteBatch binds the sprite you Draw() to slot 0
// (SpriteTexture); the mask is a SECOND texture you set from C# as the MaskTexture
// parameter. See Masking.cs for the C# side.
Texture2D SpriteTexture;                 // slot 0 — the image SpriteBatch is drawing
sampler2D ImageSampler = sampler_state { Texture = <SpriteTexture>; };

Texture2D MaskTexture;                    // the mask, set from C#: effect.Parameters["MaskTexture"]
sampler2D MaskSampler = sampler_state { Texture = <MaskTexture>; };

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float4 col = tex2D(ImageSampler, input.TextureCoordinates) * input.Color;

    // The mask is sampled with the SAME UVs as the sprite, so it stretches to map 1:1
    // over the image regardless of either texture's pixel size. We read the red channel
    // (the mask is grayscale): white (1) keeps the pixel, black (0) hides it, and the
    // soft edge in between gives partial transparency.
    //
    // This lowers ONLY alpha (straight, non-premultiplied alpha), so the C# side must draw
    // with BlendState.NonPremultiplied — see the note in Masking.cs for why AlphaBlend
    // would darken instead of mask.
    float mask = tex2D(MaskSampler, input.TextureCoordinates).r;
    col.a *= mask;

    return col;
}

technique BasicColorDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
