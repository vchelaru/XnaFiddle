// Alpha-masking effect for SpriteBatch: a second grayscale texture drives the sprite's alpha.
// SpriteBatch binds the sprite being drawn to the first texture declared here; MaskTexture is
// set from C#: effect.Parameters["MaskTexture"].
Texture2D SpriteTexture;
SamplerState ImageSampler;

Texture2D MaskTexture;
SamplerState MaskSampler;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(ImageSampler, uv) * color;

    // The mask is sampled with the SAME UVs as the sprite, so it stretches to map 1:1
    // over the image regardless of either texture's pixel size. We read the red channel
    // (the mask is grayscale): white (1) keeps the pixel, black (0) hides it, and the
    // soft edge in between gives partial transparency.
    //
    // This lowers ONLY alpha (straight, non-premultiplied alpha), so the C# side must draw
    // with BlendState.NonPremultiplied -- see the note in Masking.cs for why AlphaBlend
    // would darken instead of mask.
    float mask = MaskTexture.Sample(MaskSampler, uv).r;
    col.a *= mask;

    return col;
}
