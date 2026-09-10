// Color-inverting effect for SpriteBatch.
Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(SpriteTextureSampler, uv) * color;
    col.rgb = 1 - col.rgb; // Invert color
    return col;
}
