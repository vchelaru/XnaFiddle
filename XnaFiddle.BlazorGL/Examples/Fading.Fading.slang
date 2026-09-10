// Vertical fade effect for SpriteBatch: transparent at the top, solid at the bottom.
Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(SpriteTextureSampler, uv) * color;
    // Fade vertically: top transparent, bottom full.
    col.rgba = col.rgba * (uv.y * 2 - 0.4f);
    return col;
}
